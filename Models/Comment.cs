namespace StanTrack.Models
{
    public class Comment
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser? User { get; set; }
        public int EventId { get; set; }
        public Event? Event { get; set; }
        public string Text { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
