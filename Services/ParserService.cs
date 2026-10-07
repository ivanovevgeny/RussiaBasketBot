using HtmlAgilityPack;
using MongoDB.Driver;
using RussiaBasketBot.Models;
using Match = RussiaBasketBot.Models.Match;

namespace RussiaBasketBot.Services;

public class ParserService(ILogger<ParserService> logger, MongoDbContext db)
{
    private const string BaseUrl = "https://competitions.russiabasket.ru";

    public async Task<int> ParseTeams()
    {
        try
        {
            logger.LogInformation("Starting team database initialization");

            var web = new HtmlWeb();
            var doc = await web.LoadFromWebAsync($"{BaseUrl}/superliga/men/teams/");
            var teamNodes = doc.DocumentNode.SelectNodes("//a[contains(@class, ' teams-item ')]");
            if (teamNodes == null)
            {
                logger.LogWarning("Team nodes not found");
                return 0;
            }

            var teamsCollection = db.Teams;

            var teams = new List<Team>();
            foreach (var teamNode in teamNodes)
            {
                try
                {
                    var href = teamNode.GetAttributeValue("href", "");
                    var team = new Team
                    {
                        TeamId = ParseUtils.ExtractTeamId(href) ?? 0,
                        Url = href,
                        Name = teamNode.SelectSingleNode(".//p[contains(@class, 'teams-item__name ')]")?.InnerText.Trim() ?? "",
                        City = teamNode.SelectSingleNode(".//span[contains(@class, 'teams-item__place ')]")?.InnerText.Trim() ?? "",
                        LogoUrl = teamNode.SelectSingleNode(".//picture/img")?.GetAttributeValue("src", "") ?? "",
                        Created = DateTime.UtcNow
                    };

                    if (!string.IsNullOrEmpty(team.Url))
                        team.Url = $"{BaseUrl}{team.Url}";

                    // LogoUrl may be "https://org.infobasket.su/Widget/GetTeamLogo/2237?compId=0" and redirects to another url
                    if (!string.IsNullOrEmpty(team.LogoUrl))
                    {
                        if (team.LogoUrl.Contains("GetTeamLogo"))
                        {
                            var newLogoUrl = await Utils.GetRedirectLocationAsync(team.LogoUrl);
                            if (!string.IsNullOrEmpty(newLogoUrl))
                                team.LogoUrl = newLogoUrl;
                        }
                        else
                        {
                            team.LogoUrl = $"{BaseUrl}{team.LogoUrl}";
                        }
                    }

                    teams.Add(team);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Error processing team node: {ex.Message}");
                }
            }

            if (teams.Any())
            {
                // заменяем старые только если удалось распарсить новые
                await teamsCollection.DeleteManyAsync(Builders<Team>.Filter.Empty);
                await teamsCollection.InsertManyAsync(teams);
                logger.LogInformation("Successfully imported {Count} teams", teams.Count);
            }
            else
            {
                return 0;
            }

            return teams.Count;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize teams database");
            throw;
        }
    }

    public async Task<List<StandingEntry>> ParseStandings()
    {
        try
        {
            logger.LogInformation("Parsing standings");

            var web = new HtmlWeb();
            var doc = await web.LoadFromWebAsync($"{BaseUrl}/superliga/men/polozhenie/");

            var table = doc.DocumentNode.SelectSingleNode("//table[contains(@class,'tourtable')]") ??
                        doc.DocumentNode.SelectSingleNode("//div[contains(@class,'tourtable')]//table");

            if (table == null)
            {
                logger.LogWarning("Standings table not found");
                return [];
            }

            // Определяем индексы нужных колонок по тексту заголовков
            var headers = table.SelectNodes(".//thead//th | .//tr[1]//th")
                ?.Select(th => th.InnerText.Trim())
                .ToList() ?? [];

            int idxPlayed  = FindColumnIndex(headers, "И");
            int idxWins    = FindColumnIndex(headers, "В");
            int idxLosses  = FindColumnIndex(headers, "П");
            int idxPoints  = FindColumnIndex(headers, "Очки");

            var rows = table.SelectNodes(".//tbody//tr") ?? table.SelectNodes(".//tr[position()>1]");
            if (rows == null) return [];

            var standings = new List<StandingEntry>();
            int autoPlace = 1;

            foreach (var row in rows)
            {
                try
                {
                    var cells = row.SelectNodes(".//td");
                    if (cells == null || cells.Count < 4) continue;

                    var placeText = cells[0].InnerText.Trim();
                    var place = int.TryParse(placeText, out var p) ? p : autoPlace;

                    // Название команды — второй td, берём текст без дочерних тегов (img и т.п.)
                    var teamName = cells[1].SelectSingleNode(".//a")?.InnerText.Trim()
                                   ?? cells[1].InnerText.Trim();
                    teamName = System.Net.WebUtility.HtmlDecode(teamName);

                    int played  = GetCellInt(cells, idxPlayed);
                    int wins    = GetCellInt(cells, idxWins);
                    int losses  = GetCellInt(cells, idxLosses);
                    int points  = GetCellInt(cells, idxPoints);

                    standings.Add(new StandingEntry(place, teamName, played, wins, losses, points));
                    autoPlace++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error parsing standings row");
                }
            }

            logger.LogInformation("Parsed {Count} standings entries", standings.Count);
            return standings;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse standings");
            throw;
        }
    }

