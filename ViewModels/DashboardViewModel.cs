using StanTrack.Models;

namespace StanTrack.ViewModels
{
    public class DashboardViewModel
    {
        public IReadOnlyList<Celebrity> FollowedCelebrities { get; set; } = Array.Empty<Celebrity>();
        public IReadOnlyList<Event> UpcomingEvents { get; set; } = Array.Empty<Event>();
    }
}
