using System.ComponentModel.DataAnnotations;

namespace StanTrack.Models.Dtos
{
    public class CelebrityCreateRequest
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Category { get; set; } = string.Empty;

        public string? Bio { get; set; }
        public string? PhotoUrl { get; set; }
        public DateTime? DateOfBirth { get; set; }
    }

    public class CelebrityResponse
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? Bio { get; set; }
        public string? PhotoUrl { get; set; }
        public DateTime? DateOfBirth { get; set; }

        public static CelebrityResponse FromEntity(Celebrity c) => new()
        {
            Id = c.Id,
            Name = c.Name,
            Category = c.Category,
            Bio = c.Bio,
            PhotoUrl = c.PhotoUrl,
            DateOfBirth = c.DateOfBirth,
        };
    }
}
