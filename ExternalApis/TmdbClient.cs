using System.Text.Json;
using StanTrack.Dtos;
using StanTrack.Interfaces;
using StanTrack.Models.Enums;

namespace StanTrack.ExternalApis
{
    public class TmdbClient : IEventFetchService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TmdbClient> _logger;

        public TmdbClient(HttpClient http, IConfiguration configuration, ILogger<TmdbClient> logger)
        {
            _http = http;
            _configuration = configuration;
            _logger = logger;
        }

        public string SourceName => "TMDb";

        public async Task<IReadOnlyList<FetchedEventDto>> FetchForCelebrityAsync(string celebrityName, CancellationToken ct)
        {
            try
            {
                var apiKey = _configuration["ExternalApis:Tmdb:ApiKey"];
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    _logger.LogWarning("TMDb API key not configured; skipping fetch for {Name}", celebrityName);
                    return Array.Empty<FetchedEventDto>();
                }

                var searchUrl = $"search/person?api_key={Uri.EscapeDataString(apiKey)}&query={Uri.EscapeDataString(celebrityName)}";
                using var searchResponse = await _http.GetAsync(searchUrl, ct);
                if (!searchResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("TMDb search returned {Status} for {Name}", searchResponse.StatusCode, celebrityName);
                    return Array.Empty<FetchedEventDto>();
                }

                int personId;
                await using (var searchStream = await searchResponse.Content.ReadAsStreamAsync(ct))
                using (var searchDoc = await JsonDocument.ParseAsync(searchStream, cancellationToken: ct))
                {
                    var results = searchDoc.RootElement.GetProperty("results");
                    if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
                    {
                        return Array.Empty<FetchedEventDto>();
                    }
                    personId = results[0].GetProperty("id").GetInt32();
                }

                var creditsUrl = $"person/{personId}/movie_credits?api_key={Uri.EscapeDataString(apiKey)}";
                using var creditsResponse = await _http.GetAsync(creditsUrl, ct);
                if (!creditsResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("TMDb credits returned {Status} for person {PersonId}", creditsResponse.StatusCode, personId);
                    return Array.Empty<FetchedEventDto>();
                }

                var today = DateTime.UtcNow.Date;
                var results2 = new List<FetchedEventDto>();

                await using var creditsStream = await creditsResponse.Content.ReadAsStreamAsync(ct);
                using var creditsDoc = await JsonDocument.ParseAsync(creditsStream, cancellationToken: ct);
                var cast = creditsDoc.RootElement.GetProperty("cast");

                foreach (var movie in cast.EnumerateArray())
                {
                    if (!movie.TryGetProperty("release_date", out var rd)) continue;
                    var releaseStr = rd.GetString();
                    if (string.IsNullOrWhiteSpace(releaseStr)) continue;
                    if (!DateTime.TryParse(releaseStr, out var releaseDate)) continue;
                    if (releaseDate.Date < today) continue;

                    var title = movie.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrEmpty(title)) continue;

                    var movieId = movie.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
                    if (movieId == 0) continue;

                    string? overview = null;
                    if (movie.TryGetProperty("overview", out var ov) && ov.ValueKind == JsonValueKind.String)
                    {
                        overview = ov.GetString();
                    }

                    results2.Add(new FetchedEventDto
                    {
                        Title = title,
                        EventType = EventType.MovieRelease,
                        EventDate = releaseDate.ToUniversalTime(),
                        Source = SourceName,
                        SourceExternalId = $"movie-{movieId}",
                        Description = overview
                    });
                }

                return results2;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TMDb fetch failed for {Name}", celebrityName);
                return Array.Empty<FetchedEventDto>();
            }
        }
    }
}
