namespace StanTrack.BackgroundJobs
{
    public class StanTrackSyncBackgroundService : BackgroundService
    {
        private static readonly TimeSpan SyncInterval = TimeSpan.FromHours(24);

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
            using var timer = new PeriodicTimer(SyncInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
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
    }
}
