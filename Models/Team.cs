using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace RussiaBasketBot.Models;

public class Team
{
    [BsonId]
    public ObjectId Id { get; set; }
    public int TeamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public DateTime Created { get; set; }
}