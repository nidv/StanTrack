namespace StanTrack.Models
{
    public class Celebrity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // "K-pop", "Actor", "Musician", etc.
        public string? Bio { get; set; }
        public string? PhotoUrl { get; set; }
        public DateTime? DateOfBirth { get; set; }   // birth date for people, inception date for groups
        public string CreatedByUserId { get; set; } = string.Empty;
        public ApplicationUser? CreatedBy { get; set; }
        public ICollection<Event> Events { get; set; } = new List<Event>();
        public ICollection<Follow> Follows { get; set; } = new List<Follow>();
    }
}
