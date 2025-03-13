using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace RussiaBasketBot.Models;

public class TelegramGroup
{
    [BsonId]
    public ObjectId Id { get; set; }
    public long ChatId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public DateTime AddedDate { get; set; }
}