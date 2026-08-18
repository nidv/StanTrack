using Microsoft.EntityFrameworkCore;
using StanTrack.Data;
using StanTrack.Interfaces;
using StanTrack.Models;

namespace StanTrack.Repositories
{
    public class FollowRepository(ApplicationDbContext context) : IFollowRepository
    {
        public Task<bool> IsFollowingAsync(string userId, int celebrityId)
            => context.Follows.AnyAsync(f => f.UserId == userId && f.CelebrityId == celebrityId);

        public async Task<IReadOnlyList<int>> GetFollowedCelebrityIdsAsync(string userId)
            => await context.Follows
                .Where(f => f.UserId == userId)
                .Select(f => f.CelebrityId)
                .ToListAsync();

        public async Task FollowAsync(string userId, int celebrityId)
        {
            var exists = await IsFollowingAsync(userId, celebrityId);
            if (!exists)
            {
                await context.Follows.AddAsync(new Follow { UserId = userId, CelebrityId = celebrityId });
            }
        }

        public async Task UnfollowAsync(string userId, int celebrityId)
        {
            var follow = await context.Follows
                .FirstOrDefaultAsync(f => f.UserId == userId && f.CelebrityId == celebrityId);
            if (follow is not null)
            {
                context.Follows.Remove(follow);
            }
        }
    }
}
