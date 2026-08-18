using StanTrack.Models;

namespace StanTrack.Interfaces
{
    public interface IEventRepository
    {
        Task<IReadOnlyList<Event>> GetUpcomingForCelebritiesAsync(IEnumerable<int> celebrityIds, DateTime fromUtc);
        Task<bool> ExistsBySourceAsync(string source, string sourceExternalId);
        Task AddAsync(Event ev);
        Task<IReadOnlyList<Event>> GetAllForAdminReviewAsync();
    }
}
