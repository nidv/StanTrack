using System.Text.Json;
using Microsoft.Data.SqlClient;

// Wikidata seeder for StanTrack Celebrities.
// Run from Windows against the LocalDB instance:
//   dotnet run --project Tools/CelebritySeeder -- `
//     --connection-string "Server=(localdb)\mssqllocaldb;Database=StanTrackDb;Trusted_Connection=True;MultipleActiveResultSets=true" `
//     --created-by-user-id "<Id from AspNetUsers for admin@stantrack.local>"
//
// Two modes:
//   * Default (no --csv): original category-bucketed SPARQL queries over Wikidata (~200 actors
//     + ~200 musicians + ~100 k-pop). Bulk-fetch but hits whoever Wikidata returns, fame varies.
//   * --csv <path> mode: reads curated (rank,name,category) rows, resolves each name via the
//     Wikidata wbsearchentities API (NOT SPARQL), then runs the same pass-2 SPARQL enrichment
//     (bio + photo). Maps CSV categories onto the StanTrack schema:
//        actor       -> Actor
//        actress     -> Actress
//        artist      -> Artist
//        k-pop group -> K-Pop
//     In --csv mode callers typically wipe Celebrities first; this mode does NOT wipe, it
//     just inserts (dedup happens against the existing-names set loaded at startup).

var argsDict = ParseArgs(args);
if (!argsDict.TryGetValue("--connection-string", out var connectionString) ||
    !argsDict.TryGetValue("--created-by-user-id", out var createdByUserId))
{
    Console.Error.WriteLine("Usage: CelebritySeeder --connection-string <cs> --created-by-user-id <guid> [--csv <path-to-top-100.csv>]");
    return 2;
}

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("StanTrack-CelebritySeeder/1.0 (https://github.com/lexutb/StanTrack)");
http.DefaultRequestHeaders.Accept.ParseAdd("application/sparql-results+json");
http.Timeout = TimeSpan.FromSeconds(120);

var existingNames = await LoadExistingNamesAsync(connectionString);
Console.WriteLine($"Existing celebrities in DB: {existingNames.Count}");

int totalInserted = 0;
int totalSkipped = 0;

if (argsDict.TryGetValue("--csv", out var csvPath))
{
    (totalInserted, totalSkipped) = await RunCsvModeAsync(http, csvPath, connectionString, createdByUserId, existingNames);
}
else
{
    (totalInserted, totalSkipped) = await RunBulkCategoriesModeAsync(http, connectionString, createdByUserId, existingNames);
}

Console.WriteLine($"\nDone. Total inserted: {totalInserted}. Total skipped: {totalSkipped}.");
return 0;

static async Task<(int, int)> RunCsvModeAsync(HttpClient http, string csvPath, string connectionString, string createdByUserId, HashSet<string> existingNames)
{
    var rows = LoadCsv(csvPath);
    Console.WriteLine($"CSV loaded: {rows.Count} rows");
    Console.WriteLine($"  Actor: {rows.Count(r => r.Category == "Actor")}");
    Console.WriteLine($"  Actress: {rows.Count(r => r.Category == "Actress")}");
    Console.WriteLine($"  Artist: {rows.Count(r => r.Category == "Artist")}");
    Console.WriteLine($"  K-Pop: {rows.Count(r => r.Category == "K-Pop")}");

    // Resolve names to QIDs via wbsearchentities. Sequential with a small pause: Wikidata
    // is polite about it but we don't need to hammer them with 400 concurrent lookups.
    var resolved = new List<SeedRow>();
    var unresolved = new List<string>();
    foreach (var csv in rows)
    {
        var qid = await ResolveQidByNameAsync(http, csv.Name, csv.CsvCategory);
        if (qid is null)
        {
            Console.WriteLine($"  unresolved: {csv.Name} ({csv.CsvCategory})");
            unresolved.Add(csv.Name);
            continue;
        }
        resolved.Add(new SeedRow
        {
            WikidataQid = qid,
            Name = csv.Name,
            Category = csv.Category,
        });
        // ~200ms pause between search calls -> ~80s total for 400 names. Well under Wikidata's
        // tolerance. Search endpoint is way cheaper than SPARQL too.
        await Task.Delay(200);
    }
    Console.WriteLine($"Resolved {resolved.Count}/{rows.Count} names to QIDs; {unresolved.Count} unresolved.");

    // Enrich: bio + photo + dob, same shape as bulk mode but restricted to resolved QIDs.
    await EnrichWithBioPhotoAndDobAsync(http, resolved);
    var withPhoto = resolved.Count(r => !string.IsNullOrWhiteSpace(r.PhotoUrl));
    var withBio = resolved.Count(r => !string.IsNullOrWhiteSpace(r.Bio));
    var withDob = resolved.Count(r => r.BornOrFormed.HasValue);
    Console.WriteLine($"Enriched: {withBio} bios / {withPhoto} photos / {withDob} dobs");

    return await InsertAsync(connectionString, createdByUserId, resolved, existingNames);
}

