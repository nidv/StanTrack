using StanTrack.Models.Enums;

namespace StanTrack.Dtos
{
    public class FetchedEventDto
    {
        public string Title { get; set; } = string.Empty;
        public EventType EventType { get; set; }
        public DateTime EventDate { get; set; }
        public string Source { get; set; } = string.Empty;
        public string SourceExternalId { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}
