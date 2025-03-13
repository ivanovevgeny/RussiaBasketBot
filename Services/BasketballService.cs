using System.Linq.Expressions;
using RussiaBasketBot.Models;
using MongoDB.Driver;
using RussiaBasketBot.ViewModels;

namespace RussiaBasketBot.Services;

public class BasketballService(ILogger<BasketballService> logger, MongoDbContext db)
{
    public async Task<List<MatchVm>> GetMatches(bool newestOrLatest, int limit = 8)
    {
        var teams = await db.Teams.Find(x => true).ToListAsync();

        Expression<Func<Match, bool>> filterExpr = x => newestOrLatest ? x.Status == MatchStatus.Plan : x.Status == MatchStatus.Live || x.Status == MatchStatus.Finish;

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

        var matches = (await query.Limit(limit).ToListAsync()).OrderBy(m => m.Date);

        return matches.Select(m => MatchVm.FromMatch(m).FillTeams(teams)).ToList();
    }

    public async Task<List<Team>> GetTeams()
    {
        return await db.Teams.Find(x => true).ToListAsync();
    }
}