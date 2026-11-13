using RussiaBasketBot.Models;
using MongoDB.Driver;
using RussiaBasketBot.ViewModels;

namespace RussiaBasketBot.Services;

public class BasketballService(ILogger<BasketballService> logger, MongoDbContext db, ParserService parserService)
{
    public async Task<List<MatchVm>> GetMatches(bool newestOrLatest, int limit = 8, DateOnly? date = null, bool refresh = true)
    {
        if (refresh) await parserService.ParseMatches(true);
        var teams = await db.Teams.Find(x => true).ToListAsync();

        IFindFluent<Match, Match> query;
        if (newestOrLatest)
        {
            query = db.Matches
                    .Find(x => x.Status == MatchStatus.Plan)
                    .SortBy(x => x.Date);
        } else
        {
            query = db.Matches
                .Find(x => x.Status == MatchStatus.Live || x.Status == MatchStatus.Finish)
                .SortByDescending(x => x.Date);
        }

        var matches = (await query/*.Limit(limit)*/.ToListAsync()).ToList();
        if (date != null)
            matches = matches.Where(x => DateOnly.FromDateTime(x.Date.UtcToMsc()) == date.Value).ToList();

        return matches.Take(limit).Select(m => MatchVm.FromMatch(m).FillTeams(teams)).ToList();
    }

    public async Task<List<MatchVm>> GetMatchesByTeam(int teamId)
    {
        await parserService.ParseMatches(true);
        var teams = await db.Teams.Find(x => true).ToListAsync();

        var matches = await db.Matches
            .Find(x => x.Status == MatchStatus.Plan &&
                       (x.HomeTeamId == teamId || x.GuestTeamId == teamId))
            .SortBy(x => x.Date)
            .ToListAsync();

        return matches.Select(m => MatchVm.FromMatch(m).FillTeams(teams)).ToList();
    }

    public async Task<List<Team>> GetTeams()
    {
        await parserService.ParseTeams();
        return await db.Teams.Find(x => true).ToListAsync();
    }
}
