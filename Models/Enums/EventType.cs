using System.ComponentModel.DataAnnotations;

namespace StanTrack.Models.Enums
{
    public enum EventType
    {
        Concert,

        // Legacy value: kept here so existing rows with this int value still parse.
        // New rows from TMDb use MovieRelease; new rows from MusicBrainz use AlbumRelease.
        // The data migration script flips existing rows to the new values.
        Release,

        Birthday,
        Other,

        [Display(Name = "Movie Release")]
        MovieRelease,

        [Display(Name = "Album Release")]
        AlbumRelease,
    }
}
