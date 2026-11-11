using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Altcha.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Smes.Api.Infrastructure;
using Smes.Api.Infrastructure.Auth;
using Smes.Api.Messaging;
using Smes.Api.Services;
using Smes.Domain.Identity;
using Smes.Infrastructure;
using Smes.Infrastructure.Seed;
using Smes.Infrastructure.Tenancy;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Default is required.");

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.Secret) || jwtOptions.Secret.Length < 32 ||
    jwtOptions.Secret.Contains("change", StringComparison.OrdinalIgnoreCase) ||
    jwtOptions.Secret.Contains("super-secret", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Jwt:Secret must be a non-placeholder secret of at least 32 characters.");

var redisConfiguration = builder.Configuration["Redis:Configuration"];
if (string.IsNullOrWhiteSpace(redisConfiguration))
    throw new InvalidOperationException("Redis:Configuration is required.");

var rabbitHost = builder.Configuration["RabbitMq:Host"];
var rabbitUser = builder.Configuration["RabbitMq:UserName"];
var rabbitPassword = builder.Configuration["RabbitMq:Password"];
if (string.IsNullOrWhiteSpace(rabbitHost) || string.IsNullOrWhiteSpace(rabbitUser) || string.IsNullOrWhiteSpace(rabbitPassword))
    throw new InvalidOperationException("RabbitMq host and credentials are required.");

var altchaSignatureSecret = builder.Configuration["Altcha:HmacSignatureSecret"];
var altchaKeySignatureSecret = builder.Configuration["Altcha:HmacKeySignatureSecret"];
if (!IsUsableSecret(altchaSignatureSecret) || !IsUsableSecret(altchaKeySignatureSecret) ||
    string.Equals(altchaSignatureSecret, altchaKeySignatureSecret, StringComparison.Ordinal))
    throw new InvalidOperationException(
        "Altcha:HmacSignatureSecret and Altcha:HmacKeySignatureSecret must be distinct, non-placeholder secrets of at least 32 characters.");

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<CurrentUser>(sp =>
{
    var principal = sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User;
    if (principal?.Identity?.IsAuthenticated != true) return new CurrentUser();
    long.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id);
    long.TryParse(principal.FindFirst("tenantId")?.Value, out var tenantId);
    return new CurrentUser
    {
        Id = id,
        TenantId = tenantId,
        TenantCode = principal.FindFirst("tenantCode")?.Value ?? "",
        SessionId = principal.FindFirst("sid")?.Value ?? "",
        Username = principal.FindFirst(ClaimTypes.Name)?.Value ?? "system",
        Nickname = principal.FindFirst("nickname")?.Value ?? "",
        IsAdmin = principal.FindFirst("isAdmin")?.Value == "1",
        Roles = principal.FindAll(ClaimTypes.Role).Select(x => x.Value).ToList(),
        Permissions = principal.FindAll("perm").Select(x => x.Value).ToList()
    };
});

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<JwtService>();
builder.Services.AddScoped<StockService>();
builder.Services.AddDbContext<SmesDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 4, 0))));

