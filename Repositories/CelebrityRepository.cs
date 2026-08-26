using Microsoft.EntityFrameworkCore;
using StanTrack.Data;
using StanTrack.Interfaces;
using StanTrack.Models;

namespace StanTrack.Repositories
{
    public class CelebrityRepository(ApplicationDbContext context) : ICelebrityRepository
    {
        public Task<Celebrity?> GetByIdAsync(int id)
            => context.Celebrities.FirstOrDefaultAsync(c => c.Id == id);

        public async Task<IReadOnlyList<Celebrity>> GetByIdsAsync(IEnumerable<int> ids)
        {
            var idList = ids.ToList();
            if (idList.Count == 0)
            {
                return Array.Empty<Celebrity>();
            }
            return await context.Celebrities
                .Where(c => idList.Contains(c.Id))
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<Celebrity>> SearchAsync(string? query, string? category)
        {
            var q = context.Celebrities.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query))
            {
                q = q.Where(c => c.Name.Contains(query));
            }
            if (!string.IsNullOrWhiteSpace(category))
            {
                q = q.Where(c => c.Category == category);
            }

            return await q.OrderBy(c => c.Name).ToListAsync();
        }

        public async Task<IReadOnlyList<Celebrity>> GetAllAsync()
            => await context.Celebrities
                .OrderBy(c => c.Name)
                .ToListAsync();

        public async Task<IReadOnlyList<string>> GetDistinctCategoriesAsync()
            => await context.Celebrities
                .Select(c => c.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

        public async Task AddAsync(Celebrity celebrity)
            => await context.Celebrities.AddAsync(celebrity);

        public void Update(Celebrity celebrity)
            => context.Celebrities.Update(celebrity);

        public void Delete(Celebrity celebrity)
            => context.Celebrities.Remove(celebrity);
    }
}
