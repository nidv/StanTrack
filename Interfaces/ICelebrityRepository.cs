using StanTrack.Models;

namespace StanTrack.Interfaces
{
    public interface ICelebrityRepository
    {
        Task<Celebrity?> GetByIdAsync(int id);
        Task<IReadOnlyList<Celebrity>> GetByIdsAsync(IEnumerable<int> ids);
        Task<IReadOnlyList<Celebrity>> SearchAsync(string? query, string? category);
        Task<IReadOnlyList<string>> GetDistinctCategoriesAsync();
        Task AddAsync(Celebrity celebrity);
        void Update(Celebrity celebrity);
        void Delete(Celebrity celebrity);
    }
}
