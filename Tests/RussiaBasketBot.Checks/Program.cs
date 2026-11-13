using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Hangfire;
using MongoDB.Driver;
using RussiaBasketBot;
using RussiaBasketBot.Models;
using RussiaBasketBot.Services;

var fixtures = new FixtureFactory();
await PackageCompatibilityChecks.CheckHost();
var api = new BasketballApiClient(fixtures);
var expected = new[]
{
    new StandingEntry(1, "Динамо (Уфа)", 8, 7, 1, 15),
    new StandingEntry(2, "Темп-СУМЗ (Ревда)", 8, 7, 1, 15),
    new StandingEntry(3, "ЧБК (Челябинская область)", 8, 7, 1, 15),
    new StandingEntry(4, "Динамо (Грозный)", 9, 7, 2, 16),
    new StandingEntry(5, "Химки (Московская область)", 9, 6, 3, 15),
    new StandingEntry(6, "ЦСКА-2 (Москва)", 9, 6, 3, 15),
    new StandingEntry(7, "Тамбов (Тамбовская область)", 8, 3, 5, 11),
    new StandingEntry(8, "Барнаул (Алтайский край)", 8, 3, 5, 11),
    new StandingEntry(9, "Металлург (Магнитогорск)", 8, 3, 5, 11),
    new StandingEntry(10, "Новосибирск (Новосибирск)", 8, 3, 5, 11),
    new StandingEntry(11, "Югра (Сургут)", 9, 3, 6, 12),
    new StandingEntry(12, "Десна (Брянская область)", 8, 1, 7, 9),
    new StandingEntry(13, "Купол-Родники (Ижевск)", 8, 1, 7, 9),
    new StandingEntry(14, "Русичи (Курск)", 8, 1, 7, 9)
};
Check((await api.GetStandings()).SequenceEqual(expected), "All 14 standings match screenshot, including official places and points");
var teams = await api.GetTeams();
Check(teams.Count == 14 && teams.Any(t => t.TeamId == 36937 && t.Name == "Динамо" && t.City == "Уфа"),
    "Roster maps stable team IDs, names and regions");
Check(teams.All(t => t.Url.StartsWith("https://russiabasket.ru/team/") && t.Url.EndsWith("season=2027") && t.LogoUrl.StartsWith("https://")),
    "Team links use the new site and current season; logos are used directly");
var games = await api.GetMatches();
var finished = games.Single(g => g.MatchId == 1083519);
Check(finished.HomeTeamId == 741 && finished.GuestTeamId == 36937 && finished.HomeScore == 63 && finished.GuestScore == 86,
    "Calendar uses teamId, preserves home/away order and score");
Check(finished.Date == new DateTime(2026, 11, 12, 16, 0, 0, DateTimeKind.Utc) && finished.Status == MatchStatus.Finish,
    "Offset +05:00 converts to UTC; ResultConfirmed is finished");
Check(games.Single(g => g.MatchId == 1083533).Date == new DateTime(2026, 12, 4, 9, 0, 0, DateTimeKind.Utc),
    "Unassigned time uses 12:00 Moscow on the supplied calendar date");
Check(fixtures.Requests.Any(u => u.Query.Contains("calendarType=-1") && u.Query.Contains("maxResultCount=1000")),
    "Calendar requests all statuses and up to 1000 games");
var rosterIds = teams.Select(t => t.TeamId).ToHashSet();
Check(games.All(g => rosterIds.Contains(g.HomeTeamId) && rosterIds.Contains(g.GuestTeamId)), "Calendar and roster team IDs agree");

