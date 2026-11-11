using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Altcha;
using Altcha.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Smes.Api.Infrastructure.Auth;
using Smes.Api.Controllers;
using StackExchange.Redis;
using Xunit;

namespace Smes.Api.Tests;

public sealed class AltchaSecurityTests
{
    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    public void RefreshCookieIsSecureInProductionButLocalHttpDevelopmentRemainsUsable(
        string environmentName,
        bool expectSecure)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(environmentName);
        var controller = new AuthController(null!, null!, null!, null!, environment.Object, null!);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        var setCookie = typeof(AuthController).GetMethod(
            "SetRefreshCookie", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Refresh cookie method not found.");

        setCookie.Invoke(controller, ["test-refresh-token", DateTime.UtcNow.AddMinutes(5)]);

        var cookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        Assert.Equal(expectSecure, cookieHeader.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("httponly", cookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoginRequiresFailClosedCaptchaAndBoundsPayloadSize()
    {
        var action = typeof(AuthController).GetMethod(nameof(AuthController.Login));
        Assert.NotNull(action);
        var verification = action.GetCustomAttribute<AltchaVerifyAttribute>();
        Assert.NotNull(verification);
        Assert.True(verification.RejectOnFailure);
        Assert.Null(action.GetCustomAttribute<EnableRateLimitingAttribute>());
        Assert.NotNull(action.GetCustomAttribute<RequestSizeLimitAttribute>());

        var payloadLimit = typeof(AuthController.LoginRequest)
            .GetProperty(nameof(AuthController.LoginRequest.AltchaPayload))
            ?.GetCustomAttribute<StringLengthAttribute>();
        Assert.Equal(8192, payloadLimit?.MaximumLength);
    }

    [Fact]
    public async Task LoginAttemptLimitUsesTenantAndUsernameWithAtomicRedisWindow()
    {
        var redisKeys = new List<string>();
        var database = new Mock<IDatabase>();
        database.Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                CommandFlags.None))
            .Returns((string script, RedisKey[] keys, RedisValue[] values, CommandFlags _) =>
            {
                Assert.Contains("INCR", script, StringComparison.Ordinal);
                Assert.Contains("PEXPIRE", script, StringComparison.Ordinal);
                Assert.Equal(300_000L, (long)values[0]);
                redisKeys.Add(keys[0].ToString());
                return Task.FromResult(RedisResult.Create((RedisValue)10L));
            });
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);
        var limiter = new RedisLoginAttemptRateLimiter(
            redis.Object, NullLogger<RedisLoginAttemptRateLimiter>.Instance);

        var firstAccount = await limiter.CheckAndRecordAsync("machinery", "operator");
        var sameAccount = await limiter.CheckAndRecordAsync("MACHINERY", " OPERATOR ");
        var otherAccount = await limiter.CheckAndRecordAsync("electronics", "operator");

        Assert.Equal(LoginAttemptRateLimitResult.Allowed, firstAccount);
        Assert.Equal(LoginAttemptRateLimitResult.Allowed, sameAccount);
        Assert.Equal(LoginAttemptRateLimitResult.Allowed, otherAccount);
        Assert.Equal(redisKeys[0], redisKeys[1]);
        Assert.NotEqual(redisKeys[0], redisKeys[2]);
        database.Verify(db => db.ScriptEvaluateAsync(
            It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), CommandFlags.None),
            Times.Exactly(3));
    }

    [Fact]
    public async Task LoginAttemptLimitThrottlesOverLimitAndFailsClosedOnRedisFailure()
    {
        var throttledDatabase = new Mock<IDatabase>();
        throttledDatabase.Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), CommandFlags.None))
            .ReturnsAsync(RedisResult.Create((RedisValue)11L));
        var throttledRedis = new Mock<IConnectionMultiplexer>();
        throttledRedis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(throttledDatabase.Object);
        var throttledLimiter = new RedisLoginAttemptRateLimiter(
            throttledRedis.Object, NullLogger<RedisLoginAttemptRateLimiter>.Instance);

        var throttled = await throttledLimiter.CheckAndRecordAsync("machinery", "operator");

        var unavailableDatabase = new Mock<IDatabase>();
        unavailableDatabase.Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), CommandFlags.None))
            .Returns(Task.FromException<RedisResult>(new RedisConnectionException(
                ConnectionFailureType.SocketFailure, "simulated Redis outage")));
        var unavailableRedis = new Mock<IConnectionMultiplexer>();
        unavailableRedis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(unavailableDatabase.Object);
        var unavailableLimiter = new RedisLoginAttemptRateLimiter(
            unavailableRedis.Object, NullLogger<RedisLoginAttemptRateLimiter>.Instance);

        var unavailable = await unavailableLimiter.CheckAndRecordAsync("machinery", "operator");

        Assert.Equal(LoginAttemptRateLimitResult.Throttled, throttled);
        Assert.Equal(LoginAttemptRateLimitResult.Unavailable, unavailable);
    }

    [Fact]
    public void SignedChallengeRejectsForgedCounterThatDoesNotMatchKeyPrefix()
    {
        const string signatureSecret = "unit-test-only-signature-secret-1234567890";
        var challenge = AltchaPow.CreateChallenge(new CreateChallengeOptions
        {
            Algorithm = "PBKDF2/SHA-256",
            Cost = 10,
            Counter = 10,
            KeyPrefixLength = 16,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(3),
            HmacSignatureSecret = signatureSecret
        });
        var salt = Convert.FromHexString(challenge.Parameters.Salt);
        var nonce = Convert.FromHexString(challenge.Parameters.Nonce);
        var cryptoType = typeof(AltchaPow).Assembly.GetType("Altcha.AltchaCrypto")
            ?? throw new InvalidOperationException("ALTCHA crypto helper not found.");
        var passwordWithCounter = cryptoType.GetMethod(
            "PasswordWithCounter", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ALTCHA counter password helper not found.");
        var counterPassword = (byte[])(passwordWithCounter.Invoke(null, [nonce, 0])
            ?? throw new InvalidOperationException("ALTCHA counter password was not produced."));
        var forgedKey = KeyDerivation.Resolve(challenge.Parameters.Algorithm)(
            challenge.Parameters,
            salt,
            counterPassword);
        var forgedSolution = new Solution
        {
            Counter = 0,
            DerivedKey = Convert.ToHexString(forgedKey).ToLowerInvariant()
        };

        var verification = AltchaPow.VerifySolution(new VerifySolutionOptions
        {
            Challenge = challenge,
            Solution = forgedSolution,
            HmacSignatureSecret = signatureSecret
        });

        Assert.False(verification.Verified);
        Assert.True(verification.InvalidSolution);
    }

    [Fact]
    public async Task SignedChallengeIsShortLivedAndPayloadCannotBeReplayed()
    {
        var replayStore = new InMemoryReplayStore();
        using var services = CreateAltchaServices(replayStore);
        var altcha = services.GetRequiredService<IAltchaService>();

        var challenge = altcha.CreateChallenge(new DefaultHttpContext());
        Assert.False(string.IsNullOrWhiteSpace(challenge.Signature));
        var expiresAt = challenge.Parameters.ExpiresAt
            ?? throw new InvalidOperationException("The generated ALTCHA challenge has no expiry.");
        var lifetime = DateTimeOffset.FromUnixTimeSeconds((long)expiresAt) - DateTimeOffset.UtcNow;
        Assert.InRange(lifetime.TotalSeconds, 0, 180);

        var solution = AltchaPow.SolveChallenge(new SolveChallengeOptions { Challenge = challenge });
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
            new { challenge, solution }, AltchaJson.SerializerOptions));

        var first = await altcha.VerifyAsync(payload, _ => string.Empty);
        var replay = await altcha.VerifyAsync(payload, _ => string.Empty);

        Assert.True(first.Verified, first.Error);
        Assert.False(replay.Verified);
        Assert.Equal(AltchaErrorCode.Replayed, replay.ErrorCode);
    }

    [Fact]
    public async Task RedisReplayClaimUsesAtomicSetIfAbsentAndBoundedExpiry()
    {
        var claims = new ConcurrentDictionary<string, byte>();
        var expiries = new ConcurrentBag<TimeSpan>();
        var database = new Mock<IDatabase>();
        database.Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                When.NotExists,
                CommandFlags.None))
            .Returns((RedisKey key, RedisValue _, TimeSpan? expiry, When _, CommandFlags _) =>
            {
                if (expiry is { } value) expiries.Add(value);
                return Task.FromResult(claims.TryAdd(key.ToString(), 0));
            });
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);
        var store = new RedisAltchaReplayStore(redis.Object, NullLogger<RedisAltchaReplayStore>.Instance);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(2);

        var claimsResult = await Task.WhenAll(Enumerable.Range(0, 24)
            .Select(_ => store.TryClaimAsync("same-altcha-nonce", expiresAt).AsTask()));

        Assert.Single(claimsResult, claimed => claimed);
        Assert.Equal(24, claimsResult.Length);
        Assert.All(expiries, expiry => Assert.InRange(expiry.TotalMinutes, 0, 5));
        database.Verify(db => db.StringSetAsync(
            It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
            When.NotExists, CommandFlags.None), Times.Exactly(24));
    }

    [Fact]
    public async Task RedisFailureAndOutOfBoundsExpiryRejectCaptchaClaim()
    {
        var database = new Mock<IDatabase>();
        database.Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
                When.NotExists, CommandFlags.None))
            .Returns(Task.FromException<bool>(new RedisConnectionException(
                ConnectionFailureType.SocketFailure, "simulated Redis outage")));
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);
        var store = new RedisAltchaReplayStore(redis.Object, NullLogger<RedisAltchaReplayStore>.Instance);

        var unavailable = await store.TryClaimAsync("nonce", DateTimeOffset.UtcNow.AddMinutes(1));
        var expired = await store.TryClaimAsync("expired", DateTimeOffset.UtcNow.AddSeconds(-1));
        var unbounded = await store.TryClaimAsync("unbounded", DateTimeOffset.UtcNow.AddMinutes(6));

        Assert.False(unavailable);
        Assert.False(expired);
        Assert.False(unbounded);
        database.Verify(db => db.StringSetAsync(
            It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
            When.NotExists, CommandFlags.None), Times.Once);
    }

    private static ServiceProvider CreateAltchaServices(IAltchaReplayStore replayStore)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddAltcha(options =>
        {
            options.HmacSignatureSecret = "unit-test-only-signature-secret-1234567890";
            options.HmacKeySignatureSecret = "unit-test-only-key-signature-secret-1234567890";
            options.ReplayProtection = true;
            options.Challenge.Cost = 10;
            options.Challenge.CounterMin = 1;
            options.Challenge.CounterMax = 32;
            options.Challenge.KeyPrefixLength = 4;
            options.Challenge.Expires = TimeSpan.FromMinutes(3);
        });
        services.AddSingleton<IAltchaReplayStore>(replayStore);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class InMemoryReplayStore : IAltchaReplayStore
    {
        private readonly ConcurrentDictionary<string, DateTimeOffset> _claims = new();

        public ValueTask<bool> TryClaimAsync(
            string key,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(expiresAt > DateTimeOffset.UtcNow && _claims.TryAdd(key, expiresAt));
        }
    }
}
