using MongoDB.Driver;
using RussiaBasketBot.Models;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace RussiaBasketBot.Services;

public class NotifyService(ILogger<NotifyService> logger, MongoDbContext db, ParserService parserService, BasketballService basketballService, ITelegramBotClient botClient)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(3);
    private readonly Dictionary<string, (string Text, DateTime Expiry)> _cache = new();

    private bool TryGetCache(string key, out string text)
    {
        if (_cache.TryGetValue(key, out var entry) && DateTime.UtcNow < entry.Expiry)
        {
            text = entry.Text;
            return true;
        }
        text = string.Empty;
        return false;
    }

    private void SetCache(string key, string text) =>
        _cache[key] = (text, DateTime.UtcNow.Add(CacheTtl));

    public async Task<string> GetStandingsMessage(CancellationToken cancellationToken)
    {
        const string cacheKey = "standings";
        if (TryGetCache(cacheKey, out var cached)) return cached;

        var standings = await parserService.ParseStandings();
        if (standings.Count == 0) return "Таблица не найдена";

        const int nameWidth = 18;
        var sb = new StringBuilder();
        sb.AppendLine("🏆 <b>Положение команд</b>");
        sb.AppendLine("<pre>");
        sb.AppendLine($"{"#",2} {"Команда",-nameWidth} {"В",2} {"П",2} {"Оч",2}");
        sb.AppendLine(new string('─', 2 + 1 + nameWidth + 1 + 2 + 1 + 2 + 1 + 2));

        foreach (var s in standings)
        {
            var name = ShortenTeamName(s.TeamName, nameWidth);
            sb.AppendLine($"{s.Place,2} {name,-nameWidth} {s.Wins,2} {s.Losses,2} {s.Points,2}");
        }

        sb.Append("</pre>");

        var message = sb.ToString();
        SetCache(cacheKey, message);
        return message;
    }

    public async Task<string> GetScheduleMessage(int teamId, string teamName, CancellationToken cancellationToken)
    {
        var cacheKey = $"schedule_{teamId}";
        if (TryGetCache(cacheKey, out var cached)) return cached;

        var matches = await basketballService.GetMatchesByTeam(teamId);

        if (matches.Count == 0)
        {
            var noMatches = $"Запланированных матчей для команды <b>{teamName}</b> не найдено";
            SetCache(cacheKey, noMatches);
            return noMatches;
        }

        var sb = new StringBuilder($"📅 Расписание — <b>{teamName}</b>:\n\n");
        foreach (var match in matches)
        {
            sb.AppendLine($"🏀 {match.HomeTeamName} vs {match.GuestTeamName}");
            sb.AppendLine($"Дата: {match.DateMsc:dd.MM.yyyy HH:mm (мск)}");
            sb.AppendLine($"<a href='{match.Url}'>{match.UrlText}</a>");
            sb.AppendLine();
        }

        var message = sb.ToString();
        SetCache(cacheKey, message);
        return message;
    }

    public async Task ParseAndNotify(bool newestOrLatest, CancellationToken token)
    {
        try
        {
            logger.LogInformation("Starting match parsing and notification process");
            await parserService.ParseMatches(true);
            await NotifyTelegramGroups(newestOrLatest, token, date: DateOnly.FromDateTime(DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse and notify about matches");
            throw;
        }
    }

    public async Task NotifyTelegramGroups(bool newestOrLatest, CancellationToken cancellationToken, long? chatId = null, DateOnly? date = null)
    {
        try
        {
            var chatIdList = chatId == null ? 
                await db.TelegramGroups.Find(Builders<TelegramGroup>.Filter.Empty).Project(g => g.ChatId).ToListAsync(cancellationToken) :
                [chatId.Value];

            if (!chatIdList.Any()) return;

            var matches = await basketballService.GetMatches(newestOrLatest, date: date);

            // если ищем последние матчи и есть те, что в Процессе, то парсим заново
            if (!newestOrLatest && matches.Any(x => x.Status is MatchStatus.Live or MatchStatus.Plan))
            {
                await parserService.ParseMatches(true);
                matches = await basketballService.GetMatches(newestOrLatest, date: date);
            }

            if (chatId != null && !matches.Any())
            {
                await botClient.SendMessage(chatId: chatId, text: "Матчи не найдены", cancellationToken: cancellationToken);
                return;
            }

            if (!matches.Any()) return;

            var messageText = new StringBuilder($"{(newestOrLatest ? "Ближайшие" : "Недавние")} матчи:\n\n");
            foreach (var match in matches)
            {
                messageText.AppendLine($"🏀 {match.HomeTeamName} vs {match.GuestTeamName}");
                if (!newestOrLatest)
                {
                    messageText.AppendLine($"Счет: <b>{match.Score}</b>");
                    messageText.AppendLine($"Статус: {match.StatusText}");
                }

                messageText.AppendLine($"Дата: {match.DateMsc:dd.MM.yyyy HH:mm (мск)}");
                messageText.AppendLine($"<a href='{match.Url}'>{match.UrlText}</a>");
                messageText.AppendLine();
            }

            var text = messageText.ToString();

            foreach (var id in chatIdList)
            {
                try
                {
                    await botClient.SendMessage(chatId: id, text: text, parseMode: ParseMode.Html, linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true }, cancellationToken: cancellationToken);
                    await Task.Delay(100, cancellationToken); // Rate limiting
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Failed to send notifications to chat {id}");
                }
            }
        }
        catch (Exception ex)
        {
            if (chatId != null)
            {
                await botClient.SendMessage(chatId: chatId, text: "❌ Ошибка, попробуйте позже", cancellationToken: cancellationToken);
            }
        }
    }

    public async Task Subscribe(Message message, CancellationToken cancellationToken)
    {
        try
        {
            var coll = db.TelegramGroups;

            var group = new TelegramGroup
            {
                ChatId = message.Chat.Id,
                GroupName = message.Chat.Title ?? message.Chat.Username ?? message.Chat.Id.ToString(),
                AddedDate = DateTime.UtcNow
            };

            await coll.ReplaceOneAsync(
                Builders<TelegramGroup>.Filter.Eq(g => g.ChatId, group.ChatId),
                group,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);

            await botClient.SendMessage(chatId: message.Chat.Id, text: "✅ Теперь вы будете получать уведомления о ближайших и сыгранных матчах", cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to subscribe chat {ChatId}", message.Chat.Id);
            await botClient.SendMessage(chatId: message.Chat.Id, text: "❌ Ошибка. Пожалуйста, попробуйте позже.", cancellationToken: cancellationToken);
        }
    }

    // "Динамо (Приморский край)" → "Динамо (Примор.)"
    private static string ShortenTeamName(string name, int maxWidth)
    {
        if (name.Length <= maxWidth) return name;

        var parenIdx = name.IndexOf('(');
        if (parenIdx < 0) return name[..(maxWidth - 1)] + "…";

        var prefix = name[..parenIdx];                         // "Динамо "
        var city = name[(parenIdx + 1)..].TrimEnd(')').Trim(); // "Приморский край"

        // символов доступно внутри скобок
        var available = maxWidth - prefix.Length - 2; // -2 для "(" и ")"
        if (available <= 1) return (prefix.TrimEnd() + "…")[..maxWidth];

        var firstWord = city.Split(' ')[0];
        var shortened = firstWord.Length <= available - 1
            ? firstWord + "."
            : firstWord[..(available - 1)] + ".";

        return $"{prefix}({shortened})";
    }

    public async Task Unsubscribe(Message message, CancellationToken cancellationToken)
    {
        try
        {
            var coll = db.TelegramGroups;

            var result = await coll.DeleteOneAsync(
                Builders<TelegramGroup>.Filter.Eq(g => g.ChatId, message.Chat.Id),
                cancellationToken);

            var responseMessage = result.DeletedCount > 0
                ? "✅ Вы успешно отписались от обновлений"
                : "ℹ️ Вы не подписаны на обновления";

            await botClient.SendMessage(chatId: message.Chat.Id, text: responseMessage, cancellationToken: cancellationToken);

        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to unsubscribe chat {ChatId}", message.Chat.Id);
            await botClient.SendMessage(chatId: message.Chat.Id, text: "❌ Ошибка. Пожалуйста, попробуйте позже.", cancellationToken: cancellationToken);
        }
    }
}