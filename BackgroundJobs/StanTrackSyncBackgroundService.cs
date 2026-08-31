namespace StanTrack.BackgroundJobs
{
    public class StanTrackSyncBackgroundService : BackgroundService
    {
        // Sunday 03:00 local server time. Adjust DayOfWeek/hour here if the schedule changes.
        private static readonly DayOfWeek SyncDay = DayOfWeek.Sunday;
        private static readonly TimeSpan SyncTimeOfDay = TimeSpan.FromHours(3);

        private readonly IServiceProvider _services;
        private readonly ILogger<StanTrackSyncBackgroundService> _logger;

        public StanTrackSyncBackgroundService(
            IServiceProvider services,
            ILogger<StanTrackSyncBackgroundService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = GetDelayUntilNextRun(DateTimeOffset.Now);
                _logger.LogInformation("Next event sync scheduled in {Delay} at {NextRun}", delay, DateTimeOffset.Now + delay);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    using var scope = _services.CreateScope();
                    var syncService = scope.ServiceProvider.GetRequiredService<EventSyncService>();
                    await syncService.RunAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Event sync tick failed");
                }
            }
        }

        private static TimeSpan GetDelayUntilNextRun(DateTimeOffset now)
        {
            var daysAhead = ((int)SyncDay - (int)now.DayOfWeek + 7) % 7;
            var nextRun = new DateTimeOffset(now.Date.AddDays(daysAhead), now.Offset) + SyncTimeOfDay;
            if (nextRun <= now)
            {
                nextRun = nextRun.AddDays(7);
            }
            return nextRun - now;
        }
    }
}
