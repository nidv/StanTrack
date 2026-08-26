namespace StanTrack.Interfaces
{
    public interface IUnitOfWork
    {
        ICelebrityRepository Celebrities { get; }
        IEventRepository Events { get; }
        IFollowRepository Follows { get; }
        Task<int> SaveChangesAsync();
        void DetachPendingEventInserts();
    }
}
