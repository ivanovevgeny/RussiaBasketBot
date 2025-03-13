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

            var teamsCollection = db.Teams;

            // удаляем старые
            await teamsCollection.DeleteManyAsync(Builders<Team>.Filter.Empty);

            // вставляем новые
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
}