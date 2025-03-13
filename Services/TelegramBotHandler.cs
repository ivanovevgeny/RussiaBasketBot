using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Types;
using Telegram.Bot;

namespace RussiaBasketBot.Services;

public class TelegramBotHandler(ILogger<TelegramBotHandler> logger, BasketballService basketballService, NotifyService notifyService) : IUpdateHandler
{

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            var handler = update.Type switch
            {
                UpdateType.Message => HandleMessageAsync(botClient, update.Message!, cancellationToken),
                _ => UnknownUpdateHandlerAsync(botClient, update, cancellationToken)
            };

            await handler;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling update {UpdateId}", update.Id);
            throw;
        }
    }

    public async Task RegisterBotMenu(ITelegramBotClient botClient, CancellationToken stoppingToken)
    {
        await botClient.SetMyCommands(new[]
        {
            new BotCommand { Command = "start", Description = "Запуск бота" },
            new BotCommand { Command = "newest", Description = "Ближайшие матчи" },
            new BotCommand { Command = "latest", Description = "Последние матчи" },
            new BotCommand { Command = "subscribe", Description = "Подписаться на обновления" },
            new BotCommand { Command = "unsubscribe", Description = "Отписаться от обновлений" },
            //new BotCommand { Command = "settings", Description = "Настройки" }
        }, cancellationToken: stoppingToken);
    }
    private async Task HandleMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        if (message.Type != MessageType.Text)
        {
            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: "Sorry, I don't accept media files. Please send text commands only.",
                cancellationToken: cancellationToken
            );
            return;
        }

        if (message.Text is not { } messageText)
            return;

        var action = messageText.Split(' ')[0].ToLower() switch
        {
            "/start" => StartCommand(botClient, message, cancellationToken),
            "/newest" => GetMatchesCommand(true, botClient, message, cancellationToken),
            "/latest" => GetMatchesCommand(false, botClient, message, cancellationToken),
            "/subscribe" => SubscribeCommand(botClient, message, cancellationToken),
            "/unsubscribe" => UnsubscribeCommand(botClient, message, cancellationToken),
            _ => HandleUnknownCommand(botClient, message, cancellationToken)
        };

        await action;
    }
    private static async Task StartCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        /*var keyboard = new InlineKeyboardMarkup(new[]
        {
            new [] { InlineKeyboardButton.WithCallbackData("Subscribe to Updates", "confirm_subscribe"), }
        });*/

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: "Добро пожаловать! 🏀\n" +
                  "Я помогу тебе узнать результаты последних матчей, планируемые мачти, а также напомнить о них.\n\n" +
                  "Доступные команды:\n" +
                  "/newest - посмотреть ближайшие матчи\n" +
                  "/latest - посмотреть результаты последних матчей\n" +
                  "/subscribe - подписаться на обновления\n" +
                  "/unsubscribe - отписаться от обновлений",
            //+ "\n/help - Show this help message",
            //replyMarkup: keyboard,
            replyMarkup: new ReplyKeyboardRemove(),
            cancellationToken: cancellationToken);
    }
    
    private async Task GetMatchesCommand(bool newestOrLatest, ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        await notifyService.NotifyTelegramGroups(newestOrLatest, cancellationToken, message.Chat.Id);
    }

    public async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
    {
        logger.LogInformation("HandleError: {Exception}", exception);
        // Cooldown in case of network connection error
        if (exception is RequestException)
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }

    private async Task SubscribeCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        await notifyService.Subscribe( message, cancellationToken);
    }

    private async Task UnsubscribeCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        await notifyService.Unsubscribe(message, cancellationToken);
    }

    private static async Task HandleUnknownCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        await botClient.SendMessage(chatId: message.Chat.Id, text: "Неизвестная команда. Выполните /help чтобы посмотреть список доступных команд", cancellationToken: cancellationToken);
    }

    private async Task UnknownUpdateHandlerAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        logger.LogInformation("Unknown update type: {UpdateType}", update.Type);
        await Task.CompletedTask;
    }
}