using Microsoft.EntityFrameworkCore;
using StanTrack.Data;
using StanTrack.Interfaces;

namespace StanTrack.Repositories
{
    public class UnitOfWork(ApplicationDbContext context) : IUnitOfWork
    {
        private ICelebrityRepository? _celebrities;
        private IEventRepository? _events;
        private IFollowRepository? _follows;

        public ICelebrityRepository Celebrities => _celebrities ??= new CelebrityRepository(context);
        public IEventRepository Events => _events ??= new EventRepository(context);
        public IFollowRepository Follows => _follows ??= new FollowRepository(context);

        public Task<int> SaveChangesAsync() => context.SaveChangesAsync();

        // If SaveChangesAsync throws, the failed `Added` entries stay in the change tracker and the
        // next iteration's SaveChanges will retry them and fail again the same way. Called by
        // EventSyncService on DbUpdateException to drop those entries so the sync can continue.
        public void DetachPendingEventInserts()
        {
            var added = context.ChangeTracker.Entries<Models.Event>()
                .Where(e => e.State == EntityState.Added)
                .ToList();
            foreach (var entry in added)
            {
                entry.State = EntityState.Detached;
            }
        }
    }
}
