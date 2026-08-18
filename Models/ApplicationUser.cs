using Microsoft.AspNetCore.Identity;

namespace StanTrack.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string? DisplayName { get; set; }
        public ICollection<Celebrity> CreatedCelebrities { get; set; } = new List<Celebrity>();
        public ICollection<Follow> Follows { get; set; } = new List<Follow>();
        public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    }
}
