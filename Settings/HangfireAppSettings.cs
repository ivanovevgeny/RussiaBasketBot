namespace RussiaBasketBot.Settings;

public class HangfireAppSettings
{
    public string ServerName { get; set; }
    public string DashboardUrl { get; set; }

    public List<HangfireQueueAppSettings> Queues { get; set; } = [];
}

public class HangfireQueueAppSettings
{
    public string QueueName { get; set; }
    public int WorkerCount { get; set; }
}