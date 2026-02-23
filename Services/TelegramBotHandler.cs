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
                UpdateType.CallbackQuery => HandleCallbackQueryAsync(botClient, update.CallbackQuery!, cancellationToken),
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
            new BotCommand { Command = "standings", Description = "Положение команд" },
            new BotCommand { Command = "schedule", Description = "Расписание по команде" },
            new BotCommand { Command = "subscribe", Description = "Подписаться на обновления" },
            new BotCommand { Command = "unsubscribe", Description = "Отписаться от обновлений" },
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
            "/standings" => StandingsCommand(botClient, message, cancellationToken),
            "/schedule" => ScheduleCommand(botClient, message, cancellationToken),
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
                  "Я помогу тебе узнать результаты последних матчей, планируемые матчи, а также напомнить о них.\n\n" +
                  "Доступные команды:\n" +
                  "/newest - ближайшие матчи\n" +
                  "/latest - результаты последних матчей\n" +
                  "/standings - положение команд\n" +
                  "/schedule - расписание по команде\n" +
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

    private async Task StandingsCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        var text = await notifyService.GetStandingsMessage(cancellationToken);
        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: text,
            parseMode: ParseMode.Html,
            cancellationToken: cancellationToken);
    }

    private async Task ScheduleCommand(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        var teams = await basketballService.GetTeams();
        if (teams.Count == 0)
        {
            await botClient.SendMessage(chatId: message.Chat.Id, text: "Список команд пуст. Выполните /init для инициализации базы.", cancellationToken: cancellationToken);
            return;
        }

        // Кнопки по 2 в строке
        var buttons = teams
            .OrderBy(t => t.Name)
            .Select(t => InlineKeyboardButton.WithCallbackData($"{t.Name} ({t.City})", $"schedule_{t.TeamId}"))
            .Chunk(2)
            .Select(row => row.ToArray())
            .ToArray();

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: "Выберите команду:",
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: cancellationToken);
    }

    private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var data = callbackQuery.Data ?? "";

        if (data.StartsWith("schedule_") && int.TryParse(data["schedule_".Length..], out var teamId))
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);

            var teams = await basketballService.GetTeams();
            var team = teams.FirstOrDefault(t => t.TeamId == teamId);
            var teamName = team != null ? $"{team.Name} ({team.City})" : $"команда #{teamId}";

            var text = await notifyService.GetScheduleMessage(teamId, teamName, cancellationToken);
            await botClient.SendMessage(
                chatId: callbackQuery.Message!.Chat.Id,
                text: text,
                parseMode: ParseMode.Html,
                linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                cancellationToken: cancellationToken);
        }
        else
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
        }
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