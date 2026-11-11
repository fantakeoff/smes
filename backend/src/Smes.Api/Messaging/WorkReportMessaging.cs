using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Smes.Domain.Production;
using Smes.Infrastructure;

namespace Smes.Api.Messaging;

public static class WorkReportMessagingRegistration
{
    public static IServiceCollection AddWorkReportMessaging(this IServiceCollection services)
    {
        services.AddHostedService<WorkReportOutboxDispatcher>();
        services.AddHostedService<WorkReportTopicConsumer>();
        return services;
    }
}

public sealed record WorkReportRequested(Guid MessageId, long JobId, long TenantId, long WorkOrderId,
    long? ProcessId, long? WorkerId, decimal Qty, decimal QualifiedQty, decimal DefectQty,
    decimal WorkHours, long? DefectItemId, long CreatedBy, string? Remark);

/// <summary>Configure RabbitMQ through RabbitMq:Host/Port/UserName/Password/VHost.</summary>
internal static class WorkReportRabbitTopology
{
    public const string Exchange = "smes.events";
    public const string Queue = "smes.production.work-report.v1";
    public const string DeadLetterQueue = Queue + ".dlq";
    public const string Binding = "tenant.*.production.work-report.requested.v1";

    public static async Task<IConnection> ConnectAsync(IConfiguration config, CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = config["RabbitMq:Host"] ?? throw new InvalidOperationException("RabbitMq:Host is required"),
            Port = int.TryParse(config["RabbitMq:Port"], out var port) ? port : 5672,
            UserName = config["RabbitMq:UserName"] ?? throw new InvalidOperationException("RabbitMq:UserName is required"),
            Password = config["RabbitMq:Password"] ?? throw new InvalidOperationException("RabbitMq:Password is required"),
            VirtualHost = config["RabbitMq:VirtualHost"] ?? "/",
            AutomaticRecoveryEnabled = true
        };
        return await factory.CreateConnectionAsync("smes-work-report", ct);
    }

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>(), cancellationToken: ct);
        await channel.QueueBindAsync(Queue, Exchange, Binding, cancellationToken: ct);
        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>(), cancellationToken: ct);
        await channel.QueueBindAsync(DeadLetterQueue, Exchange, Queue + ".dead", cancellationToken: ct);
        await channel.BasicQosAsync(0, 10, false, ct);
    }

    public static CreateChannelOptions ConfirmOptions() => new(true, true);
}

