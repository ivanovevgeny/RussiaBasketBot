using System.Globalization;
using RussiaBasketBot.Models;

namespace RussiaBasketBot.Services;

public class BasketballApiClient(IHttpClientFactory httpClientFactory)
{
    private const string ApiUrl = "https://basket2.sportoteka.org/api/abc/comps";
    private const string SiteUrl = "https://russiabasket.ru";

    private async Task<T> Get<T>(string url, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient("basketball");
        return await client.GetFromJsonAsync<T>(url, cancellationToken)
            ?? throw new InvalidDataException($"Basketball API returned an empty response: {url}");
    }

    private static void EnsureSuccess(string status, string? message)
    {
        if (status != "Ok")
            throw new InvalidDataException($"Basketball API failed: {message ?? status}");
    }

    private async Task<int> GetCurrentSeason(CancellationToken cancellationToken)
    {
        // Тот же источник сезона, который использует новая страница соревнования.
        var response = await Get<LeaguePageResponse>(
            "https://a.russiabasket.org/api/view/page/league/msl", cancellationToken);
        EnsureSuccess(response.Status, response.Message);
        var season = response.Result.League.Season;
        if (season <= 0) throw new InvalidDataException("Basketball API returned an invalid season");
        return season;
    }

    private async Task<List<T>> GetItems<T>(string query, CancellationToken cancellationToken)
    {
        var response = await Get<ApiListResponse<T>>($"{ApiUrl}/{query}", cancellationToken);
        EnsureSuccess(response.Status, response.Message);
        // Нельзя заменять БД частичным календарём: по умолчанию API отдаёт только 10 игр.
        if (response.Items.Count != response.TotalCount)
            throw new InvalidDataException($"Incomplete basketball API response: {response.Items.Count} of {response.TotalCount} items");
        return response.Items;
    }

    public async Task<List<Team>> GetTeams(CancellationToken cancellationToken = default)
    {
        var season = await GetCurrentSeason(cancellationToken);
        var rosters = await GetItems<ApiRoster>($"rosters?tag=msl&season={season}", cancellationToken);
        return rosters.Select(roster => new Team
        {
            TeamId = roster.Team.Id,
            Name = roster.Team.Name.Trim(),
            City = roster.Team.RegionName?.Trim() ?? "",
            LogoUrl = roster.Team.Logo ?? "",
            Url = $"{SiteUrl}/team/{roster.Team.Id}?league=msl&season={season}",
            Created = DateTime.UtcNow
        }).ToList();
    }

    public async Task<List<Match>> GetMatches(CancellationToken cancellationToken = default)
    {
        var season = await GetCurrentSeason(cancellationToken);
        var games = await GetItems<ApiCalendarEntry>(
            $"calendar?tag=msl&season={season}&calendarType=-1&maxResultCount=1000", cancellationToken);
        return games.Select(entry => new Match
        {
            MatchId = entry.Game.Id,
            Date = GetGameDate(entry.Game),
            HomeTeamId = entry.Team1.TeamId,
            GuestTeamId = entry.Team2.TeamId,
            HomeScore = entry.Game.Score1,
            GuestScore = entry.Game.Score2,
            Status = entry.Game.GameStatus switch
            {
                "Scheduled" => MatchStatus.Plan,
                "Online" => MatchStatus.Live,
                "Result" or "ResultConfirmed" => MatchStatus.Finish,
                _ => throw new InvalidDataException($"Unknown game status: {entry.Game.GameStatus}")
            },
            Stage = entry.Comp.Name,
            Url = $"{SiteUrl}/game/{entry.Game.Id}?league=msl",
            Created = DateTime.UtcNow
        }).ToList();
    }

    private static DateTime GetGameDate(ApiGame game)
    {
        if (game.HasTime)
            return game.ScheduledTime?.UtcDateTime
                ?? throw new InvalidDataException($"Game {game.Id} has no scheduled time");

        // Если время ещё не назначено, сохраняем прежнее условное время: 12:00 МСК.
        var date = DateTime.ParseExact(game.LocalDate, "dd.MM.yyyy", CultureInfo.InvariantCulture);
        return date.AddHours(12).MscToUtc();
    }

    public async Task<List<StandingEntry>> GetStandings(CancellationToken cancellationToken = default)
    {
        var season = await GetCurrentSeason(cancellationToken);
        var tables = await GetItems<StandingsTable>($"standings?tag=msl&season={season}", cancellationToken);
        // Заголовки этапов тоже входят в items, но не содержат строк таблицы.
        var table = tables.FirstOrDefault(item => item.Standings.Count > 0);
        if (table == null) return [];

        return table.Standings.OrderBy(team => team.Place).Select(team => new StandingEntry(
            team.Place,
            string.IsNullOrWhiteSpace(team.RegionName)
                ? team.Name.Trim()
                : $"{team.Name.Trim()} ({team.RegionName.Trim()})",
            team.TotalGames, team.TotalWin, team.TotalDefeat, team.TotalPoints)).ToList();
    }
}
