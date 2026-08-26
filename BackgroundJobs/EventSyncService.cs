using StanTrack.Interfaces;
using StanTrack.Models;

namespace StanTrack.BackgroundJobs
{
    public class EventSyncService
    {
        private readonly IUnitOfWork _uow;
        private readonly IEnumerable<IEventFetchService> _fetchers;
        private readonly ILogger<EventSyncService> _logger;

        public EventSyncService(
            IUnitOfWork uow,
            IEnumerable<IEventFetchService> fetchers,
            ILogger<EventSyncService> logger)
        {
            _uow = uow;
            _fetchers = fetchers;
            _logger = logger;
        }

        public async Task<EventSyncSummary> RunAsync(CancellationToken ct = default)
        {
            var summary = new EventSyncSummary();
            var celebrities = await _uow.Celebrities.GetAllAsync();

            foreach (var celebrity in celebrities)
            {
                ct.ThrowIfCancellationRequested();
                summary.CelebritiesProcessed++;

                foreach (var fetcher in _fetchers)
                {
                    IReadOnlyList<Dtos.FetchedEventDto> fetched;
                    try
                    {
                        fetched = await fetcher.FetchForCelebrityAsync(celebrity.Name, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "{Source} fetch failed for {Name}", fetcher.SourceName, celebrity.Name);
                        summary.FailuresBySource[fetcher.SourceName] = summary.FailuresBySource.GetValueOrDefault(fetcher.SourceName) + 1;
                        continue;
                    }

                    foreach (var dto in fetched)
                    {
                        if (string.IsNullOrEmpty(dto.Source) || string.IsNullOrEmpty(dto.SourceExternalId))
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
                }

                await _uow.SaveChangesAsync();
            }

            _logger.LogInformation(
                "Event sync completed: {CelebritiesProcessed} celebrities, {EventsInserted} events inserted, failures by source: {@Failures}",
                summary.CelebritiesProcessed,
                summary.EventsInserted,
                summary.FailuresBySource);

            return summary;
        }
    }
}
