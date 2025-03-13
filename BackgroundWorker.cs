using RussiaBasketBot.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;

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

            var receiverOptions = new ReceiverOptions{ DropPendingUpdates = true, AllowedUpdates = [] };

            await botClient.ReceiveAsync(updateHandler, receiverOptions, stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error starting bot");
            throw;
        }
    }
}