using System.Globalization;
using System.Text.Json;
using StanTrack.Dtos;
using StanTrack.Interfaces;
using StanTrack.Models.Enums;

namespace StanTrack.ExternalApis
{
    public class MusicBrainzClient : IEventFetchService
    {
        // MusicBrainz rate limit: 1 request per second averaged, applied per source IP.
        // Both calls in this client share one HttpClient, so pacing here paces every
        // MusicBrainz call the process makes. Semaphore+ticks serializes concurrent
        // FetchForCelebrityAsync invocations so they queue instead of bursting.
        private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static long _lastRequestTicks;

        private readonly HttpClient _http;
        private readonly ILogger<MusicBrainzClient> _logger;

        public MusicBrainzClient(HttpClient http, ILogger<MusicBrainzClient> logger)
        {
            _http = http;
            _logger = logger;
        }

        public string SourceName => "MusicBrainz";

        public async Task<IReadOnlyList<FetchedEventDto>> FetchForCelebrityAsync(string celebrityName, CancellationToken ct)
        {
            try
            {
                var searchUrl = $"artist?query={Uri.EscapeDataString(celebrityName)}&fmt=json&limit=1";
                using var searchResponse = await GetThrottledAsync(searchUrl, ct);
                if (!searchResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("MusicBrainz artist search returned {Status} for {Name}", searchResponse.StatusCode, celebrityName);
                    return Array.Empty<FetchedEventDto>();
                }

                string artistId;
                await using (var searchStream = await searchResponse.Content.ReadAsStreamAsync(ct))
                using (var searchDoc = await JsonDocument.ParseAsync(searchStream, cancellationToken: ct))
                {
                    var artists = searchDoc.RootElement.GetProperty("artists");
                    if (artists.ValueKind != JsonValueKind.Array || artists.GetArrayLength() == 0)
                    {
                        return Array.Empty<FetchedEventDto>();
                    }
                    artistId = artists[0].GetProperty("id").GetString() ?? string.Empty;
                    if (string.IsNullOrEmpty(artistId))
                    {
                        return Array.Empty<FetchedEventDto>();
                    }
                }

                // Search endpoint with explicit future-date filter — the /release-group?artist=...
                // browse endpoint paginates oldest-first with no sort control, so future releases are unreachable.
                var todayIso = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var rgUrl = $"release-group/?query=firstreleasedate:[{todayIso} TO *] AND arid:{artistId}&fmt=json&limit=100";
                using var rgResponse = await GetThrottledAsync(rgUrl, ct);
                if (!rgResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("MusicBrainz release-group search returned {Status} for artist {ArtistId}", rgResponse.StatusCode, artistId);
                    return Array.Empty<FetchedEventDto>();
                }

                var results = new List<FetchedEventDto>();
                var today = DateTime.UtcNow.Date;
                await using var rgStream = await rgResponse.Content.ReadAsStreamAsync(ct);
                using var rgDoc = await JsonDocument.ParseAsync(rgStream, cancellationToken: ct);
                var groups = rgDoc.RootElement.GetProperty("release-groups");

                foreach (var rg in groups.EnumerateArray())
                {
                    if (!rg.TryGetProperty("first-release-date", out var frd)) continue;
                    var dateStr = frd.GetString();
                    if (string.IsNullOrWhiteSpace(dateStr)) continue;
                    if (!TryParseMusicBrainzDate(dateStr, out var releaseDate)) continue;
                    if (releaseDate.Date < today) continue;

                    var title = rg.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrEmpty(title)) continue;

                    var rgId = rg.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrEmpty(rgId)) continue;

                    results.Add(new FetchedEventDto
                    {
                        Title = title,
                        EventType = EventType.Release,
                        EventDate = releaseDate.ToUniversalTime(),
                        Source = SourceName,
                        SourceExternalId = rgId,
                        Description = null
                    });
                }
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MusicBrainz fetch failed for {Name}", celebrityName);
                return Array.Empty<FetchedEventDto>();
            }
        }

        // MusicBrainz first-release-date has 3 precisions: "yyyy-MM-dd", "yyyy-MM", "yyyy".
        // For coarse precision, the actual date could be any day in the period; using the
        // first day would discard legitimately-future entries when filtering by date.
        // Returning the last possible day so year-precision "2026" stays comparable to any day in 2026.
        private static bool TryParseMusicBrainzDate(string value, out DateTime result)
        {
            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result))
            {
                return true;
            }
            if (DateTime.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var month))
            {
                result = new DateTime(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month), 0, 0, 0, DateTimeKind.Utc);
                return true;
            }
            if (DateTime.TryParseExact(value, "yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var year))
            {
                result = new DateTime(year.Year, 12, 31, 0, 0, 0, DateTimeKind.Utc);
                return true;
            }
            result = default;
            return false;
        }

        private async Task<HttpResponseMessage> GetThrottledAsync(string url, CancellationToken ct)
        {
            await Gate.WaitAsync(ct);
            try
            {
                var lastTicks = Interlocked.Read(ref _lastRequestTicks);
                if (lastTicks != 0)
                {
                    var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - lastTicks);
                    if (elapsed < MinInterval)
                    {
                        await Task.Delay(MinInterval - elapsed, ct);
                    }
                }
                var response = await _http.GetAsync(url, ct);
                Interlocked.Exchange(ref _lastRequestTicks, DateTime.UtcNow.Ticks);
                return response;
            }
            finally
            {
                Gate.Release();
            }
        }
    }
}
