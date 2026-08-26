using System.Text.Json;
using Microsoft.Data.SqlClient;

// Wikidata seeder for StanTrack Celebrities.
// Run from Windows against the LocalDB instance:
//   dotnet run --project Tools/CelebritySeeder -- `
//     --connection-string "Server=(localdb)\mssqllocaldb;Database=StanTrackDb;Trusted_Connection=True;MultipleActiveResultSets=true" `
//     --created-by-user-id "<Id from AspNetUsers for admin@stantrack.local>"
//
// Two-pass design: pass 1 pulls only the celebrity ID / name / dob (cheap SPARQL).
// Pass 2 batches the IDs (50 at a time) back into SPARQL via VALUES to fetch
// bio + photo per person. Splitting the cheap filter from the optional fields
// sidesteps the query-cost timeouts that hit when OPTIONAL blocks are fetched
// against the entire occupation-subtree.

var argsDict = ParseArgs(args);
if (!argsDict.TryGetValue("--connection-string", out var connectionString) ||
    !argsDict.TryGetValue("--created-by-user-id", out var createdByUserId))
{
    Console.Error.WriteLine("Usage: CelebritySeeder --connection-string <cs> --created-by-user-id <guid>");
    return 2;
}

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("StanTrack-CelebritySeeder/1.0 (https://github.com/lexutb/StanTrack)");
http.DefaultRequestHeaders.Accept.ParseAdd("application/sparql-results+json");
http.Timeout = TimeSpan.FromSeconds(120);

var existingNames = await LoadExistingNamesAsync(connectionString);
Console.WriteLine($"Existing celebrities in DB: {existingNames.Count}");

var categories = new[]
{
    new CategorySpec(
        Category: "Actor",
        Limit: 200,
        // wikibase:sitelinks causes 504s even without ORDER BY; dropped.
        Query: """
            SELECT DISTINCT ?person ?personLabel ?dob WHERE {
              VALUES ?occ { wd:Q33999 wd:Q10800557 wd:Q10798782 }
              ?person wdt:P106 ?occ ;
                      wdt:P569 ?dob .
              FILTER NOT EXISTS { ?person wdt:P570 ?d }
              FILTER(YEAR(NOW()) - YEAR(?dob) < 75)
              SERVICE wikibase:label { bd:serviceParam wikibase:language "en". }
            }
            LIMIT 200
            """),
    new CategorySpec(
        Category: "Musician",
        Limit: 200,
        // Singer + pop singer + rapper + singer-songwriter (no generic Q639669 "musician" —
        // that bucket includes every baroque harpsichordist and session bassist).
        Query: """
            SELECT DISTINCT ?person ?personLabel ?dob WHERE {
              VALUES ?occ { wd:Q177220 wd:Q205375 wd:Q2252262 wd:Q488205 }
              ?person wdt:P106 ?occ ;
                      wdt:P569 ?dob .
              FILTER NOT EXISTS { ?person wdt:P570 ?d }
              FILTER(YEAR(NOW()) - YEAR(?dob) < 75)
              SERVICE wikibase:label { bd:serviceParam wikibase:language "en". }
            }
            LIMIT 200
            """),
    new CategorySpec(
        Category: "K-pop",
        Limit: 100,
        // Genre P136 matches songs, so constrain to instance-of human or musical group.
        // Sitelinks threshold 25 (K-pop acts typically have fewer wiki sitelinks than
        // Hollywood actors but more than 25 when genuinely famous).
        Query: """
            SELECT DISTINCT ?person ?personLabel ?bornOrFormed WHERE {
              VALUES ?instanceOf { wd:Q5 wd:Q215380 }
              ?person wdt:P31 ?instanceOf ;
                      wdt:P136 wd:Q213665 .
              FILTER NOT EXISTS { ?person wdt:P570 ?d }
              FILTER NOT EXISTS { ?person wdt:P576 ?disbanded }
              OPTIONAL { ?person wdt:P569 ?dob }
              OPTIONAL { ?person wdt:P571 ?inception }
              BIND(COALESCE(?dob, ?inception) AS ?bornOrFormed)
              FILTER(BOUND(?bornOrFormed))
              FILTER(YEAR(NOW()) - YEAR(?bornOrFormed) < 75)
              SERVICE wikibase:label { bd:serviceParam wikibase:language "en". }
            }
            LIMIT 100
            """),
};