var redisOptions = ConfigurationOptions.Parse(redisConfiguration);
redisOptions.AbortOnConnectFail = false;
redisOptions.ConnectRetry = 3;
redisOptions.ConnectTimeout = 5000;
redisOptions.SyncTimeout = 5000;
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
builder.Services.AddScoped<ISessionStore, RedisSessionStore>();
builder.Services.AddAltcha(options =>
{
    options.HmacSignatureSecret = altchaSignatureSecret;
    options.HmacKeySignatureSecret = altchaKeySignatureSecret;
    options.ReplayProtection = true;
    options.Challenge.Expires = TimeSpan.FromMinutes(3);
    options.Challenge.Cost = 5_000;
});
builder.Services.AddSingleton<IAltchaReplayStore, RedisAltchaReplayStore>();
builder.Services.AddSingleton<ILoginAttemptRateLimiter, RedisLoginAttemptRateLimiter>();
builder.Services.AddWorkReportMessaging();
builder.Services.AddHostedService<MaterialIssueTopicConsumer>();
builder.Services.AddQualityJudgeMessaging();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var sessions = context.HttpContext.RequestServices.GetRequiredService<ISessionStore>();
                var jwt = context.HttpContext.RequestServices.GetRequiredService<JwtService>();
                if (context.Principal is null || !await jwt.IsActiveAccessSessionAsync(context.Principal, sessions, context.HttpContext.RequestAborted))
                {
                    context.Fail("Session is revoked or unavailable.");
                    return;
                }
                var tenant = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
                if (!long.TryParse(context.Principal.FindFirst("tenantId")?.Value, out var tenantId))
                {
                    context.Fail("Tenant claim is missing.");
                    return;
                }
                tenant.TenantId = tenantId;
                tenant.TenantCode = context.Principal.FindFirst("tenantCode")?.Value;
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(JsonSerializer.Serialize(ApiResult.Fail("登录已过期，请重新登录", 401)));
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(JsonSerializer.Serialize(ApiResult.Fail("无权执行此操作", 403)));
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.AddPolicy("AdminOnly", policy => policy.RequireClaim("isAdmin", "1"));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        await context.HttpContext.Response.WriteAsync(
            JsonSerializer.Serialize(ApiResult.Fail("请求过于频繁，请稍后再试", StatusCodes.Status429TooManyRequests)),
            cancellationToken);
    };
    options.AddPolicy("altcha-challenge", context => RateLimitPartition.GetFixedWindowLimiter(
        GetRateLimitKey(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
if (!builder.Environment.IsDevelopment() && allowedOrigins.Length == 0)
    throw new InvalidOperationException("AllowedOrigins must be configured in production.");
builder.Services.AddCors(options => options.AddPolicy("frontend", policy =>
{
    if (allowedOrigins.Length > 0) policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    else policy.WithOrigins("http://localhost:8848", "http://localhost:8088").AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var traceId = context.TraceIdentifier;
    app.Logger.LogError(error, "Unhandled request error {TraceId}", traceId);
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "application/json; charset=utf-8";
    await context.Response.WriteAsync(JsonSerializer.Serialize(ApiResult.Fail($"服务器内部错误，跟踪号：{traceId}", 500)));
}));

app.UseCors("frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapAltchaChallenge("/api/altcha/challenge")
    .AllowAnonymous()
    .RequireRateLimiting("altcha-challenge");
app.MapHealthChecks("/health/live").AllowAnonymous();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options => options.WithTitle("SMES API")).AllowAnonymous();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SmesDbContext>();
    var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    for (var attempt = 1; attempt <= 30; attempt++)
    {
        try
        {
            await EnsureDatabaseMigrationStateIsSafeAsync(db);
            await db.Database.MigrateAsync();
            if (builder.Configuration.GetValue<bool>("Seed:IndustryScenariosEnabled"))
            {
                await IndustryScenarioSeeder.SeedAsync(db, tenantContext,
                    builder.Configuration["Seed:AdminPassword"],
                    builder.Configuration["Seed:DemoUserPassword"],
                    builder.Configuration["Seed:ScenarioPassword"]);
            }
            else if (builder.Configuration.GetValue<bool>("Seed:DemoEnabled"))
            {
                var tenant = await db.Set<Tenant>().FirstOrDefaultAsync(x => x.Code == "machinery");
                if (tenant is null)
                {
                    tenant = new Tenant { Code = "machinery", Name = "机械制造演示租户", Status = 1 };
                    db.Add(tenant);
                    await db.SaveChangesAsync();
                }
                tenantContext.TenantId = tenant.Id;
                tenantContext.TenantCode = tenant.Code;
                await DbSeeder.SeedAsync(db,
                    builder.Configuration["Seed:AdminPassword"],
                    builder.Configuration["Seed:DemoUserPassword"]);
            }
            var tenantIds = await db.Set<Tenant>().Select(x => new { x.Id, x.Code }).ToListAsync();
            foreach (var item in tenantIds)
            {
                db.ChangeTracker.Clear();
                tenantContext.TenantId = item.Id;
                tenantContext.TenantCode = item.Code;
                await DbSeeder.EnsureFeatureMenusAsync(db);
            }
            logger.LogInformation("Database initialization completed");
            break;
        }
        catch (Exception ex)
        {
            if (await db.Database.CanConnectAsync())
            {
                logger.LogCritical(ex, "Database schema/seed initialization failed");
                throw;
            }
            logger.LogWarning(ex, "Database is not ready ({Attempt}/30)", attempt);
            if (attempt == 30) throw;
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}

static async Task EnsureDatabaseMigrationStateIsSafeAsync(SmesDbContext db)
{
    var connection = db.Database.GetDbConnection();
    await db.Database.OpenConnectionAsync();
    try
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'";
        var tableCount = Convert.ToInt64(await command.ExecuteScalarAsync());

        command.CommandText = "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '__EFMigrationsHistory' AND TABLE_TYPE = 'BASE TABLE'";
        var historyTableExists = Convert.ToInt64(await command.ExecuteScalarAsync()) != 0;
        if (!historyTableExists && tableCount != 0)
        {
            throw new InvalidOperationException(
                "Refusing automatic EF migration: this database already has tables but no __EFMigrationsHistory. " +
                "Do not run Migrate against an EnsureCreated database. Verify and explicitly adopt only with " +
                "scripts/migrations/adopt-baseline; see docs/DATABASE_MIGRATIONS.md.");
        }

        if (historyTableExists)
        {
            command.CommandText = "SELECT COUNT(*) FROM `__EFMigrationsHistory`";
            var appliedMigrationCount = Convert.ToInt64(await command.ExecuteScalarAsync());
            if (appliedMigrationCount == 0 && tableCount > 1)
            {
                throw new InvalidOperationException(
                    "Refusing automatic EF migration: __EFMigrationsHistory is empty in a non-empty database. " +
                    "Complete the verified baseline adoption procedure in docs/DATABASE_MIGRATIONS.md first.");
            }
        }
    }
    finally
    {
        await db.Database.CloseConnectionAsync();
    }
}

static bool IsUsableSecret(string? secret) =>
    !string.IsNullOrWhiteSpace(secret) &&
    secret.Length >= 32 &&
    !secret.Contains("change", StringComparison.OrdinalIgnoreCase) &&
    !secret.Contains("placeholder", StringComparison.OrdinalIgnoreCase) &&
    !secret.Contains("example", StringComparison.OrdinalIgnoreCase);

static string GetRateLimitKey(HttpContext context)
{
    // The private Nginx gateway overwrites X-Real-IP; direct callers fall back to the socket address.
    var forwardedClientIp = context.Request.Headers["X-Real-IP"].ToString();
    return IPAddress.TryParse(forwardedClientIp, out var parsedAddress)
        ? parsedAddress.ToString()
        : context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
}

app.Run();
