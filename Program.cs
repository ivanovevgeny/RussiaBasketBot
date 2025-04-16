using Hangfire;
using Microsoft.AspNetCore.Http.HttpResults;
using RussiaBasketBot;
using RussiaBasketBot.Infrastructure.Filters;
using RussiaBasketBot.Services;
using RussiaBasketBot.Settings;
using Serilog;
using RussiaBasketBot.Models;
using RussiaBasketBot.ViewModels;
using Telegram.Bot;
using System.Reflection;

var contentRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

var options = new WebApplicationOptions
{
    ContentRootPath = contentRoot,
    Args = args
};

var builder = WebApplication.CreateBuilder(options);

Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

// The initial bootstrap logger is able to log errors during start-up.
// It's fully replaced by the logger configured in `AddSerilog()`.
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting up the service");

    builder.Host
        .UseWindowsService(options => { options.ServiceName = "RussiaBasketBot"; });

    builder.Configuration
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

    //if (builder.Environment.IsDevelopment())
        builder.Configuration.AddJsonFile("appsettings.Personal.json", optional: true, reloadOnChange: true);

    builder.Configuration.GetSection("AppSettings").Get<AppSettings>();

    builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    var connectionString = builder.Configuration.GetConnectionString("MongoDb");

    builder.Services.AddSingleton(_ => new MongoDbContext(connectionString!));

    builder.Services.AddHangfire(connectionString!);

    builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(AppSettings.TelegramBotToken));

    builder.Services.AddHttpClient("tghttpClient").AddTypedClient<ITelegramBotClient>(
        httpClient => new TelegramBotClient(AppSettings.TelegramBotToken, httpClient));
    
    builder.Services.AddSingleton<BasketballService>();

    builder.Services.AddSingleton<ParserService>();

    builder.Services.AddSingleton<NotifyService>();

    builder.Services.AddSingleton<TelegramBotHandler>();

    builder.Services.AddHostedService<BackgroundWorker>();

    var app = builder.Build();

    app.UseHangfireDashboard(AppSettings.Hangfire.DashboardUrl, new DashboardOptions //https://docs.hangfire.io/en/latest/configuration/using-dashboard.html
    {
        Authorization = new[] { new HangfireAuthorizationFilter() },
        DisplayStorageConnectionString = false,
        IgnoreAntiforgeryToken = true
    });

    ConfigureHangfireJobs();

    ConfigureEndpoints(app);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "The service failed to start");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

return;

void ConfigureHangfireJobs()
{
    var mscTimeZone = DateExtensions.GetMscTimeZoneInfo();

    RecurringJob.AddOrUpdate<NotifyService>("MorningUpdate", service => service.ParseAndNotify(true, CancellationToken.None), "0 9 * * *", new RecurringJobOptions { TimeZone = mscTimeZone });

    RecurringJob.AddOrUpdate<NotifyService>("EveningUpdate", service => service.ParseAndNotify(false, CancellationToken.None), "0 21 * * *", new RecurringJobOptions { TimeZone = mscTimeZone });
}

void ConfigureEndpoints(WebApplication app)
{
    app.MapGet("/", () => Results.Ok(new { status = "ok" }));

    app.MapGet("/init",
        async Task<Results<Ok<string>, ProblemHttpResult>> (ParserService service, ILogger<Program> logger) =>
        {
            try
            {
                var teamsCount = await service.ParseTeams();
                var matchesCount = await service.ParseMatches(true);
                return TypedResults.Ok($"Database initialized successfully. Teams count: {teamsCount}, matches count: {matchesCount}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Database initialization failed");
                return TypedResults.Problem("Failed to initialize database");
            }
        });

    app.MapGet("/teams",
        async Task<Results<Ok<List<Team>>, NotFound>> (BasketballService service, ILogger<Program> logger) =>
        {
            try
            {
                var teams = await service.GetTeams();
                return teams.Any()
                    ? TypedResults.Ok(teams)
                    : TypedResults.NotFound();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "");
                return TypedResults.NotFound();
            }
        });

    app.MapGet("/newest",
        async Task<Results<Ok<List<MatchVm>>, NotFound>> (BasketballService service, ILogger<Program> logger) =>
        {
            try
            {
                var matches = await service.GetMatches(true);
                return matches.Any()
                    ? TypedResults.Ok(matches)
                    : TypedResults.NotFound();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "");
                return TypedResults.NotFound();
            }
        });

    app.MapGet("/latest",
        async Task<Results<Ok<List<MatchVm>>, NotFound>> (BasketballService service, ILogger<Program> logger) =>
        {
            try
            {
                var matches = await service.GetMatches(false);
                return matches.Any()
                    ? TypedResults.Ok(matches)
                    : TypedResults.NotFound();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "");
                return TypedResults.NotFound();
            }
        });
}