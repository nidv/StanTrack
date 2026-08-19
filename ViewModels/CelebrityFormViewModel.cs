using System.ComponentModel.DataAnnotations;

namespace StanTrack.ViewModels
{
    public class CelebrityFormViewModel
    {
        public int? Id { get; set; }

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Category { get; set; } = string.Empty;

        [StringLength(4000)]
        public string? Bio { get; set; }

        [Url]
        [StringLength(1000)]
        [Display(Name = "Photo URL")]
        public string? PhotoUrl { get; set; }
    }
}
