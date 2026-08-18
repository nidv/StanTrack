namespace StanTrack.Interfaces
{
    public interface IFollowRepository
    {
        Task<bool> IsFollowingAsync(string userId, int celebrityId);
        Task<IReadOnlyList<int>> GetFollowedCelebrityIdsAsync(string userId);
        Task FollowAsync(string userId, int celebrityId);
        Task UnfollowAsync(string userId, int celebrityId);
    }
}
