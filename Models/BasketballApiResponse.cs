namespace RussiaBasketBot.Models;

internal sealed class ApiListResponse<T>
{
    public required string Status { get; init; }
    public string? Message { get; init; }
    public required int TotalCount { get; init; }
    public required List<T> Items { get; init; }
}

internal sealed class LeaguePageResponse
{
    public required string Status { get; init; }
    public string? Message { get; init; }
    public required LeaguePage Result { get; init; }
}

internal sealed class LeaguePage
{
    public required LeagueSeason League { get; init; }
}

internal sealed class LeagueSeason
{
    public required int Season { get; init; }
}

internal sealed class ApiRoster
{
    public required ApiTeam Team { get; init; }
}

internal sealed class ApiTeam
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string? RegionName { get; init; }
    public string? Logo { get; init; }
}

internal sealed class ApiCalendarEntry
{
    public required ApiGame Game { get; init; }
    public required ApiCompetition Comp { get; init; }
    public required ApiCalendarTeam Team1 { get; init; }
    public required ApiCalendarTeam Team2 { get; init; }
}

internal sealed class ApiCompetition
{
    public required string Name { get; init; }
}

internal sealed class ApiCalendarTeam
{
    public required int TeamId { get; init; }
}

internal sealed class ApiGame
{
    public required int Id { get; init; }
    public required string GameStatus { get; init; }
    public DateTimeOffset? ScheduledTime { get; init; }
    public required bool HasTime { get; init; }
    public required string LocalDate { get; init; }
    public int? Score1 { get; init; }
    public int? Score2 { get; init; }
}
