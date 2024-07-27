using FluentAssertions;
using NSubstitute;
using Rebus.Retry;
using Rebus.Retry.Simple;
using StackExchange.Redis;
using Xunit;

namespace PFabIO.Rebus.Retry.ErrorTracking.Tests;

public class RedisErrorTrackerTests
{
    private readonly IConnectionMultiplexer _connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IExceptionInfoFactory _exceptionInfoFactory = Substitute.For<IExceptionInfoFactory>();
    private readonly IExceptionLogger _exceptionLogger = Substitute.For<IExceptionLogger>();

    [Fact]
    public async Task MarkAsFinal_NotExistingKey_ExpectedKeyCreation()
    {
        var retryStrategySettings = new RetryStrategySettings();

        var db = Substitute.For<IDatabase>();
        _connectionMultiplexer.GetDatabase().Returns(db);

        db.When(x => x.StringSetAsync(key: Arg.Any<RedisKey>(), value: Arg.Any<RedisValue>(),
            expiry: TimeSpan.FromMinutes(retryStrategySettings.ErrorTrackingMaxAgeMinutes), when: When.NotExists)).Do(
            callInfo =>
            {
                var redisKey = callInfo.Arg<RedisKey>();
                var redisValue = callInfo.Arg<RedisValue>();

                redisKey.Should().Be("rbserror:unitTestQueueName:myMessageId");

                redisValue.Should().Be("{\"Errors\":[],\"Final\":true,\"ErrorCount\":0}");
            });

        var tracker = GetDefault(retryStrategySettings);

        await tracker.MarkAsFinal("myMessageId");

        await db.Received(1).StringSetAsync(key: Arg.Any<RedisKey>(), value: Arg.Any<RedisValue>(),
            expiry: TimeSpan.FromMinutes(retryStrategySettings.ErrorTrackingMaxAgeMinutes), when: When.NotExists);
    }

    [Fact]
    public async Task MarkAsFinal_KeyExists_ExpectedKeyUpdated()
    {
        var retryStrategySettings = new RetryStrategySettings();

        var db = Substitute.For<IDatabase>();
        _connectionMultiplexer.GetDatabase().Returns(db);

        db.StringGetAsync("rbserror:unitTestQueueName:myMessageId").Returns(
            """{"Errors":[{"Type":null,"Message":null,"Details":null,"Time":"0001-01-01T00:00:00+00:00"}],"Final":false,"ErrorCount":1}""");

        db.When(x => x.StringSetAsync(key: Arg.Any<RedisKey>(), value: Arg.Any<RedisValue>(),
            expiry: TimeSpan.FromMinutes(retryStrategySettings.ErrorTrackingMaxAgeMinutes), when: When.Exists)).Do(
            callInfo =>
            {
                var redisKey = callInfo.Arg<RedisKey>();
                var redisValue = callInfo.Arg<RedisValue>();

                redisKey.Should().Be("rbserror:unitTestQueueName:myMessageId");

                redisValue.Should()
                    .Be(
                        """{"Errors":[{"Type":null,"Message":null,"Details":null,"Time":"0001-01-01T00:00:00+00:00"}],"Final":true,"ErrorCount":1}""");
            });

        var tracker = GetDefault(retryStrategySettings);

        await tracker.MarkAsFinal("myMessageId");

        await db.Received(1).StringSetAsync(key: Arg.Any<RedisKey>(), value: Arg.Any<RedisValue>(),
            expiry: TimeSpan.FromMinutes(retryStrategySettings.ErrorTrackingMaxAgeMinutes), when: When.Exists);
    }

    [Fact]
    public async Task RegisterError_NotExistingKey_ExpectedKeyCreation()
    {
        var retryStrategySettings = new RetryStrategySettings();

        var db = Substitute.For<IDatabase>();
        _connectionMultiplexer.GetDatabase().Returns(db);

        var exception = new Exception("MyEx");

        _exceptionInfoFactory.CreateInfo(exception)
            .Returns(new ExceptionInfo("MyExType", "MyExMessage", "MyExDetails", DateTimeOffset.MinValue));

        db.When(x => x.StringSetAsync(key: Arg.Any<RedisKey>(), value: Arg.Any<RedisValue>(),
            expiry: TimeSpan.FromMinutes(retryStrategySettings.ErrorTrackingMaxAgeMinutes), when: When.NotExists)).Do(
            callInfo =>
            {
                var redisKey = callInfo.Arg<RedisKey>();
                var redisValue = callInfo.Arg<RedisValue>();

                redisKey.Should().Be("rbserror:unitTestQueueName:myMessageId");

                redisValue.Should()
                    .Be(
                        """{"Errors":[{"Type":"MyExType","Message":"MyExMessage","Details":"MyExDetails","Time":"0001-01-01T00:00:00+00:00"}],"Final":false,"ErrorCount":1}""");
            });

        var tracker = GetDefault(retryStrategySettings);

        await tracker.RegisterError("myMessageId", exception);

        await db.Received(1).StringSetAsync(key: Arg.Any<RedisKey>(), value: Arg.Any<RedisValue>(),
            expiry: TimeSpan.FromMinutes(retryStrategySettings.ErrorTrackingMaxAgeMinutes), when: When.NotExists);
    }

