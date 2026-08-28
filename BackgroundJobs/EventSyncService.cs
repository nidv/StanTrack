using StanTrack.Dtos;
using StanTrack.Interfaces;
using StanTrack.Models;

namespace StanTrack.BackgroundJobs
{
    public class EventSyncService
    {
        private readonly IUnitOfWork _uow;
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

        public EventSyncService(
            IUnitOfWork uow,
            IEnumerable<IEventFetchService> fetchers,
            ILogger<EventSyncService> logger)
        {
            _uow = uow;
            _logger = logger;
            _allFetchers = fetchers.ToList();
        }

        public async Task<EventSyncSummary> RunAsync(CancellationToken ct = default)
        {
            var summary = new EventSyncSummary();
            var celebrities = await _uow.Celebrities.GetAllAsync();
            var now = DateTime.UtcNow;

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

            foreach (var celebrity in celebrities)
            {
                ct.ThrowIfCancellationRequested();
                var fetched = new List<FetchedEventDto>();

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
                        fetched.AddRange(mbResults);
                        celebrity.LastMusicBrainzYield = mbResults.Count;
                    }
                }

                // Phase 1 results for this celebrity were stashed during FetchFastSourcesParallelAsync.
                if (summary.FastResultsByCelebrity.TryGetValue(celebrity.Id, out var fastDtos))
                {
                    fetched.AddRange(fastDtos);
                }

                celebrity.LastEventSyncAt = now;

                // The pre-check ExistsBySourceAsync only sees committed rows; it does NOT see
                // events already added to the change tracker this run but not yet flushed (we only
                // SaveChanges once per celebrity). Without this set, the same (Source, SourceExternalId)
                // can be queued twice in one batch and the unique index rejects the whole flush.
                foreach (var dto in fetched)
                {
                    if (string.IsNullOrEmpty(dto.Source) || string.IsNullOrEmpty(dto.SourceExternalId))
                    {
                        continue;
                    }

                    var counts = summary.ForSource(dto.Source);

                    if (!summary.QueuedThisRun.Add((dto.Source, dto.SourceExternalId)))
                    {
                        counts.DuplicateInRun++;
                        continue;
                    }

                    if (await _uow.Events.ExistsBySourceAsync(dto.Source, dto.SourceExternalId))
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

                summary.CelebritiesProcessed++;

                try
                {
                    await _uow.SaveChangesAsync();
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
                {
                    // Unique-index still wins if some other path pushed the same (Source, SourceExternalId)
                    // between our check and our commit (two admins pressing Sync at once, the 24h timer
                    // and the manual button racing, etc). Detach the failed adds so the next
                    // celebrity's flush doesn't retry them, then keep going — the next sync will
                    // re-fetch anything that was actually new and not yet in the DB.
                    _logger.LogWarning(ex, "Save failed for {Name}; detaching pending inserts and continuing", celebrity.Name);
                    _uow.DetachPendingEventInserts();
                }
            }

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

        // Phase 1: hits TM+TMDb for every applicable celebrity concurrently, in batches of
        // ParallelBatchSize, and stashes the DTOs on the summary keyed by celebrity id so the
        // serial per-celebrity loop in RunAsync can pick them up. No DB writes here — the
        // per-celebrity insert path stays serialized through the normal loop.
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