var totalInserted = 0;
var totalSkipped = 0;

foreach (var spec in categories)
{
    Console.WriteLine($"\n=== {spec.Category} (target {spec.Limit}) ===");

    var rows = await RunPass1Async(http, spec);
    Console.WriteLine($"  pass 1: {rows.Count} names (with QID + dob)");

    if (rows.Count == 0)
    {
        Console.WriteLine($"  pass 1 returned no rows; skipping {spec.Category}");
        continue;
    }

    await EnrichWithBioAndPhotoAsync(http, rows);
    var withPhoto = rows.Count(r => !string.IsNullOrWhiteSpace(r.PhotoUrl));
    var withBio = rows.Count(r => !string.IsNullOrWhiteSpace(r.Bio));
    Console.WriteLine($"  pass 2: enriched {withBio} bios / {withPhoto} photos");

    var (inserted, skipped) = await InsertAsync(connectionString, createdByUserId, spec.Category, rows, existingNames);
    Console.WriteLine($"  inserted {inserted}, skipped {skipped} (already in DB or insert failed)");
    totalInserted += inserted;
    totalSkipped += skipped;
}

Console.WriteLine($"\nDone. Total inserted: {totalInserted}. Total skipped: {totalSkipped}.");
return 0;

static Dictionary<string, string> ParseArgs(string[] argv)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i + 1 < argv.Length; i += 2)
    {
        d[argv[i]] = argv[i + 1];
    }
    return d;
}

static async Task<HashSet<string>> LoadExistingNamesAsync(string connectionString)
{
    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT Name FROM Celebrities";
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var name = reader.GetString(0);
        if (!string.IsNullOrWhiteSpace(name))
        {
            names.Add(name);
        }
    }
    return names;
}

static async Task<JsonDocument> ExecuteSparqlAsync(HttpClient http, string sparql, int maxAttempts = 4)
{
    var url = "https://query.wikidata.org/sparql?format=json&query=" + Uri.EscapeDataString(sparql);
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            using var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            return await JsonDocument.ParseAsync(stream);
        }
        catch (HttpRequestException ex) when (attempt < maxAttempts)
        {
            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
            Console.WriteLine($"  SPARQL attempt {attempt} failed: {ex.Message}; retrying in {delay.TotalSeconds}s");
            await Task.Delay(delay);
        }
    }
}

static async Task<List<SeedRow>> RunPass1Async(HttpClient http, CategorySpec spec)
{
    using var doc = await ExecuteSparqlAsync(http, spec.Query);
    var rows = new List<SeedRow>();
    var seenQids = new HashSet<string>(StringComparer.Ordinal);
    var bindings = doc.RootElement.GetProperty("results").GetProperty("bindings");
    foreach (var b in bindings.EnumerateArray())
    {
        var personUri = GetProp(b, "person");
        var name = GetProp(b, "personLabel");
        if (string.IsNullOrWhiteSpace(personUri) || string.IsNullOrWhiteSpace(name)) continue;

        // Wikidata's label service returns the raw entity id (e.g. "Q12345") when no
        // English label is set. Drop those — they'd end up as "Name = Q12345" rows.
        if (name.StartsWith("Q") && name.Length > 1 && name.Skip(1).All(char.IsDigit)) continue;

        var qid = personUri.Substring(personUri.LastIndexOf('/') + 1);
        // SELECT DISTINCT on (?person, ?personLabel, ?dob) still returns multiple rows
        // for the same person when more than one English label/description binding exists.
        // Dedupe by QID so each celebrity is inserted once.
        if (!seenQids.Add(qid)) continue;

        var row = new SeedRow { WikidataQid = qid, Name = name };

        if (GetProp(b, "dob") is { } dob && DateTime.TryParse(dob, out var d1)) row.BornOrFormed = d1;
        if (GetProp(b, "bornOrFormed") is { } bf && DateTime.TryParse(bf, out var d2)) row.BornOrFormed = d2;

        rows.Add(row);
    }
    return rows;
}