    [Fact]
    public async Task RegisterError_KeyExists_ExpectedKeyUpdated()
    {
        var retryStrategySettings = new RetryStrategySettings();

        var db = Substitute.For<IDatabase>();
        _connectionMultiplexer.GetDatabase().Returns(db);

        var exception = new Exception("MyEx1");

        _exceptionInfoFactory.CreateInfo(exception)
            .Returns(new ExceptionInfo("MyExType2", "MyExMessage2", "MyExDetails2", DateTimeOffset.MinValue));

        db.StringGetAsync("rbserror:unitTestQueueName:myMessageId").Returns(
            """{"Errors":[{"Type":"MyExType1","Message":"MyExMessage1","Details":"MyExDetails1","Time":"0001-01-01T00:00:00+00:00"}],"Final":false,"ErrorCount":1}""");

        db.When(x => x.StringSetAsync(key: Arg.Any<RedisKey>(), value: Arg.Any<RedisValue>(),
            expiry: TimeSpan.FromMinutes(retryStrategySettings.ErrorTrackingMaxAgeMinutes), when: When.Exists)).Do(
            callInfo =>
            {
                var redisKey = callInfo.Arg<RedisKey>();
                var redisValue = callInfo.Arg<RedisValue>();

                redisKey.Should().Be("rbserror:unitTestQueueName:myMessageId");

                redisValue.Should()
                    .Be(
                        """{"Errors":[{"Type":"MyExType1","Message":"MyExMessage1","Details":"MyExDetails1","Time":"0001-01-01T00:00:00+00:00"},{"Type":"MyExType2","Message":"MyExMessage2","Details":"MyExDetails2","Time":"0001-01-01T00:00:00+00:00"}],"Final":false,"ErrorCount":2}""");
            });

        var tracker = GetDefault(retryStrategySettings);

        await tracker.RegisterError("myMessageId", exception);

        await db.Received(1).StringSetAsync(key: Arg.Any<RedisKey>(), value: Arg.Any<RedisValue>(),
            expiry: TimeSpan.FromMinutes(retryStrategySettings.ErrorTrackingMaxAgeMinutes), when: When.Exists);
    }
    
    [Fact]
    public async Task HasFailedTooManyTimes_NotExistingKey_ExpectedFalse()
    {
        var retryStrategySettings = new RetryStrategySettings();

        var tracker = GetDefault(retryStrategySettings);

        var hasFailedTooManyTimes = await tracker.HasFailedTooManyTimes("myMessageId");

        hasFailedTooManyTimes.Should().BeFalse();
    }
    
    [Fact]
    public async Task HasFailedTooManyTimes_KeyExistsNotFinal_ExpectedFalse()
    {
        var retryStrategySettings = new RetryStrategySettings();
        
        var db = Substitute.For<IDatabase>();
        _connectionMultiplexer.GetDatabase().Returns(db);
        db.StringGetAsync("rbserror:unitTestQueueName:myMessageId").Returns(
            """{"Errors":[{}],"Final":false,"ErrorCount":1}""");

        var tracker = GetDefault(retryStrategySettings);

        var hasFailedTooManyTimes = await tracker.HasFailedTooManyTimes("myMessageId");

        hasFailedTooManyTimes.Should().BeFalse();
    }
    
    [Fact]
    public async Task HasFailedTooManyTimes_KeyNotReachedDeliveryAttempts_ExpectedFalse()
    {
        var retryStrategySettings = new RetryStrategySettings(maxDeliveryAttempts:2);
        
        
        var db = Substitute.For<IDatabase>();
        _connectionMultiplexer.GetDatabase().Returns(db);
        db.StringGetAsync("rbserror:unitTestQueueName:myMessageId").Returns(
            """{"Errors":[{}],"Final":false,"ErrorCount":1}""");

        var tracker = GetDefault(retryStrategySettings);

        var hasFailedTooManyTimes = await tracker.HasFailedTooManyTimes("myMessageId");

        hasFailedTooManyTimes.Should().BeFalse();
    }
    
    [Fact]
    public async Task HasFailedTooManyTimes_KeyExistsIsFinal_ExpectedTrue()
    {
        var retryStrategySettings = new RetryStrategySettings();
        
        var db = Substitute.For<IDatabase>();
        _connectionMultiplexer.GetDatabase().Returns(db);
        db.StringGetAsync("rbserror:unitTestQueueName:myMessageId").Returns(
            """{"Errors":[{}],"Final":true,"ErrorCount":1}""");

        var tracker = GetDefault(retryStrategySettings);

        var hasFailedTooManyTimes = await tracker.HasFailedTooManyTimes("myMessageId");

        hasFailedTooManyTimes.Should().BeTrue();
    }
    
    [Fact]
    public async Task HasFailedTooManyTimes_KeyReachedDeliveryAttempts_ExpectedTrue()
    {
        var retryStrategySettings = new RetryStrategySettings(maxDeliveryAttempts:2);
        
        
        var db = Substitute.For<IDatabase>();
        _connectionMultiplexer.GetDatabase().Returns(db);
        db.StringGetAsync("rbserror:unitTestQueueName:myMessageId").Returns(
            """{"Errors":[{},{}],"Final":false,"ErrorCount":2}""");

        var tracker = GetDefault(retryStrategySettings);

        var hasFailedTooManyTimes = await tracker.HasFailedTooManyTimes("myMessageId");

        hasFailedTooManyTimes.Should().BeTrue();
    }
    
    
    
    private RedisErrorTracker GetDefault(RetryStrategySettings retryStrategySettings) =>
        new(retryStrategySettings: retryStrategySettings, exceptionLogger: _exceptionLogger,
            exceptionInfoFactory: _exceptionInfoFactory,
            connectionMultiplexer: _connectionMultiplexer, queueName: "unitTestQueueName");
}