using MongoDB.Driver;
using RussiaBasketBot.Models;

namespace RussiaBasketBot.Services;

public class ParserService(ILogger<ParserService> logger, MongoDbContext db, BasketballApiClient api)
{
    public async Task<int> ParseTeams()
    {
        try
        {
            logger.LogInformation("Starting team database initialization");
            var teams = await api.GetTeams();
            if (teams.Count == 0)
            {
                logger.LogWarning("Teams not found");
                return 0;
            }

            var updates = teams.Select(team => new UpdateOneModel<Team>(
                Builders<Team>.Filter.Eq(t => t.TeamId, team.TeamId),
                Builders<Team>.Update
                    .Set(t => t.Name, team.Name)
                    .Set(t => t.City, team.City)
                    .Set(t => t.Url, team.Url)
                    .Set(t => t.LogoUrl, team.LogoUrl)
                    .SetOnInsert(t => t.Created, team.Created)) { IsUpsert = true });
            await db.Teams.BulkWriteAsync(updates);
            await db.Teams.DeleteManyAsync(Builders<Team>.Filter.Nin(t => t.TeamId, teams.Select(t => t.TeamId)));
            logger.LogInformation("Successfully imported {Count} teams", teams.Count);
            return teams.Count;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize teams database");
            throw;
        }
    }

    public async Task<List<StandingEntry>> ParseStandings(CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Parsing standings");
            var standings = await api.GetStandings(cancellationToken);
            logger.LogInformation("Parsed {Count} standings entries", standings.Count);
            return standings;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse standings");
            throw;
        }
    }

    public async Task<int> ParseMatches(bool updateAll = false)
    {
        try
        {
            var matches = await api.GetMatches();
            if (matches.Count == 0)
            {
                logger.LogWarning("Matches not found");
                return 0;
            }

            await EnsureTeamsUpToDate(matches);
            // Обновляем счёт и переносы дат, даже если статус матча не изменился.
            var updates = matches.Select(match => new UpdateOneModel<Match>(
                Builders<Match>.Filter.Eq(m => m.MatchId, match.MatchId),
                Builders<Match>.Update
                    .Set(m => m.Date, match.Date)
                    .Set(m => m.HomeTeamId, match.HomeTeamId)
                    .Set(m => m.GuestTeamId, match.GuestTeamId)
                    .Set(m => m.HomeScore, match.HomeScore)
                    .Set(m => m.GuestScore, match.GuestScore)
                    .Set(m => m.Status, match.Status)
                    .Set(m => m.Url, match.Url)
                    .Set(m => m.Stage, match.Stage)
                    .SetOnInsert(m => m.Created, match.Created)) { IsUpsert = true });
            await db.Matches.BulkWriteAsync(updates);

            if (updateAll)
                await db.Matches.DeleteManyAsync(Builders<Match>.Filter.Nin(m => m.MatchId, matches.Select(m => m.MatchId)));

            logger.LogInformation("Successfully imported {Count} matches", matches.Count);
            return matches.Count;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse matches");
            throw;
        }
    }

    private async Task EnsureTeamsUpToDate(List<Match> matches)
    {
        var teams = await db.Teams.Find(Builders<Team>.Filter.Empty).ToListAsync();
        var storedTeamIds = teams.Select(t => t.TeamId).ToHashSet();
        var hasUnknownTeams = matches.SelectMany(m => new[] { m.HomeTeamId, m.GuestTeamId })
            .Any(id => !storedTeamIds.Contains(id));
        var hasOldUrls = teams.Any(team => !team.Url.StartsWith("https://russiabasket.ru/team/", StringComparison.Ordinal));
        if (!hasUnknownTeams && !hasOldUrls) return;

        logger.LogInformation("Refreshing teams for the current calendar");
        await ParseTeams();
    }
}
