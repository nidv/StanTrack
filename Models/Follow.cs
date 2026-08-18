namespace StanTrack.Models
{
    public class Follow
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser? User { get; set; }
        public int CelebrityId { get; set; }
        public Celebrity? Celebrity { get; set; }
        public DateTime FollowedAt { get; set; } = DateTime.UtcNow;
    }
}
