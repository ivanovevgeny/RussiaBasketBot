using Hangfire;
using Hangfire.Mongo;
using Hangfire.Mongo.Migration.Strategies;
using Hangfire.Mongo.Migration.Strategies.Backup;
using RussiaBasketBot.Settings;

namespace RussiaBasketBot;

public static class ServiceCollectionExtensions
{
    public static void AddHangfire(this IServiceCollection services, string connectionString)
    {
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseMongoStorage(connectionString, new MongoStorageOptions
            {
                MigrationOptions = new MongoMigrationOptions
                {
                    MigrationStrategy = new MigrateMongoMigrationStrategy(),
                    BackupStrategy = new CollectionMongoBackupStrategy()
                },
                Prefix = "hangfire.mongo",
                CheckConnection = true,
                CheckQueuedJobsStrategy = CheckQueuedJobsStrategy.TailNotificationsCollection
            })
        );

        foreach (var queue in AppSettings.Hangfire.Queues)
        {
            services.AddHangfireServer(serverOptions =>
            {
                serverOptions.ServerName = AppSettings.Hangfire.ServerName;
                serverOptions.WorkerCount = queue.WorkerCount;
                serverOptions.Queues = [queue.QueueName];
            });
        }
    }
}