using StanTrack.Models;

namespace StanTrack.Interfaces
{
    public interface ICelebrityRepository
    {
        Task<Celebrity?> GetByIdAsync(int id);
        Task<IReadOnlyList<Celebrity>> GetByIdsAsync(IEnumerable<int> ids);
        Task<IReadOnlyList<Celebrity>> SearchAsync(string? query, string? category);
        Task<IReadOnlyList<Celebrity>> SearchPaginatedAsync(string? query, string? category, int page, int pageSize);
        Task<int> CountAsync(string? query, string? category);
        Task<IReadOnlyList<Celebrity>> GetAllAsync();
        Task<IReadOnlyList<string>> GetDistinctCategoriesAsync();
        Task<IReadOnlyList<Celebrity>> GetRandomAsync(int count);
        Task AddAsync(Celebrity celebrity);
        void Update(Celebrity celebrity);
        void Delete(Celebrity celebrity);
    }
}