var statusData = JsonNode.Parse(fixtures.Responses["calendar"])!;
foreach (var (status, mapped) in new[] { ("Scheduled", MatchStatus.Plan), ("Online", MatchStatus.Live), ("Result", MatchStatus.Finish), ("ResultConfirmed", MatchStatus.Finish) })
{
    statusData["items"]![0]!["game"]!["gameStatus"] = status;
    fixtures.Responses["calendar"] = statusData.ToJsonString();
    Check((await api.GetMatches())[0].Status == mapped, "Status mapping: " + status);
}
statusData["items"]![0]!["game"]!["gameStatus"] = "Unexpected";
fixtures.Responses["calendar"] = statusData.ToJsonString();
await Expect<InvalidDataException>(async () => { await api.GetMatches(); }, "Unknown status fails instead of becoming a planned game");
fixtures.Reset("calendar");

var missing = JsonNode.Parse(fixtures.Responses["standings"])!;
missing["items"]![1]!["standings"]![0]!.AsObject().Remove("totalPoints");
fixtures.Responses["standings"] = missing.ToJsonString();
await Expect<JsonException>(async () => { await api.GetStandings(); }, "Missing totals fail instead of showing zeroes");
fixtures.Reset("standings");

var multiple = JsonNode.Parse(fixtures.Responses["standings"])!;
var rows = multiple["items"]![1]!["standings"]!.AsArray();
var first = rows[0]!.DeepClone();
rows.RemoveAt(0);
rows.Add(first);
multiple["items"]!.AsArray().Add(multiple["items"]![1]!.DeepClone());
multiple["totalCount"] = 4;
fixtures.Responses["standings"] = multiple.ToJsonString();
Check((await api.GetStandings()).SequenceEqual(expected), "Skip empty headings, order by official place, keep separate stage tables separate");
fixtures.Reset("standings");

fixtures.Responses["calendar"] = """{"status":"Ok","totalCount":182,"items":[]}""";
await Expect<InvalidDataException>(async () => { await api.GetMatches(); }, "Incomplete calendar is rejected before persistence");
fixtures.Responses["calendar"] = """{"status":"Error","message":"Unavailable","totalCount":0,"items":[]}""";
await Expect<InvalidDataException>(async () => { await api.GetMatches(); }, "API-level errors propagate");
fixtures.Responses["calendar"] = "not json";
await Expect<JsonException>(async () => { await api.GetMatches(); }, "Malformed JSON is rejected");
fixtures.Responses["calendar"] = "null";
await Expect<InvalidDataException>(async () => { await api.GetMatches(); }, "Null response is rejected");
fixtures.Reset("calendar");
fixtures.Status = HttpStatusCode.ServiceUnavailable;
await Expect<HttpRequestException>(async () => { await api.GetMatches(); }, "HTTP 503 propagates");
fixtures.Status = HttpStatusCode.OK;
using (var cancellation = new CancellationTokenSource())
{
    cancellation.Cancel();
    await Expect<OperationCanceledException>(async () => { await api.GetStandings(cancellation.Token); }, "Cancellation propagates");
}

fixtures.Season = 2031;
fixtures.Responses["league"] = """{"status":"Ok","result":{"league":{"season":2031}}}""";
Check((await api.GetTeams()).All(t => t.Url.EndsWith("season=2031")), "Season comes from league metadata, without a hardcoded year");
fixtures.Season = 2027;
fixtures.Reset("league");

var parser = new ParserService(NullLogger<ParserService>.Instance, null!, api);
var notify = new NotifyService(NullLogger<NotifyService>.Instance, null!, parser, null!, null!);
var message = await notify.GetStandingsMessage(CancellationToken.None);
var calls = fixtures.Requests.Count;
Check(message.Contains("Динамо (Уфа)") && message.Contains("Динамо (Грозный)") && message.Length < 4096,
    "Telegram message distinguishes both Dynamo teams and fits message limit");
Check(await notify.GetStandingsMessage(CancellationToken.None) == message && fixtures.Requests.Count == calls,
    "Standings message cache avoids additional API calls");

