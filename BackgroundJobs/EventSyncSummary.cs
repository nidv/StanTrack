using StanTrack.Dtos;

namespace StanTrack.BackgroundJobs
{
    public class EventSyncSummary
    {
        public int CelebritiesProcessed { get; set; }
        public int EventsInserted { get; set; }
        // Per-source running tally. ExistingInDb = skipped because ExistsBySourceAsync hit;
        // DuplicateInRun = skipped because the same (Source, ExternalId) was queued twice
        // during this single RunAsync; Inserted = wrote a new row.
        public Dictionary<string, SourceCounts> CountsBySource { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> FailuresBySource { get; } = new(StringComparer.OrdinalIgnoreCase);

        // Phase-1 staging: fast-source (Ticketmaster, TMDb) DTOs keyed by celebrity id.
        // The serial per-celebrity insert loop reads from here.
        public Dictionary<int, List<FetchedEventDto>> FastResultsByCelebrity { get; } = new();

        // Dedup set for the whole RunAsync. ExistsBySourceAsync only sees committed rows; this
        // catches in-tracker duplicates within a run.
        public HashSet<(string Source, string ExternalId)> QueuedThisRun { get; } = new();

        public SourceCounts ForSource(string source)
        {
            lock (CountsBySource)
            {
                if (!CountsBySource.TryGetValue(source, out var counts))
                {
                    counts = new SourceCounts();
                    CountsBySource[source] = counts;
                }
                return counts;
            }
        }

        public class SourceCounts
        {
            public int Inserted;
            public int ExistingInDb;
            public int DuplicateInRun;
        }
    }
}
