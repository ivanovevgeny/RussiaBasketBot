using RussiaBasketBot.Models;

namespace RussiaBasketBot.ViewModels;

public class MatchVm
{
    public int MatchId { get; set; }
    public DateTime Date { get; set; }
    public DateTime DateMsc => Date.UtcToMsc();
    public int HomeTeamId { get; set; } = 0;
    public string HomeTeamName { get; set; } = string.Empty;
    public string HomeTeamLogo { get; set; } = string.Empty;
    public int GuestTeamId { get; set; } = 0;
    public string GuestTeamName { get; set; } = string.Empty;
    public string GuestTeamLogo { get; set; } = string.Empty;
    public int? HomeScore { get; set; }
    public int? GuestScore { get; set; }
    public MatchStatus Status { get; set; } = MatchStatus.Plan;
    public string Url { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty; // регулярный сезон / плей-офф

    public string Score => Status == MatchStatus.Finish ? $"{HomeScore}:{GuestScore}" : "";
    public string StatusText => Status == MatchStatus.Finish ? "Завершен" : Status == MatchStatus.Plan ? "Запланирован" : Status == MatchStatus.Live ? "В процессе" : "";
    public string UrlText => Status == MatchStatus.Finish ? "Статистика" : Status == MatchStatus.Plan ? "Превью" : Status == MatchStatus.Live ? "Трансляция" : "";

    public static MatchVm FromMatch(Match m)
    {
        return new MatchVm
        {
            MatchId = m.MatchId,
            Date = m.Date,
            Status = m.Status,
            Stage = m.Stage,
            HomeTeamId = m.HomeTeamId,
            GuestTeamId = m.GuestTeamId,
            HomeScore = m.HomeScore,
            GuestScore = m.GuestScore,
            Url = m.Url
        };
    }

    public MatchVm FillTeams(List<Team>? teams)
    {
        var homeTeam = teams?.FirstOrDefault(x => x.TeamId == HomeTeamId);
        if (homeTeam != null)
        {
            HomeTeamName = $"{homeTeam.Name} ({homeTeam.City})";
            HomeTeamLogo = homeTeam.LogoUrl;
        }
        var guestTeam = teams?.FirstOrDefault(x => x.TeamId == GuestTeamId);
        if (guestTeam != null)
        {
            GuestTeamName = $"{guestTeam.Name} ({guestTeam.City})";
            GuestTeamLogo = guestTeam.LogoUrl;
        }

        return this;
    }
}