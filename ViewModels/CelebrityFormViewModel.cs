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

        // Date of birth for people; inception date for groups. Nullable so existing rows
        // and non-individual entries (K-pop groups prior to a manual inception date) stay valid.
        [DataType(DataType.Date)]
        [Display(Name = "Date of birth / Inception")]
        public DateTime? DateOfBirth { get; set; }
    }
}
