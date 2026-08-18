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
    }
}
