using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PFabIO.Rebus.Retry.ErrorTracking.Redis.Extensions;
using Rebus.Config;
using Rebus.RabbitMq;
using Rebus.Retry.Simple;
using StackExchange.Redis;
using TestConsole.HostedServices;

Console.WriteLine(value: "Hello, World!");

var hostBuilder = Host.CreateDefaultBuilder()
    //.ConfigureAppConfiguration(x => { x.AddJsonFile(path: "appSettings.test.json"); })
    .ConfigureServices((context, serviceCollection) =>
    {
        const string inputQueueName = "RedisErrorTrackerTestConsoleQueue";
        const string errorQueueName = "RedisErrorTrackerTestConsoleErrorQueue";

        var connectionMultiplexer = ConnectionMultiplexer.Connect("localhost:6379");


        serviceCollection.AddRebus((configurer, _)
            => configurer.Transport(x => x
                    .UseRabbitMq(connectionString: "amqps://localhost",
                        inputQueueName: inputQueueName)
                    .Ssl(new SslSettings(enabled: false, serverName: null)))
                .Options(x =>
                {
                    x.DecorateRedisErrorTracker(connectionMultiplexer, queueName: inputQueueName);

                    x.SetNumberOfWorkers(1);
                    x.SetMaxParallelism(1);

                    x.RetryStrategy(secondLevelRetriesEnabled: true, maxDeliveryAttempts: 5,
                        errorQueueName: errorQueueName);
                }), onCreated: async bus => { await bus.Subscribe<ErrorTrackerTestHostedService.MyEvent>(); });

        serviceCollection.AddRebusHandler<ErrorTrackerTestHostedService.MyEventHandler>();

        serviceCollection.AddHostedService<ErrorTrackerTestHostedService>();
    });

using var build = hostBuilder.Build();

await build.RunAsync();

namespace TestConsole
{
    public class Marker
    {
    }
}