static async Task<(int, int)> RunBulkCategoriesModeAsync(HttpClient http, string connectionString, string createdByUserId, HashSet<string> existingNames)
{
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
            Category: "K-Pop",
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

    var inserted = 0;
    var skipped = 0;

    foreach (var spec in categories)
    {
        Console.WriteLine($"\n=== {spec.Category} (target {spec.Limit}) ===");

        var rows = await RunBulkPass1Async(http, spec);
        Console.WriteLine($"  pass 1: {rows.Count} names (with QID + dob)");

        if (rows.Count == 0)
        {
            Console.WriteLine($"  pass 1 returned no rows; skipping {spec.Category}");
            continue;
        }

        await EnrichWithBioPhotoAndDobAsync(http, rows);
        var withPhoto = rows.Count(r => !string.IsNullOrWhiteSpace(r.PhotoUrl));
        var withBio = rows.Count(r => !string.IsNullOrWhiteSpace(r.Bio));
        Console.WriteLine($"  pass 2: enriched {withBio} bios / {withPhoto} photos");

        // Bulk mode has a single category for the whole batch — apply it before insert.
        foreach (var r in rows) r.Category = spec.Category;

        var (i, s) = await InsertAsync(connectionString, createdByUserId, rows, existingNames);
        Console.WriteLine($"  inserted {i}, skipped {s} (already in DB or insert failed)");
        inserted += i;
        skipped += s;
    }

    return (inserted, skipped);
}

static Dictionary<string, string> ParseArgs(string[] argv)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i + 1 < argv.Length; i += 2)
    {
        d[argv[i]] = argv[i + 1];
    }
    return d;
}

static List<CsvRow> LoadCsv(string path)
{
    var rows = new List<CsvRow>();
    string? header = null;
    foreach (var line in File.ReadLines(path))
    {
        if (string.IsNullOrWhiteSpace(line)) continue;
        if (header is null)
        {
            header = line;
            continue;
        }
        // CSV is rank,name,category; names are simple (no embedded commas seen in practice).
        // If a name ever does need a comma, swap this for a real CSV parser.
        var parts = line.Split(',', 3);
        if (parts.Length < 3) continue;
        var csvCategory = parts[2].Trim().ToLowerInvariant();
        var mapped = csvCategory switch
        {
            "actor" => "Actor",
            "actress" => "Actress",
            "artist" => "Artist",
            "k-pop group" => "K-Pop",
            _ => (string?)null,
        };
        if (mapped is null)
        {
            Console.Error.WriteLine($"  CSV row skipped (unknown category '{csvCategory}'): {line}");
            continue;
        }
        rows.Add(new CsvRow
        {
            Name = parts[1].Trim(),
            CsvCategory = csvCategory,
            Category = mapped,
        });
    }
    return rows;
}

static async Task<string?> ResolveQidByNameAsync(HttpClient http, string name, string csvCategory)
{
    // wbsearchentities is Wikidata's general-purpose search. For K-pop we deliberately
    // bias toward groups by ALSO asking for instance-of filtering on the result side:
    // we fetch the top 5 candidates and prefer one whose description mentions "group"/"band"
    // or whose entity we can verify is Q215380 in a followup call. To keep this simple
    // we just take the top hit EXCEPT for k-pop, where we take the first candidate whose
    // description hints at "group", "band", "boy band", "girl group" — falling back to
    // the top hit if none match.

    // Try the name as-is first; fall back to a parenthetical-stripped form if that returns nothing.
    // "WJSN (Cosmic Girls)" doesn't match anything, but "WJSN" does.
    var qid = await TrySearchAsync(http, name, csvCategory);
    if (qid is null)
    {
        var stripped = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\([^)]*\)", "").Trim();
        if (!string.Equals(stripped, name, StringComparison.Ordinal))
        {
            qid = await TrySearchAsync(http, stripped, csvCategory);
        }
    }
    return qid;
}

