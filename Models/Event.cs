using Microsoft.EntityFrameworkCore;
using StanTrack.Models.Enums;

namespace StanTrack.Models
{
    [Index(nameof(EventDate))]
    [Index(nameof(CelebrityId), nameof(EventDate))]
    public class Event
    {
        public int Id { get; set; }
        public int CelebrityId { get; set; }
        public Celebrity? Celebrity { get; set; }
        public string Title { get; set; } = string.Empty;
        public EventType EventType { get; set; }
        public DateTime EventDate { get; set; }
        public string Source { get; set; } = "Manual"; // "Ticketmaster" | "TMDb" | "MusicBrainz" | "Manual"
        public string? SourceExternalId { get; set; }   // null for manual entries
        public string? Description { get; set; }

        // Location — populated by Ticketmaster for concerts; null for other sources/types.
        // Coords stored for future map/distance features; not rendered yet.
        public string? Venue { get; set; }              // e.g. "Madison Square Garden"
        public string? City { get; set; }               // e.g. "New York"
        public string? Country { get; set; }            // e.g. "United States"
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    }
}
