using Rebus.Config;
using Rebus.Retry;
using Rebus.Retry.Simple;
using StackExchange.Redis;

namespace PFabIO.Rebus.Retry.ErrorTracking.Redis.Extensions;

public static class OptionsConfigurerExtensions
{
     public static void DecorateRedisErrorTracker(this OptionsConfigurer optionsConfigurer, IConnectionMultiplexer connectionMultiplexer, string queueName)
     {
          optionsConfigurer.Decorate<IErrorTracker>(c =>
          {
               var retryStrategySettings = c.Get<RetryStrategySettings>();
               var exceptionLogger = c.Get<IExceptionLogger>();
               var exceptionInfoFactory = c.Get<IExceptionInfoFactory>();
               return new RedisErrorTracker(retryStrategySettings: retryStrategySettings,
                    exceptionLogger: exceptionLogger,
                    exceptionInfoFactory: exceptionInfoFactory, connectionMultiplexer: connectionMultiplexer,
                    queueName: queueName ?? throw new InvalidOperationException("QueueName not set"));
          });
     }
}