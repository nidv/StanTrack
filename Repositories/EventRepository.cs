using Microsoft.EntityFrameworkCore;
using StanTrack.Data;
using StanTrack.Interfaces;
using StanTrack.Models;

namespace StanTrack.Repositories
{
    public class EventRepository(ApplicationDbContext context) : IEventRepository
    {
        public async Task<IReadOnlyList<Event>> GetUpcomingForCelebritiesAsync(IEnumerable<int> celebrityIds, DateTime fromUtc)
        {
            var ids = celebrityIds.ToList();
            if (ids.Count == 0)
            {
                return [];
            }

            return await context.Events
                .Where(e => ids.Contains(e.CelebrityId) && e.EventDate >= fromUtc)
                .Include(e => e.Celebrity)
                .OrderBy(e => e.EventDate)
                .ToListAsync();
        }

        public Task<bool> ExistsBySourceAsync(string source, string sourceExternalId)
            => context.Events.AnyAsync(e => e.Source == source && e.SourceExternalId == sourceExternalId);

        public async Task AddAsync(Event ev)
            => await context.Events.AddAsync(ev);

        public async Task<IReadOnlyList<Event>> GetAllForAdminReviewAsync()
            => await context.Events
                .Include(e => e.Celebrity)
                .OrderByDescending(e => e.EventDate)
                .ToListAsync();
    }
}