    private static int FindColumnIndex(List<string> headers, string name)
    {
        var idx = headers.FindIndex(h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 ? idx : -1;
    }

    private static int GetCellInt(HtmlNodeCollection cells, int index)
    {
        if (index < 0 || index >= cells.Count) return 0;
        return int.TryParse(cells[index].InnerText.Trim(), out var v) ? v : 0;
    }

    public async Task<int> ParseMatches(bool updateAll = false)
    {
        try
        {
            var web = new HtmlWeb();
            var doc = await web.LoadFromWebAsync($"{BaseUrl}/superliga/men/games/");
            var matchNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'matches-table__item ')]");

            var matchesCollection = db.Matches;

            var matches = new List<Match>();

            foreach (var matchNode in matchNodes)
            {
                try
                {
                    var classText = matchNode.GetAttributeValue("class", "");
                    
                    var url = matchNode.SelectSingleNode(".//button[contains(text(), 'татистика')]")?.GetAttributeValue("onClick", "") ?? "";
                    if (string.IsNullOrEmpty(url))
                        url = matchNode.SelectSingleNode(".//a[contains(@class, 'match-opponent__preview')]")?.GetAttributeValue("href", "") ?? "";
                    var (matchUrl, matchId) = ParseUtils.ExtractGameUrlAndId(url);
                    var (homeTeamId, guestTeamId) = ParseUtils.ExtractTeamIds(classText);
                    var status = ParseUtils.ExtractGameStatus(classText);

                    var match = new Match
                    {
                        MatchId = matchId,
                        Date = ParseUtils.GameDateToUtc(matchNode.SelectSingleNode(".//div[contains(@class, 'matches-table__item-date')]/p")?.InnerText ?? "", TimeSpan.FromHours(12)),
                        Stage = matchNode.SelectSingleNode(".//div[contains(@class, 'matches-table__item-date')]/span")?.InnerText ?? "",
                        HomeTeamId = homeTeamId,
                        GuestTeamId = guestTeamId,
                        HomeScore = int.Parse(matchNode.SelectSingleNode("(.//div[contains(@class, 'match-opponent__team-score')]/span[contains(@class, 'match-opponent__result')])[1]")?.InnerText ?? "0"),
                        GuestScore = int.Parse(matchNode.SelectSingleNode("(.//div[contains(@class, 'match-opponent__team-score')]/span[contains(@class, 'match-opponent__result')])[2]")?.InnerText ?? "0"),
                        Status = status ?? MatchStatus.Plan,
                        Url = matchUrl,
                        Created = DateTime.UtcNow
                    };

                    if (!string.IsNullOrEmpty(match.Url))
                        match.Url = $"{BaseUrl}{match.Url}";

                    matches.Add(match);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing match node");
                }
            }

            if (matches.Any())
            {
                await EnsureTeamsUpToDate(matches);

                if (updateAll)
                {
                    await matchesCollection.DeleteManyAsync(Builders<Match>.Filter.Empty);
                    await matchesCollection.InsertManyAsync(matches);
                    logger.LogInformation("Successfully imported {Count} matches", matches.Count);
                }
                else
                {
                    var storedMatches = (await matchesCollection.FindAsync(match => true)).ToList();
                    foreach (var m in matches)
                    {
                        var sm = storedMatches.FirstOrDefault(x => x.MatchId == m.MatchId);
                        if (sm == null)
                        {
                            await matchesCollection.InsertOneAsync(m);
                        }
                        else
                        {
                            if (m.Status == sm.Status) continue;

                            sm.Status = m.Status;
                            sm.HomeScore = m.HomeScore;
                            sm.GuestScore = m.GuestScore;
                            sm.Date = m.Date;
                            sm.Stage = m.Stage;

                            await matchesCollection.ReplaceOneAsync(x => x.Id == sm.Id, sm, new ReplaceOptions { IsUpsert = true });
                        }
                    }
                }
            }
            else
            {
                return 0;
            }

            return matches.Count;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse matches");
            throw;
        }
    }

    // Если в матчах встречаются команды, которых нет в БД (например, начался новый сезон), обновляем список команд
    private async Task EnsureTeamsUpToDate(List<Match> matches)
    {
        try
        {
            var storedTeamIds = (await db.Teams.Find(Builders<Team>.Filter.Empty).Project(t => t.TeamId).ToListAsync()).ToHashSet();

            var unknownTeamIds = matches
                .SelectMany(m => new[] { m.HomeTeamId, m.GuestTeamId })
                .Where(id => id != 0 && !storedTeamIds.Contains(id))
                .Distinct()
                .ToList();

            if (!unknownTeamIds.Any()) return;

            logger.LogInformation("Found unknown teams {TeamIds}, updating teams", string.Join(", ", unknownTeamIds));
            await ParseTeams();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update teams");
        }
    }
}