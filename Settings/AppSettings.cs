namespace RussiaBasketBot.Settings;

public class AppSettings
{
    public static string TelegramBotToken { get; set; } = "";

    public static HangfireAppSettings Hangfire { get; set; } = new();
}