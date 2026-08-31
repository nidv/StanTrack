using Microsoft.EntityFrameworkCore;
using StanTrack.Data;
using StanTrack.Dtos;
using StanTrack.Interfaces;
using StanTrack.Models;

namespace StanTrack.BackgroundJobs
{
    public class EventSyncService
    {
        private readonly IUnitOfWork _uow;
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly IReadOnlyList<IEventFetchService> _allFetchers;
        private readonly ILogger<EventSyncService> _logger;

        // Each fetcher is only relevant for specific celebrity categories. Skipping irrelevant
        // fetches avoids bogus 0-result calls and (for MusicBrainz) is the single biggest
        // runtime saving in the sync sweep because of its 1 req/s static throttle.
        // Names match what the seeders tag celebrities with — bulk-mode writes "Musician",
        // CSV-mode writes "Artist" and "K-Pop".
        private static readonly HashSet<string> MusicCategories = new(StringComparer.OrdinalIgnoreCase)
        {
            "Musician", "Artist", "K-Pop", "K-pop"
        };
        private static readonly HashSet<string> FilmCategories = new(StringComparer.OrdinalIgnoreCase)
        {
            "Actor", "Actress"
        };

        // Source-name -> categories that fetcher applies to. Anything not in the set is skipped.
        // Implicit contract: sources absent from this dict are called for every celebrity.
        private static readonly Dictionary<string, HashSet<string>> CategoriesBySource = new(StringComparer.OrdinalIgnoreCase)
        {
            ["MusicBrainz"] = MusicCategories,
            ["Ticketmaster"] = MusicCategories,
            ["TMDb"] = FilmCategories,
        };

        // MusicBrainz returns future releases for ~6% of artists, so once we've seen a zero-yield
        // sync we skip MB for that celebrity for this many days before re-checking.
        private static readonly TimeSpan MusicBrainzRecheckInterval = TimeSpan.FromDays(30);

        // TM+TMDb fan-out batch size. High enough to matter, low enough to avoid blowing
        // HTTP/2 connection pools at the OS level.
        private const int ParallelBatchSize = 20;

        // DB insert parallelism: each worker owns a slice of celebrities and its own DbContext
        // from IDbContextFactory (DbContext is not thread-safe). LocalDB tolerates ~8 concurrent
        // writers comfortably; beyond that lock/contention costs more than the parallelism saves.
        private const int InsertParallelism = 8;

        public EventSyncService(
            IUnitOfWork uow,
            IDbContextFactory<ApplicationDbContext> contextFactory,
            IEnumerable<IEventFetchService> fetchers,
            ILogger<EventSyncService> logger)
        {
            _uow = uow;
            _contextFactory = contextFactory;
            _logger = logger;
            _allFetchers = fetchers.ToList();
        }

