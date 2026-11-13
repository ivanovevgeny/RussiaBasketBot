using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RussiaBasketBot.Services;
using Serilog;

internal static class PackageCompatibilityChecks
{
    public static async Task CheckHost()
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"rbbot-package-check-{Guid.NewGuid():N}.log");
        try
        {
            using (var host = new HostBuilder()
                .ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Serilog:Using:0"] = "Serilog.Sinks.File",
                    ["Serilog:Using:1"] = "Serilog.Sinks.Console",
                    ["Serilog:MinimumLevel"] = "Information",
                    ["Serilog:WriteTo:0:Name"] = "File",
                    ["Serilog:WriteTo:0:Args:path"] = logPath,
                    ["Serilog:WriteTo:1:Name"] = "Console"
                }))
                .UseWindowsService()
                .UseSerilog((context, logger) => logger.ReadFrom.Configuration(context.Configuration))
                .ConfigureServices(services =>
                {
                    services.AddHttpClient("basketball");
                    services.AddSingleton<BasketballApiClient>();
                })
                .Build())
            {
                await host.StartAsync();
                _ = host.Services.GetRequiredService<BasketballApiClient>();
                host.Services.GetRequiredService<ILogger<BasketballApiClient>>().LogInformation("Package compatibility marker");
                await host.StopAsync();
            }

            if (!File.ReadAllText(logPath).Contains("Package compatibility marker"))
                throw new Exception("Updated Serilog configuration or file sink did not write the log");
            Console.WriteLine("PASS: Updated hosting, Windows Services registration, HTTP factory and Serilog configuration/sinks work together");
        }
        finally
        {
            File.Delete(logPath);
        }
    }
}
