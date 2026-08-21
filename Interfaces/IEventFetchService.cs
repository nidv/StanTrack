using StanTrack.Dtos;

namespace StanTrack.Interfaces
{
    public interface IEventFetchService
    {
        string SourceName { get; }
        Task<IReadOnlyList<FetchedEventDto>> FetchForCelebrityAsync(string celebrityName, CancellationToken ct);
    }
}
