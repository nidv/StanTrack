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

        // Location — populated by Ticketmaster; null for other sources.
        public string? Venue { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }
}
