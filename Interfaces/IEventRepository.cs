using StanTrack.Models;

namespace StanTrack.Interfaces
{
    public interface IEventRepository
    {
        Task<IReadOnlyList<Event>> GetUpcomingForCelebritiesAsync(IEnumerable<int> celebrityIds, DateTime fromUtc);
        Task<IReadOnlyList<Event>> GetUpcomingAsync(DateTime fromUtc, int take);
        Task<IReadOnlyList<Event>> GetAllUpcomingAsync(DateTime fromUtc);
        Task<int> GetUpcomingCountAsync(DateTime fromUtc);
        Task<IReadOnlyList<Event>> GetUpcomingPaginatedAsync(DateTime fromUtc, int page, int pageSize);
        Task<bool> ExistsBySourceAsync(string source, string sourceExternalId);
        Task AddAsync(Event ev);
        Task<IReadOnlyList<Event>> GetAllForAdminReviewAsync();
    }
}
