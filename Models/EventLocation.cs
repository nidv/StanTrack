namespace StanTrack.Models
{
    // Formats an Event's location into a single compact string for list rendering.
    // Picks whichever parts are populated (Ticketmaster rows may be missing any of them).
    public static class EventLocation
    {
        public static string? Format(Event ev)
        {
            var venueCityCountry = new List<string>(capacity: 3);
            if (!string.IsNullOrWhiteSpace(ev.Venue)) venueCityCountry.Add(ev.Venue);
            var cityCountry = FormatCityCountry(ev);
            if (!string.IsNullOrEmpty(cityCountry)) venueCityCountry.Add(cityCountry);
            return venueCityCountry.Count == 0 ? null : string.Join(" · ", venueCityCountry);
        }

        public static string? FormatCityCountry(Event ev)
        {
            if (!string.IsNullOrWhiteSpace(ev.City) && !string.IsNullOrWhiteSpace(ev.Country))
            {
                return $"{ev.City}, {ev.Country}";
            }
            if (!string.IsNullOrWhiteSpace(ev.City)) return ev.City;
            if (!string.IsNullOrWhiteSpace(ev.Country)) return ev.Country;
            return null;
        }
    }
}
