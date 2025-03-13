using MongoDB.Driver;
using RussiaBasketBot.Models;

namespace RussiaBasketBot;

public class MongoDbContext
{
    private readonly IMongoDatabase _database;
    
    public MongoDbContext(string connectionString)
    {
        var connection = new MongoUrlBuilder(connectionString);
        var client = new MongoClient(connectionString);
        _database = client.GetDatabase(connection.DatabaseName);
    }
    public IMongoCollection<Team> Teams => _database.GetCollection<Team>("team");

    public IMongoCollection<Match> Matches => _database.GetCollection<Match>("matches");

    public IMongoCollection<TelegramGroup> TelegramGroups => _database.GetCollection<TelegramGroup>("telegram_groups");
}