        public async Task<EventSyncSummary> RunAsync(CancellationToken ct = default)
        {
            var summary = new EventSyncSummary();
            var celebrities = await _uow.Celebrities.GetAllAsync();
            var now = DateTime.UtcNow;

            // Preload every existing (Source, SourceExternalId) in one query. Without this the
            // per-event loop issues one EXISTS roundtrip per candidate (thousands per run),
            // which is the dominant cost when the DB is mostly up to date.
            var existingKeys = await _uow.Events.GetAllSourceKeysAsync();

            // Phase 1 (parallel): sources with no rate limit (TM, TMDb) fanned out across all
            // applicable celebrities. MusicBrainz deliberately excluded — its in-process
            // throttle would serialize everything anyway, so it goes in the serial phase.
            var fastFetchers = _allFetchers
                .Where(f => !string.Equals(f.SourceName, "MusicBrainz", StringComparison.OrdinalIgnoreCase))
                .ToList();
            await FetchFastSourcesParallelAsync(celebrities, fastFetchers, summary, ct);

            // Phase 2 (serial): MusicBrainz only for celebrities due a recheck. A celebrity
            // whose last MB fetch returned 0 and who was synced recently is skipped.
            var mbFetcher = _allFetchers.FirstOrDefault(f =>
                string.Equals(f.SourceName, "MusicBrainz", StringComparison.OrdinalIgnoreCase));

            // celebrityId -> updated MB yield, applied during the parallel insert pass.
            var mbYields = new System.Collections.Concurrent.ConcurrentDictionary<int, int?>();

            foreach (var celebrity in celebrities)
            {
                ct.ThrowIfCancellationRequested();

                if (mbFetcher is not null &&
                    CategoriesBySource["MusicBrainz"].Contains(celebrity.Category))
                {
                    var dueForRecheck =
                        !celebrity.LastEventSyncAt.HasValue ||
                        celebrity.LastMusicBrainzYield is null or > 0 ||
                        now - celebrity.LastEventSyncAt.Value >= MusicBrainzRecheckInterval;

                    if (dueForRecheck)
                    {
                        var mbResults = await SafeFetchAsync(mbFetcher, celebrity.Name, summary, ct);
                        mbYields[celebrity.Id] = mbResults.Count;
                        lock (summary.FastResultsByCelebrity)
                        {
                            if (summary.FastResultsByCelebrity.TryGetValue(celebrity.Id, out var existing))
                            {
                                existing.AddRange(mbResults);
                            }
                            else
                            {
                                summary.FastResultsByCelebrity[celebrity.Id] = mbResults.ToList();
                            }
                        }
                    }
                }

                summary.CelebritiesProcessed++;
            }

            // Phase 3 (parallel): build Event entities in memory, dedupe against the preloaded
            // set + this-run adds, then partition celebrities across InsertParallelism workers
            // that each get their own DbContext and save their slice in one roundtrip.
            await InsertEventsParallelAsync(celebrities, existingKeys, mbYields, now, summary, ct);

            _logger.LogInformation(
                "Event sync completed: {CelebritiesProcessed} celebrities, {EventsInserted} events inserted, failures by source: {@Failures}",
                summary.CelebritiesProcessed,
                summary.EventsInserted,
                summary.FailuresBySource);

            foreach (var kvp in summary.CountsBySource)
            {
                _logger.LogInformation(
                    "  {Source}: inserted={Inserted}, already-in-db={ExistingInDb}, dup-in-run={DuplicateInRun}",
                    kvp.Key, kvp.Value.Inserted, kvp.Value.ExistingInDb, kvp.Value.DuplicateInRun);
            }

            return summary;
        }