static async Task<string?> TrySearchAsync(HttpClient http, string query, string csvCategory)
{
    var url = "https://www.wikidata.org/w/api.php?action=wbsearchentities"
            + "&search=" + Uri.EscapeDataString(query)
            + "&language=en&format=json&limit=5";
    using var response = await http.GetAsync(url);
    response.EnsureSuccessStatusCode();
    await using var stream = await response.Content.ReadAsStreamAsync();
    using var doc = await JsonDocument.ParseAsync(stream);
    if (!doc.RootElement.TryGetProperty("search", out var search) || search.GetArrayLength() == 0)
    {
        return null;
    }

    var isKpop = csvCategory == "k-pop group";
    string? topQid = null;
    foreach (var hit in search.EnumerateArray())
    {
        var qid = hit.GetProperty("id").GetString();
        if (qid is null) continue;
        topQid ??= qid;

        if (!isKpop)
        {
            // Non-k-pop: take the first hit and move on.
            return qid;
        }

        // K-pop: prefer a hit whose description suggests a musical group.
        if (hit.TryGetProperty("description", out var descEl))
        {
            var desc = descEl.GetString() ?? string.Empty;
            var d = desc.ToLowerInvariant();
            if (d.Contains("group") || d.Contains("band"))
            {
                return qid;
            }
        }
    }
    return topQid;
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

static async Task<List<SeedRow>> RunBulkPass1Async(HttpClient http, CategorySpec spec)
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

static async Task EnrichWithBioPhotoAndDobAsync(HttpClient http, List<SeedRow> rows)
{
    // Chunk by 50 QIDs. 50 keeps each SPARQL's VALUES clause small enough that Wikidata
    // answers quickly while still giving ~8 round-trips for 400 rows. We fetch description,
    // image, and date of birth in one pass — dob is needed in CSV mode where pass-1 is a
    // name-search API that doesn't return dates.
    const int chunkSize = 50;
    for (var i = 0; i < rows.Count; i += chunkSize)
    {
        var slice = rows.Skip(i).Take(chunkSize).ToList();
        var valuesClause = string.Join(' ', slice.Select(r => "wd:" + r.WikidataQid));
        var q = """
            SELECT ?person ?desc ?img ?dob ?inception WHERE {
              VALUES ?person { __VALUES__ }
              OPTIONAL { ?person schema:description ?desc . FILTER(LANG(?desc) = "en") }
              OPTIONAL { ?person wdt:P18 ?img }
              OPTIONAL { ?person wdt:P569 ?dob }
              OPTIONAL { ?person wdt:P571 ?inception }
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
            if (!row.BornOrFormed.HasValue)
            {
                if (GetProp(b, "dob") is { } dob && DateTime.TryParse(dob, out var parsedDob))
                {
                    row.BornOrFormed = parsedDob;
                }
                else if (GetProp(b, "inception") is { } inc && DateTime.TryParse(inc, out var parsedInc))
                {
                    row.BornOrFormed = parsedInc;
                }
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
    List<SeedRow> rows,
    HashSet<string> existingNames)
{
    var inserted = 0;
    var skipped = 0;

    await using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();

    foreach (var row in rows)
    {
        if (row.Category is null)
        {
            Console.Error.WriteLine($"  SeedRow '{row.Name}' has no Category; skipping.");
            skipped++;
            continue;
        }
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
        cmd.Parameters.AddWithValue("@category", row.Category);
        cmd.Parameters.AddWithValue("@bio", (object?)row.Bio ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@photoUrl", (object?)row.PhotoUrl ?? DBNull.Value);
        // Celebrity.DateOfBirth is datetime2, but AddWithValue infers datetime (range 1753–9999).
        // Wikidata P571 (inception) sometimes has ancient dates for misresolved groups, which overflows
        // datetime and crashes the whole batch. Explicitly type as datetime2 and clamp silly values to null.
        var dobParam = cmd.Parameters.Add("@dob", System.Data.SqlDbType.DateTime2);
        dobParam.Value = row.BornOrFormed.HasValue && row.BornOrFormed.Value.Year >= 1800
            ? row.BornOrFormed.Value
            : DBNull.Value;
        cmd.Parameters.AddWithValue("@createdBy", createdByUserId);

        try
        {
            await cmd.ExecuteNonQueryAsync();
            inserted++;
        }
        catch (Exception ex)
        {
            // Catch broadly: SqlException for constraint violations, SqlTypeException for
            // type-mapping overflows, etc. One bad row should never kill the entire seed run.
            Console.Error.WriteLine($"  INSERT failed for '{row.Name}': {ex.GetType().Name}: {ex.Message}");
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
    public string? Category { get; set; }
    public string? Bio { get; set; }
    public string? PhotoUrl { get; set; }
    public DateTime? BornOrFormed { get; set; }
}

internal sealed class CsvRow
{
    public required string Name { get; init; }
    public required string CsvCategory { get; init; }
    public required string Category { get; init; }
}
