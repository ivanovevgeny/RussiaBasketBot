namespace RussiaBasketBot.Models;

internal sealed class StandingsTable
{
    public required List<StandingsTeam> Standings { get; init; }
}

internal sealed class StandingsTeam
{
    public required int Place { get; init; }
    public required string Name { get; init; }
    public string? RegionName { get; init; }
    public required int TotalGames { get; init; }
    public required int TotalWin { get; init; }
    public required int TotalDefeat { get; init; }
    public required int TotalPoints { get; init; }
}