        // Single-celebrity variant driven by the admin "Sync this celebrity" button on the
        // details page. Skips the 30-day MB recheck (admin explicitly asked, so always fetch)
        // and skips the parallel fan-out orchestration — only one celebrity, so the Task.WhenAll
        // ceremony is overhead.
        public async Task<EventSyncSummary?> RunForCelebrityAsync(int celebrityId, CancellationToken ct = default)
        {
            var celebrity = await _uow.Celebrities.GetByIdAsync(celebrityId);
            if (celebrity is null)
            {
                return null;
            }

            var summary = new EventSyncSummary();
            var now = DateTime.UtcNow;
            var existingKeys = await _uow.Events.GetAllSourceKeysAsync();

            foreach (var fetcher in _allFetchers)
            {
                if (CategoriesBySource.TryGetValue(fetcher.SourceName, out var applicable) &&
                    !applicable.Contains(celebrity.Category))
                {
                    continue;
                }

                var dtos = await SafeFetchAsync(fetcher, celebrity.Name, summary, ct);
                lock (summary.FastResultsByCelebrity)
                {
                    summary.FastResultsByCelebrity[celebrity.Id] =
                        summary.FastResultsByCelebrity.TryGetValue(celebrity.Id, out var existing)
                            ? existing.Concat(dtos).ToList()
                            : dtos.ToList();
                }

                if (string.Equals(fetcher.SourceName, "MusicBrainz", StringComparison.OrdinalIgnoreCase))
                {
                    celebrity.LastMusicBrainzYield = dtos.Count;
                }
            }

            summary.CelebritiesProcessed = 1;
            celebrity.LastEventSyncAt = now;

            // Single celebrity: insert inline (no parallel workers needed). Dedup against
            // existingKeys + QueuedThisRun, save once, recover on unique-index race.
            foreach (var dto in summary.FastResultsByCelebrity[celebrity.Id])
            {
                if (string.IsNullOrEmpty(dto.Source) || string.IsNullOrEmpty(dto.SourceExternalId))
                {
                    continue;
                }

                var counts = summary.ForSource(dto.Source);
                var key = (dto.Source, dto.SourceExternalId);

                if (!summary.QueuedThisRun.Add(key))
                {
                    counts.DuplicateInRun++;
                    continue;
                }

                if (existingKeys.Contains(key))
                {
                    counts.ExistingInDb++;
                    continue;
                }

                await _uow.Events.AddAsync(new Event
                {
                    CelebrityId = celebrity.Id,
                    Title = dto.Title,
                    EventType = dto.EventType,
                    EventDate = dto.EventDate,
                    Source = dto.Source,
                    SourceExternalId = dto.SourceExternalId,
                    Description = dto.Description,
                    Venue = dto.Venue,
                    City = dto.City,
                    Country = dto.Country,
                    Latitude = dto.Latitude,
                    Longitude = dto.Longitude
                });
                counts.Inserted++;
                summary.EventsInserted++;
            }

            try
            {
                await _uow.SaveChangesAsync();
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            {
                _uow.DetachPendingEventInserts();
                _logger.LogWarning(ex, "Save failed for celebrity {CelebrityId}; continuing", celebrity.Id);
            }

            return summary;
        }

        // Phase 1: hits TM+TMDb for every applicable celebrity concurrently, in batches of
        // ParallelBatchSize, and stashes the DTOs on the summary keyed by celebrity id so the
        // insert pass can pick them up. No DB writes here.
        private async Task FetchFastSourcesParallelAsync(
            IReadOnlyList<Celebrity> celebrities,
            IReadOnlyList<IEventFetchService> fastFetchers,
            EventSyncSummary summary,
            CancellationToken ct)
        {
            if (fastFetchers.Count == 0)
            {
                return;
            }

            foreach (var batch in celebrities.Chunk(ParallelBatchSize))
            {
                ct.ThrowIfCancellationRequested();

                var batchTasks = batch.Select(async celebrity =>
                {
                    var fetcherTasks = fastFetchers
                        .Where(f =>
                            !CategoriesBySource.TryGetValue(f.SourceName, out var applicable) ||
                            applicable.Contains(celebrity.Category))
                        .Select(f => SafeFetchAsync(f, celebrity.Name, summary, ct))
                        .ToList();

                    var perSource = await Task.WhenAll(fetcherTasks);
                    var dtos = perSource.SelectMany(list => list).ToList();
                    lock (summary.FastResultsByCelebrity)
                    {
                        summary.FastResultsByCelebrity[celebrity.Id] = dtos;
                    }
                });

                await Task.WhenAll(batchTasks);
            }
        }

        // Phase 3: builds new Event entities celebrity-by-celebrity (deduped), then fans the
        // celebrities across InsertParallelism workers. Each worker flushes its slice via one
        // SaveChanges on its own context. DbContext is not thread-safe; parallel writers must
        // each hold a private instance.
        private async Task InsertEventsParallelAsync(
            IReadOnlyList<Celebrity> celebrities,
            HashSet<(string Source, string SourceExternalId)> existingKeys,
            System.Collections.Concurrent.ConcurrentDictionary<int, int?> mbYields,
            DateTime now,
            EventSyncSummary summary,
            CancellationToken ct)
        {
            // Per-celebrity new-event buckets. Built serially so the dedup set stays a plain
            // HashSet — contention on a concurrent set would erase the win.
            var newEventsByCeleb = new Dictionary<int, List<Event>>();

            foreach (var celebrity in celebrities)
            {
                if (!summary.FastResultsByCelebrity.TryGetValue(celebrity.Id, out var fetched) || fetched.Count == 0)
                {
                    continue;
                }

                List<Event>? bucket = null;
                foreach (var dto in fetched)
                {
                    if (string.IsNullOrEmpty(dto.Source) || string.IsNullOrEmpty(dto.SourceExternalId))
                    {
                        continue;
                    }

                    var counts = summary.ForSource(dto.Source);
                    var key = (dto.Source, dto.SourceExternalId);

                    if (!summary.QueuedThisRun.Add(key))
                    {
                        counts.DuplicateInRun++;
                        continue;
                    }

                    if (existingKeys.Contains(key))
                    {
                        counts.ExistingInDb++;
                        continue;
                    }

                    bucket ??= new List<Event>();
                    bucket.Add(new Event
                    {
                        CelebrityId = celebrity.Id,
                        Title = dto.Title,
                        EventType = dto.EventType,
                        EventDate = dto.EventDate,
                        Source = dto.Source,
                        SourceExternalId = dto.SourceExternalId,
                        Description = dto.Description,
                        Venue = dto.Venue,
                        City = dto.City,
                        Country = dto.Country,
                        Latitude = dto.Latitude,
                        Longitude = dto.Longitude
                    });
                    counts.Inserted++;
                    summary.EventsInserted++;
                }

                if (bucket is not null)
                {
                    newEventsByCeleb[celebrity.Id] = bucket;
                }
            }

            // Round-robin partition of celebrities across workers. Round-robin (not Chunk)
            // evens out per-celebrity insert-count skew so no single worker gets a run of
            // heavy celebrities.
            var workerSlices = Enumerable.Range(0, InsertParallelism)
                .Select(_ => new List<Celebrity>())
                .ToArray();
            for (var i = 0; i < celebrities.Count; i++)
            {
                workerSlices[i % InsertParallelism].Add(celebrities[i]);
            }

            var workerTasks = workerSlices
                .Where(slice => slice.Count > 0)
                .Select(slice => FlushSliceAsync(slice, newEventsByCeleb, mbYields, now, ct))
                .ToList();

            await Task.WhenAll(workerTasks);
        }

        private async Task FlushSliceAsync(
            List<Celebrity> slice,
            Dictionary<int, List<Event>> newEventsByCeleb,
            System.Collections.Concurrent.ConcurrentDictionary<int, int?> mbYields,
            DateTime now,
            CancellationToken ct)
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync(ct);

            foreach (var celebrity in slice)
            {
                ct.ThrowIfCancellationRequested();

                // Attach the tracked celebrity so the bookkeeping column updates flow through
                // EF's change tracker without an extra SELECT. The instance came from a
                // different context, so Attach marks it unchanged and we mutate the two fields.
                ctx.Celebrities.Attach(celebrity);
                celebrity.LastEventSyncAt = now;
                if (mbYields.TryGetValue(celebrity.Id, out var yield))
                {
                    celebrity.LastMusicBrainzYield = yield;
                }

                if (newEventsByCeleb.TryGetValue(celebrity.Id, out var events))
                {
                    ctx.Events.AddRange(events);
                }
            }

            try
            {
                await ctx.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // Unique-index still wins if some other path pushed the same (Source,
                // SourceExternalId) between the initial key preload and this flush (manual
                // admin sync racing the 24h timer, etc). Log and continue — the next sync
                // will pick up anything still missing.
                _logger.LogWarning(ex, "Save failed for slice ({Count} celebrities); continuing", slice.Count);
            }
        }

        private async Task<IReadOnlyList<FetchedEventDto>> SafeFetchAsync(
            IEventFetchService fetcher,
            string celebrityName,
            EventSyncSummary summary,
            CancellationToken ct)
        {
            try
            {
                return await fetcher.FetchForCelebrityAsync(celebrityName, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Source} fetch failed for {Name}", fetcher.SourceName, celebrityName);
                lock (summary.FailuresBySource)
                {
                    summary.FailuresBySource[fetcher.SourceName] = summary.FailuresBySource.GetValueOrDefault(fetcher.SourceName) + 1;
                }
                return Array.Empty<FetchedEventDto>();
            }
        }
    }
}
