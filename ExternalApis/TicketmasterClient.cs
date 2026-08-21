using System.Text.Json;
using StanTrack.Dtos;
using StanTrack.Interfaces;
using StanTrack.Models.Enums;

namespace StanTrack.ExternalApis
{
    public class TicketmasterClient : IEventFetchService
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TicketmasterClient> _logger;

        public TicketmasterClient(HttpClient http, IConfiguration configuration, ILogger<TicketmasterClient> logger)
        {
            _http = http;
            _configuration = configuration;
            _logger = logger;
        }

        public string SourceName => "Ticketmaster";

        public async Task<IReadOnlyList<FetchedEventDto>> FetchForCelebrityAsync(string celebrityName, CancellationToken ct)
        {
            try
            {
                var apiKey = _configuration["ExternalApis:Ticketmaster:ApiKey"];
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    _logger.LogWarning("Ticketmaster API key not configured; skipping fetch for {Name}", celebrityName);
                    return Array.Empty<FetchedEventDto>();
                }

                var url = $"events.json?keyword={Uri.EscapeDataString(celebrityName)}&apikey={Uri.EscapeDataString(apiKey)}";
                using var response = await _http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Ticketmaster returned {Status} for {Name}", response.StatusCode, celebrityName);
                    return Array.Empty<FetchedEventDto>();
                }

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = doc.RootElement;

                if (!root.TryGetProperty("_embedded", out var embedded) ||
                    !embedded.TryGetProperty("events", out var events) ||
                    events.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<FetchedEventDto>();
                }

                var results = new List<FetchedEventDto>();
                foreach (var ev in events.EnumerateArray())
                {
                    var title = ev.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                    var externalId = ev.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(externalId))
                    {
                        continue;
                    }

                    DateTime eventDate;
                    if (ev.TryGetProperty("dates", out var dates) &&
                        dates.TryGetProperty("start", out var start) &&
                        start.TryGetProperty("dateTime", out var dt) &&
                        DateTime.TryParse(dt.GetString(), out var parsed))
                    {
                        eventDate = parsed.ToUniversalTime();
                    }
                    else
                    {
                        continue;
                    }

                    var eventType = EventType.Other;
                    if (ev.TryGetProperty("classifications", out var classifications) && classifications.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in classifications.EnumerateArray())
                        {
                            if (c.TryGetProperty("segment", out var segment) &&
                                segment.TryGetProperty("name", out var segmentName) &&
                                string.Equals(segmentName.GetString(), "Music", StringComparison.OrdinalIgnoreCase))
                            {
                                eventType = EventType.Concert;
                                break;
                            }
                        }
                    }

                    string? description = null;
                    if (ev.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.String)
                    {
                        description = info.GetString();
                    }
                    else if (ev.TryGetProperty("pleaseNote", out var note) && note.ValueKind == JsonValueKind.String)
                    {
                        description = note.GetString();
                    }

                    results.Add(new FetchedEventDto
                    {
                        Title = title,
                        EventType = eventType,
                        EventDate = eventDate,
                        Source = SourceName,
                        SourceExternalId = externalId,
                        Description = description
                    });
                }
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ticketmaster fetch failed for {Name}", celebrityName);
                return Array.Empty<FetchedEventDto>();
            }
        }
    }
}
