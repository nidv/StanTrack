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

        public EventSyncService(
            IUnitOfWork uow,
            IEnumerable<IEventFetchService> fetchers,
            ILogger<EventSyncService> logger)
        {
            _uow = uow;
            _logger = logger;
            _allFetchers = fetchers.ToList();
            // MusicBrainz has its own in-process throttle (static gate, 1 req/s) that correctly
            // serializes concurrent calls — running it via Task.WhenAll along with the other
            // fetchers stays safe.
        }

        public async Task<EventSyncSummary> RunAsync(CancellationToken ct = default)
        {
            var summary = new EventSyncSummary();
            var celebrities = await _uow.Celebrities.GetAllAsync();
            // The pre-check ExistsBySourceAsync only sees committed rows; it does NOT see
            // events already added to the change tracker this run but not yet flushed (we only
            // SaveChanges once per celebrity). Without this set, the same (Source, SourceExternalId)
            // can be queued twice in one batch and the unique index rejects the whole flush.
            var queuedThisRun = new HashSet<(string Source, string ExternalId)>();

            foreach (var celebrity in celebrities)
            {
                ct.ThrowIfCancellationRequested();
                summary.CelebritiesProcessed++;

                var fetched = await FetchAllForCelebrityAsync(celebrity, summary, ct);

                foreach (var dto in fetched)
                {
                    if (string.IsNullOrEmpty(dto.Source) || string.IsNullOrEmpty(dto.SourceExternalId))
                    {
                        continue;
                    }

                    if (!queuedThisRun.Add((dto.Source, dto.SourceExternalId)))
                    {
                        continue;
                    }

                    if (await _uow.Events.ExistsBySourceAsync(dto.Source, dto.SourceExternalId))
                    {
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
                        Description = dto.Description
                    });
                    summary.EventsInserted++;
                }

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

            return summary;
        }

        private async Task<IReadOnlyList<FetchedEventDto>> FetchAllForCelebrityAsync(
            Celebrity celebrity,
            EventSyncSummary summary,
            CancellationToken ct)
        {
            var tasks = new List<Task<IReadOnlyList<FetchedEventDto>>>();

            foreach (var fetcher in _allFetchers)
            {
                // Category filter: skip fetchers whose domain doesn't match this celebrity's category.
                // A lookup miss in the dict means the source applies to every category.
                if (CategoriesBySource.TryGetValue(fetcher.SourceName, out var applicable)
                    && !applicable.Contains(celebrity.Category))
                {
                    continue;
                }
                tasks.Add(SafeFetchAsync(fetcher, celebrity.Name, summary, ct));
            }

            var perSource = await Task.WhenAll(tasks);
            return perSource.SelectMany(list => list).ToList();
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
