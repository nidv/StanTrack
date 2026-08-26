namespace StanTrack.BackgroundJobs
{
    public class EventSyncSummary
    {
        public int CelebritiesProcessed { get; set; }
        public int EventsInserted { get; set; }
        public Dictionary<string, int> FailuresBySource { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