if (args.Contains("--mongo"))
{
    var databaseName = "rbbot_api_checks_" + Guid.NewGuid().ToString("N");
    var connectionString = $"mongodb://localhost:27017/{databaseName}?serverSelectionTimeoutMS=3000";
    var mongo = new MongoClient(connectionString);
    var db = new MongoDbContext(connectionString);
    var dbParser = new ParserService(NullLogger<ParserService>.Instance, db, api);
    var basketball = new BasketballService(NullLogger<BasketballService>.Instance, db, dbParser);
    try
    {
        Check(await dbParser.ParseTeams() == 14, "Mongo: import all 14 API teams");
        var originalCreated = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var existing = new Match { MatchId = 1083519, Status = MatchStatus.Finish, HomeScore = 0, GuestScore = 0, Created = originalCreated };
        await db.Matches.InsertOneAsync(existing);
        await db.Matches.InsertOneAsync(new Match { MatchId = 999999 });
        await dbParser.ParseMatches(false);
        var saved = await db.Matches.Find(g => g.MatchId == 1083519).SingleAsync();
        Check(saved.Id == existing.Id && saved.Created == originalCreated && saved.HomeScore == 63 && saved.GuestScore == 86
              && saved.Date == finished.Date && saved.Url == finished.Url, "Mongo: same-status update refreshes scores, date and URL, preserving internal ID and creation time");
        Check(await db.Matches.CountDocumentsAsync(g => g.MatchId == 999999) == 1, "Mongo: incremental import retains other games");
        await dbParser.ParseMatches(true);
        Check(await db.Matches.CountDocumentsAsync(g => g.MatchId == 999999) == 0, "Mongo: full import removes obsolete games after successful upserts");

        var liveData = JsonNode.Parse(fixtures.Responses["calendar"])!;
        liveData["items"]![0]!["game"]!["gameStatus"] = "Online";
        liveData["items"]![0]!["game"]!["score1"] = 10;
        fixtures.Responses["calendar"] = liveData.ToJsonString();
        await dbParser.ParseMatches(false);
        liveData["items"]![0]!["game"]!["score1"] = 15;
        fixtures.Responses["calendar"] = liveData.ToJsonString();
        await dbParser.ParseMatches(false);
        Check((await db.Matches.Find(g => g.MatchId == 1083466).SingleAsync()).HomeScore == 15, "Mongo: live scores update while status stays Online");
        fixtures.Reset("calendar");

        var beforeFailure = await db.Matches.CountDocumentsAsync(_ => true);
        fixtures.Responses["calendar"] = """{"status":"Ok","totalCount":182,"items":[]}""";
        await Expect<InvalidDataException>(async () => { await dbParser.ParseMatches(true); }, "Mongo: incomplete upstream response fails");
        Check(await db.Matches.CountDocumentsAsync(_ => true) == beforeFailure, "Mongo: failed upstream response preserves stored games");
        fixtures.Reset("calendar");

        Check((await basketball.GetMatches(true)).Count == 2, "Mongo: upcoming query refreshes API and returns planned games");
        Check((await basketball.GetMatches(false)).All(g => g.Status == MatchStatus.Finish && !string.IsNullOrEmpty(g.HomeTeamName)),
            "Mongo: recent query refreshes API and resolves team names");
        var teamId = games.First(g => g.Status == MatchStatus.Plan).HomeTeamId;
        Check((await basketball.GetMatchesByTeam(teamId)).All(g => g.HomeTeamId == teamId || g.GuestTeamId == teamId),
            "Mongo: team schedule uses the same API team IDs");

        var midnightData = JsonNode.Parse(fixtures.Responses["calendar"])!;
        var midnight = midnightData["items"]![0]!.DeepClone();
        midnight["game"]!["id"] = 777777;
        midnight["game"]!["gameStatus"] = "Scheduled";
        midnight["game"]!["scheduledTime"] = "2026-11-14T00:30:00+03:00";
        midnightData["items"]!.AsArray().Add(midnight);
        midnightData["totalCount"] = 5;
        fixtures.Responses["calendar"] = midnightData.ToJsonString();
        Check((await basketball.GetMatches(true, date: new DateOnly(2026, 11, 14))).Any(g => g.MatchId == 777777),
            "Mongo: date filter uses Moscow date for games after midnight");
        fixtures.Reset("calendar");

        var teamBefore = await db.Teams.Find(t => t.TeamId == 36937).SingleAsync();
        var rosterUpdate = JsonNode.Parse(fixtures.Responses["rosters"])!;
        foreach (var row in rosterUpdate["items"]!.AsArray())
            if (row!["team"]!["id"]!.GetValue<int>() == 36937) row["team"]!["logo"] = "https://example.test/new-logo.png";
        fixtures.Responses["rosters"] = rosterUpdate.ToJsonString();
        var teamsAfter = await basketball.GetTeams();
        Check(teamsAfter.Single(t => t.TeamId == 36937).LogoUrl.EndsWith("new-logo.png")
              && teamsAfter.Single(t => t.TeamId == 36937).Id == teamBefore.Id, "Mongo: team query refreshes logo, preserving internal ID");
        fixtures.Reset("rosters");

        var services = new ServiceCollection();
        services.AddLogging();
        RussiaBasketBot.ServiceCollectionExtensions.AddHangfire(services, connectionString);
        using var provider = services.BuildServiceProvider();
        var jobs = provider.GetRequiredService<IBackgroundJobClient>();
        var jobId = jobs.Enqueue(() => Console.WriteLine("Package compatibility check"));
        using var storageConnection = provider.GetRequiredService<JobStorage>().GetConnection();
        Check(storageConnection.GetJobData(jobId)?.State == "Enqueued", "Mongo: updated Hangfire storage can persist and read jobs");
        Check(jobs.Delete(jobId), "Mongo: updated Hangfire storage can delete jobs");
    }
    finally
    {
        await mongo.DropDatabaseAsync(databaseName);
        Console.WriteLine("Removed temporary test database: " + databaseName);
    }
}

