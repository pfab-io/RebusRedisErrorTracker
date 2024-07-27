using System.Collections.Immutable;
using System.Text.Json;
using Rebus.Retry;
using Rebus.Retry.Simple;
using StackExchange.Redis;

namespace PFabIO.Rebus.Retry.ErrorTracking.Redis;

public class RedisErrorTracker(
    RetryStrategySettings retryStrategySettings,
    IExceptionLogger exceptionLogger,
    IExceptionInfoFactory exceptionInfoFactory,
    IConnectionMultiplexer connectionMultiplexer,
    string queueName,
    string redisErrorKeyPrefix = "rbserror")
    : IErrorTracker
{
    private readonly IDatabase _db = connectionMultiplexer.GetDatabase();

    public async Task MarkAsFinal(string messageId)
    {
        var key = GetRedisKey(messageId);
        var errorInfoRedisValue = await _db.StringGetAsync(key);
        if (errorInfoRedisValue.IsNull || !errorInfoRedisValue.HasValue)
        {
            var errorTracking = new ErrorTracking
            {
                Errors = ImmutableArray<ExceptionInfo>.Empty,
                Final = true
            };
            var trackingSerialized = JsonSerializer.Serialize(errorTracking);

            await _db.StringSetAsync(key: key, value: trackingSerialized,
                expiry: TimeSpan.FromMinutes(retryStrategySettings.ErrorTrackingMaxAgeMinutes), when: When.NotExists);
        }
        else
        {
            var errorTracking = JsonSerializer.Deserialize<ErrorTracking>(json: errorInfoRedisValue!)
                                ?? throw new InvalidOperationException();

            var trackingSerialized = JsonSerializer.Serialize(errorTracking.MarkAsFinal());

            await _db.StringSetAsync(key: key, value: trackingSerialized,
                expiry: TimeSpan.FromMinutes(retryStrategySettings.ErrorTrackingMaxAgeMinutes), when: When.Exists);
        }
    }

    public async Task RegisterError(string messageId, Exception exception)
    {
        ErrorTracking errorTracking;

        var key = GetRedisKey(messageId);
        var errorInfoRedisValue = await _db.StringGetAsync(key);

        var caughtException = exceptionInfoFactory.CreateInfo(exception);

        if (errorInfoRedisValue.IsNull || !errorInfoRedisValue.HasValue)
        {
            errorTracking = new ErrorTracking
            {
                Errors = ImmutableList<ExceptionInfo>.Empty.Add(caughtException),
                Final = false
            };
            var trackingSerialized = JsonSerializer.Serialize(errorTracking);

            await _db.StringSetAsync(key, trackingSerialized,
                expiry: TimeSpan.FromMinutes(retryStrategySettings.ErrorTrackingMaxAgeMinutes), when: When.NotExists);
        }
        else
        {
            errorTracking = JsonSerializer.Deserialize<ErrorTracking>(json: errorInfoRedisValue!)
                            ?? throw new InvalidOperationException();

            errorTracking = errorTracking.AddError(caughtException: caughtException,
                final: errorTracking.Final);

            var trackingSerialized = JsonSerializer.Serialize(errorTracking);

            await _db.StringSetAsync(key: key, value: trackingSerialized,
                expiry: TimeSpan.FromMinutes(retryStrategySettings.ErrorTrackingMaxAgeMinutes), when: When.Exists);
        }

        exceptionLogger.LogException(messageId: messageId, exception: exception, errorCount: errorTracking.ErrorCount,
            isFinal: errorTracking.Final);
    }

    private RedisKey GetRedisKey(string messageId) => $"{redisErrorKeyPrefix}:{queueName}:{messageId}";

    public async Task<bool> HasFailedTooManyTimes(string messageId)
    {
        var key = GetRedisKey(messageId);
        var errorInfoRedisValue = await _db.StringGetAsync(key);
        if (errorInfoRedisValue.IsNull || !errorInfoRedisValue.HasValue)
        {
            return false;
        }

        var existingTracking = JsonSerializer.Deserialize<ErrorTracking>(json: errorInfoRedisValue!);

        if (existingTracking == null)
            return false;

        var hasFailedTooManyTimes = existingTracking.Final
                                    || existingTracking.ErrorCount >= retryStrategySettings.MaxDeliveryAttempts;

        return hasFailedTooManyTimes;
    }

    public async Task<string?> GetFullErrorDescription(string messageId)
    {
        var key = GetRedisKey(messageId);
        var errorInfoRedisValue = await _db.StringGetAsync(key);
        if (errorInfoRedisValue.IsNull || !errorInfoRedisValue.HasValue)
        {
            return null;
        }

        var errorTracking = JsonSerializer.Deserialize<ErrorTracking>(json: errorInfoRedisValue!);

        if (errorTracking == null)
            return null;

        var fullExceptionInfo = string.Join(Environment.NewLine,
            errorTracking.Errors.Select(e => e.GetFullErrorDescription()));

        return $"{errorTracking.Errors.Count} unhandled exceptions: {fullExceptionInfo}";
    }

    public async Task<IReadOnlyList<ExceptionInfo>> GetExceptions(string messageId)
    {
        var key = GetRedisKey(messageId);
        var errorInfoRedisValue = await _db.StringGetAsync(key);
        if (errorInfoRedisValue.IsNull || !errorInfoRedisValue.HasValue)
        {
            return [];
        }

        var errorTracking = JsonSerializer.Deserialize<ErrorTracking>(json: errorInfoRedisValue!);

        if (errorTracking == null)
            return [];

        return [.. errorTracking.Errors];
    }

    public async Task CleanUp(string messageId)
    {
        var key = GetRedisKey(messageId);
        await _db.KeyDeleteAsync(key);
    }


    public class ErrorTracking
    {
        public required IImmutableList<ExceptionInfo> Errors { get; init; }
        public required bool Final { get; init; }
        public int ErrorCount => Errors.Count;


        public ErrorTracking AddError(ExceptionInfo caughtException, bool final) =>
            new()
            {
                Errors = Errors.Add(caughtException),
                Final = final
            };

        public ErrorTracking MarkAsFinal() => new()
        {
            Errors = Errors,
            Final = true
        };
    }
}