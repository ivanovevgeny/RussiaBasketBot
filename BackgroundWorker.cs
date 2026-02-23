using RussiaBasketBot.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace RussiaBasketBot;

public class BackgroundWorker(ILogger<BackgroundWorker> logger, MongoDbContext db, ParserService parserService, ITelegramBotClient botClient, TelegramBotHandler updateHandler) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await updateHandler.RegisterBotMenu(botClient, stoppingToken);

            var me = await botClient.GetMe(stoppingToken);
            logger.LogInformation("Start receiving updates for {BotName}", me.Username ?? "My Awesome Bot");

            var receiverOptions = new ReceiverOptions
            {
                DropPendingUpdates = true,
                AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
            };

            await botClient.ReceiveAsync(updateHandler, receiverOptions, stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error starting bot");
            throw;
        }
    }
}