if (args.Contains("--live"))
{
    var live = new BasketballApiClient(new LiveFactory());
    var liveTeams = await live.GetTeams();
    var liveGames = await live.GetMatches();
    var liveStandings = await live.GetStandings();
    Check(liveTeams.Count == 14 && liveStandings.Count == 14 && liveGames.Count == 182,
        "LIVE API: 14 teams, 14 standings rows, all 182 games");
    var ids = liveTeams.Select(t => t.TeamId).ToHashSet();
    Check(liveGames.All(g => ids.Contains(g.HomeTeamId) && ids.Contains(g.GuestTeamId) && g.Date.Kind == DateTimeKind.Utc),
        "LIVE API: calendar references roster teams and all dates are UTC");
    Check(liveStandings.SequenceEqual(expected), "LIVE API: standings match screenshot exactly");
}
Console.WriteLine("All checks passed.");

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
}
static async Task Expect<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); }
    catch (T) { Console.WriteLine("PASS: " + name); return; }
    throw new Exception("FAIL: " + name);
}

sealed class FixtureFactory : HttpMessageHandler, IHttpClientFactory
{
    public Dictionary<string, string> Responses { get; } = new();
    public List<Uri> Requests { get; } = [];
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public int Season { get; set; } = 2027;
    public FixtureFactory()
    {
        foreach (var name in new[] { "league", "rosters", "calendar", "standings" }) Reset(name);
    }
    public void Reset(string name) => Responses[name] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".json"));
    public HttpClient CreateClient(string name) => new(this, false);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uri = request.RequestUri!;
        Requests.Add(uri);
        var key = uri.Host == "a.russiabasket.org" && uri.AbsolutePath == "/api/view/page/league/msl"
            ? "league" : uri.Segments.Last();
        if (key != "league" && (!uri.Query.Contains("tag=msl") || !uri.Query.Contains($"season={Season}")))
            throw new Exception("API request did not use league metadata: " + uri);
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Responses[key]) });
    }
}

sealed class LiveFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new() { Timeout = TimeSpan.FromSeconds(30) };
}