/// <summary>Publishes committed outbox rows with publisher confirms; rows remain retryable on failure.</summary>
public sealed class WorkReportOutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WorkReportOutboxDispatcher> _logger;

    public WorkReportOutboxDispatcher(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<WorkReportOutboxDispatcher> logger)
    { _scopes = scopes; _configuration = configuration; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await WorkReportRabbitTopology.ConnectAsync(_configuration, stoppingToken);
                await using var channel = await connection.CreateChannelAsync(WorkReportRabbitTopology.ConfirmOptions(), stoppingToken);
                await WorkReportRabbitTopology.DeclareAsync(channel, stoppingToken);
                while (!stoppingToken.IsCancellationRequested)
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<SmesDbContext>();
                    var tenant = scope.ServiceProvider.GetRequiredService<Smes.Infrastructure.Tenancy.ITenantContext>();
                    var rows = await db.Set<OutboxMessage>().IgnoreQueryFilters().Where(x => x.PublishedAt == null)
                        .OrderBy(x => x.CreatedAt).Take(50).ToListAsync(stoppingToken);
                    if (rows.Count == 0) { await Task.Delay(750, stoppingToken); continue; }
                    foreach (var row in rows)
                    {
                        tenant.TenantId = row.TenantId ?? throw new InvalidOperationException("Outbox row is missing TenantId");
                        try
                        {
                            var props = new BasicProperties
                            {
                                Persistent = true, ContentType = "application/json",
                                MessageId = row.MessageId.ToString("N"), Type = row.EventType
                            };
                            await channel.BasicPublishAsync(WorkReportRabbitTopology.Exchange, row.RoutingKey, mandatory: true,
                                basicProperties: props, body: Encoding.UTF8.GetBytes(row.Payload), cancellationToken: stoppingToken);
                            row.PublishedAt = DateTime.UtcNow;
                            row.PublishAttempts++;
                            row.LastError = null;
                        }
                        catch (Exception ex)
                        {
                            row.PublishAttempts++;
                            row.LastError = ex.Message.Length > 450 ? ex.Message[..450] : ex.Message;
                            _logger.LogError(ex, "Failed to publish work-report outbox {OutboxId}", row.Id);
                            if (!channel.IsOpen) break;
                        }
                        await db.SaveChangesAsync(stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Work-report outbox dispatcher disconnected; retrying");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }
}

/// <summary>Consumes with manual acknowledgements and an Inbox unique key; redeliveries cannot double-apply reports.</summary>
public sealed class WorkReportTopicConsumer : BackgroundService
{
    private const int MaxAttempts = 3;
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WorkReportTopicConsumer> _logger;

    public WorkReportTopicConsumer(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<WorkReportTopicConsumer> logger)
    { _scopes = scopes; _configuration = configuration; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await WorkReportRabbitTopology.ConnectAsync(_configuration, stoppingToken);
                await using var channel = await connection.CreateChannelAsync(WorkReportRabbitTopology.ConfirmOptions(), stoppingToken);
                await WorkReportRabbitTopology.DeclareAsync(channel, stoppingToken);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, ea) => HandleAsync(channel, ea, stoppingToken);
                await channel.BasicConsumeAsync(WorkReportRabbitTopology.Queue, autoAck: false, consumer, stoppingToken);
                while (!stoppingToken.IsCancellationRequested && channel.IsOpen)
                    await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Work-report consumer disconnected; retrying");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var retry = HeaderInt(ea.BasicProperties.Headers, "x-smes-attempt");
        try
        {
            var evt = JsonSerializer.Deserialize<WorkReportRequested>(ea.Body.Span)
                      ?? throw new InvalidDataException("Invalid work report event");
            if (evt.MessageId == Guid.Empty || !Guid.TryParse(ea.BasicProperties.MessageId, out var propertyId) || propertyId != evt.MessageId)
                throw new InvalidDataException("Missing or mismatched event message id");
            using var scope = _scopes.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<Smes.Infrastructure.Tenancy.ITenantContext>();
            tenant.TenantId = evt.TenantId;
            var db = scope.ServiceProvider.GetRequiredService<SmesDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            if (await db.Set<InboxMessage>().AnyAsync(x => x.MessageId == evt.MessageId, ct))
            {
                await tx.CommitAsync(ct);
                await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
                return;
            }
            var job = await db.Set<WorkReportJob>().FirstOrDefaultAsync(x => x.Id == evt.JobId && x.TenantId == evt.TenantId, ct)
                      ?? throw new InvalidOperationException("Report job not found");
            if (job.JobStatus == 2)
            {
                db.Add(new InboxMessage { TenantId = evt.TenantId, MessageId = evt.MessageId });
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
                return;
            }
            var wo = await db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == evt.WorkOrderId && x.TenantId == evt.TenantId, ct)
                     ?? throw new InvalidOperationException("Work order not found");
            if (wo.WorkOrderStatus is 0 or 3 or 4) throw new InvalidOperationException("Work order status no longer accepts reports");
            if (evt.Qty <= 0 || evt.QualifiedQty < 0 || evt.DefectQty < 0 || evt.WorkHours < 0 || evt.QualifiedQty + evt.DefectQty != evt.Qty)
                throw new InvalidDataException("Invalid report quantities");
            if (wo.CompletedQty + wo.DefectQty + evt.Qty > wo.Qty) throw new InvalidOperationException("Report quantity exceeds remaining work-order quantity");
            var report = new WorkReport
            {
                TenantId = evt.TenantId, Code = $"BG{DateTime.UtcNow:yyyyMMddHHmmssfff}", Name = "报工",
                WorkOrderId = wo.Id, ProcessId = evt.ProcessId, WorkerId = evt.WorkerId, WorkshopId = wo.WorkshopId,
                Qty = evt.Qty, QualifiedQty = evt.QualifiedQty, DefectQty = evt.DefectQty, WorkHours = evt.WorkHours,
                DefectItemId = evt.DefectItemId, ReportTime = DateTime.Now, Remark = evt.Remark, CreatedBy = evt.CreatedBy
            };
            db.Add(report);
            wo.CompletedQty += evt.QualifiedQty;
            wo.DefectQty += evt.DefectQty;
            wo.WorkOrderStatus = wo.CompletedQty + wo.DefectQty >= wo.Qty ? 3 : 2;
            job.JobStatus = 2;
            job.CompletedAt = DateTime.UtcNow;
            job.Attempts = retry + 1;
            job.Error = null;
            db.Add(new InboxMessage { TenantId = evt.TenantId, MessageId = evt.MessageId });
            await db.SaveChangesAsync(ct);
            job.WorkReportId = report.Id;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Work-report event processing failed");
            try
            {
                if (retry + 1 < MaxAttempts)
                {
                    var props = new BasicProperties
                    {
                        Persistent = true, ContentType = ea.BasicProperties.ContentType,
                        MessageId = ea.BasicProperties.MessageId, Type = ea.BasicProperties.Type,
                        Headers = ea.BasicProperties.Headers == null
                            ? new Dictionary<string, object?>()
                            : new Dictionary<string, object?>(ea.BasicProperties.Headers)
                    };
                    props.Headers["x-smes-attempt"] = retry + 1;
                    await channel.BasicPublishAsync(WorkReportRabbitTopology.Exchange, ea.RoutingKey, true,
                        props, ea.Body, ct);
                }
                else
                {
                    var props = new BasicProperties
                    {
                        Persistent = true, MessageId = ea.BasicProperties.MessageId,
                        Headers = new Dictionary<string, object?> { ["x-error"] = Encoding.UTF8.GetBytes(ex.Message[..Math.Min(ex.Message.Length, 400)]) }
                    };
                    await channel.BasicPublishAsync(WorkReportRabbitTopology.Exchange, WorkReportRabbitTopology.Queue + ".dead", true,
                        props, ea.Body, ct);
                    using var scope = _scopes.CreateScope();
                    var failedTenant = scope.ServiceProvider.GetRequiredService<Smes.Infrastructure.Tenancy.ITenantContext>();
                    var db = scope.ServiceProvider.GetRequiredService<SmesDbContext>();
                    var evt = JsonSerializer.Deserialize<WorkReportRequested>(ea.Body.Span);
                    if (evt != null)
                    {
                        failedTenant.TenantId = evt.TenantId;
                        var job = await db.Set<WorkReportJob>().FirstOrDefaultAsync(x => x.Id == evt.JobId && x.TenantId == evt.TenantId, ct);
                        if (job != null)
                        {
                            job.JobStatus = 3;
                            job.Attempts = retry + 1;
                            job.Error = ex.Message[..Math.Min(ex.Message.Length, 450)];
                            await db.SaveChangesAsync(ct);
                        }
                    }
                }
                await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
            }
            catch (Exception retryError)
            {
                _logger.LogError(retryError, "Failed to republish or dead-letter event; nack/requeue");
                if (channel.IsOpen) await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: true, ct);
            }
        }
    }

    private static int HeaderInt(IDictionary<string, object?>? headers, string key)
    {
        if (headers == null || !headers.TryGetValue(key, out var raw)) return 0;
        return raw switch { byte b => b, int i => i, long l => (int)l, byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var v) => v, _ => 0 };
    }
}
