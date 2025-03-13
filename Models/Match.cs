using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace RussiaBasketBot.Models;

public class Match
{
    [BsonId]
    public ObjectId Id { get; set; }
    public int MatchId { get; set; }
    public DateTime Date { get; set; }
    public int HomeTeamId { get; set; }
    public int GuestTeamId { get; set; }
    public int? HomeScore { get; set; }
    public int? GuestScore { get; set; }
    public MatchStatus Status { get; set; } = MatchStatus.Plan;
    public string Url { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty; // регул€рный сезон / плей-офф
    public DateTime Created { get; set; }
}

public enum MatchStatus
{
    Plan = 0, Live = 1, Finish = 2
}