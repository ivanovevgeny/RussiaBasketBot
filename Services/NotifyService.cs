using MongoDB.Driver;
using RussiaBasketBot.Models;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace RussiaBasketBot.Services;

public class NotifyService(ILogger<NotifyService> logger, MongoDbContext db, ParserService parserService, BasketballService basketballService, ITelegramBotClient botClient)
{
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