static async Task EnrichWithBioAndPhotoAsync(HttpClient http, List<SeedRow> rows)
{
    // Chunk by 50 QIDs. 50 keeps each pass-2 SPARQL's VALUES clause small enough
    // that Wikidata answers quickly while still giving ~4 round-trips for 200 rows.
    const int chunkSize = 50;
    for (var i = 0; i < rows.Count; i += chunkSize)
    {
        var slice = rows.Skip(i).Take(chunkSize).ToList();
        var valuesClause = string.Join(' ', slice.Select(r => "wd:" + r.WikidataQid));
        var q = """
            SELECT ?person ?desc ?img WHERE {
              VALUES ?person { __VALUES__ }
              OPTIONAL { ?person schema:description ?desc . FILTER(LANG(?desc) = "en") }
              OPTIONAL { ?person wdt:P18 ?img }
            }
            """.Replace("__VALUES__", valuesClause);

        using var doc = await ExecuteSparqlAsync(http, q);
        var bindings = doc.RootElement.GetProperty("results").GetProperty("bindings");
        var byQid = slice.ToDictionary(r => r.WikidataQid, r => r);
        foreach (var b in bindings.EnumerateArray())
        {
            var personUri = GetProp(b, "person");
            if (personUri is null) continue;
            var qid = personUri.Substring(personUri.LastIndexOf('/') + 1);
            if (!byQid.TryGetValue(qid, out var row)) continue;

            if (GetProp(b, "desc") is { } desc && string.IsNullOrWhiteSpace(row.Bio))
            {
                row.Bio = desc;
            }
            if (GetProp(b, "img") is { } img && string.IsNullOrWhiteSpace(row.PhotoUrl))
            {
                row.PhotoUrl = img + "?width=400";
            }
        }
    }
}

static string? GetProp(JsonElement binding, string prop)
{
    if (!binding.TryGetProperty(prop, out var el)) return null;
    if (!el.TryGetProperty("value", out var v)) return null;
    return v.GetString();
}

static async Task<(int inserted, int skipped)> InsertAsync(
    string connectionString,
    string createdByUserId,
    string category,
    List<SeedRow> rows,
    HashSet<string> existingNames)
{
    var inserted = 0;
    var skipped = 0;

    await using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();

    foreach (var row in rows)
    {
        if (!existingNames.Add(row.Name!))
        {
            skipped++;
            continue;
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Celebrities (Name, Category, Bio, PhotoUrl, DateOfBirth, CreatedByUserId)
            VALUES (@name, @category, @bio, @photoUrl, @dob, @createdBy);
            """;
        cmd.Parameters.AddWithValue("@name", row.Name);
        cmd.Parameters.AddWithValue("@category", category);
        cmd.Parameters.AddWithValue("@bio", (object?)row.Bio ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@photoUrl", (object?)row.PhotoUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dob", row.BornOrFormed.HasValue ? row.BornOrFormed.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@createdBy", createdByUserId);

        try
        {
            await cmd.ExecuteNonQueryAsync();
            inserted++;
        }
        catch (SqlException ex)
        {
            Console.Error.WriteLine($"  INSERT failed for '{row.Name}': {ex.Message}");
            skipped++;
            existingNames.Remove(row.Name!);
        }
    }

    return (inserted, skipped);
}

internal sealed record CategorySpec(string Category, int Limit, string Query);

internal sealed class SeedRow
{
    public string WikidataQid { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Bio { get; set; }
    public string? PhotoUrl { get; set; }
    public DateTime? BornOrFormed { get; set; }
}
