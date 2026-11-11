using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Smes.Domain.Production;
using Smes.Domain.Quality;
using Smes.Domain.Warehouse;
using Smes.Infrastructure;
using Smes.Infrastructure.Tenancy;

namespace Smes.Api.Messaging;

public static class QualityJudgeMessagingRegistration
{
    public static IServiceCollection AddQualityJudgeMessaging(this IServiceCollection services)
    {
        services.AddHostedService<QualityJudgeTopicConsumer>();
        return services;
    }
}

public sealed record QualityJudgeRequested(Guid MessageId, long JobId, long TenantId, long InspectionId);

internal static class SubcontractQualitySource
{
    public const string StockBillSourceType = "委外加工";
    public const string InspectionSourceType = "委外收货";
}

internal static class QualityJudgeRabbitTopology
{
    public const string Exchange = "smes.events";
    public const string Queue = "smes.quality.inspection.judge.v1";
    public const string DeadLetterQueue = Queue + ".dlq";
    public const string Binding = "tenant.*.quality.inspection.judge.requested.v1";

    public static async Task<IConnection> ConnectAsync(IConfiguration config, CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = config["RabbitMq:Host"] ?? throw new InvalidOperationException("RabbitMq:Host is required"),
            Port = int.TryParse(config["RabbitMq:Port"], out var port) ? port : 5672,
            UserName = config["RabbitMq:UserName"] ?? throw new InvalidOperationException("RabbitMq:UserName is required"),
            Password = config["RabbitMq:Password"] ?? throw new InvalidOperationException("RabbitMq:Password is required"),
            VirtualHost = config["RabbitMq:VirtualHost"] ?? "/", AutomaticRecoveryEnabled = true
        };
        return await factory.CreateConnectionAsync("smes-quality-judge", ct);
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
}

/// <summary>Manual-ack consumer with shared Inbox deduplication and confirmed retry/DLQ publication.</summary>
public sealed class QualityJudgeTopicConsumer : BackgroundService
{
    private const int MaxAttempts = 3;
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<QualityJudgeTopicConsumer> _logger;

    public QualityJudgeTopicConsumer(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<QualityJudgeTopicConsumer> logger)
    { _scopes = scopes; _configuration = configuration; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await QualityJudgeRabbitTopology.ConnectAsync(_configuration, stoppingToken);
                await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true), stoppingToken);
                await QualityJudgeRabbitTopology.DeclareAsync(channel, stoppingToken);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, ea) => HandleAsync(channel, ea, stoppingToken);
                await channel.BasicConsumeAsync(QualityJudgeRabbitTopology.Queue, autoAck: false, consumer, stoppingToken);
                while (!stoppingToken.IsCancellationRequested && channel.IsOpen) await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Quality-judge consumer disconnected; retrying");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var attempt = HeaderInt(ea.BasicProperties.Headers, "x-smes-attempt");
        QualityJudgeRequested? evt = null;
        try
        {
            evt = JsonSerializer.Deserialize<QualityJudgeRequested>(ea.Body.Span)
                  ?? throw new InvalidDataException("Invalid quality-judge event");
            if (evt.MessageId == Guid.Empty || evt.JobId <= 0 || evt.TenantId <= 0 || evt.InspectionId <= 0 ||
                !Guid.TryParse(ea.BasicProperties.MessageId, out var propertyId) || propertyId != evt.MessageId)
                throw new InvalidDataException("Missing or mismatched message id");
            using var scope = _scopes.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenant.TenantId = evt.TenantId;
            var db = scope.ServiceProvider.GetRequiredService<SmesDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            if (await db.Set<InboxMessage>().AnyAsync(x => x.TenantId == evt.TenantId && x.MessageId == evt.MessageId, ct))
            {
                await tx.CommitAsync(ct);
                await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
                return;
            }
            var job = await db.Set<QualityJudgeJob>().FirstOrDefaultAsync(x => x.Id == evt.JobId && x.TenantId == evt.TenantId, ct)
                      ?? throw new InvalidOperationException("Quality-judge job not found");
            var inspection = await db.Set<QualityInspection>().FirstOrDefaultAsync(x => x.Id == evt.InspectionId && x.TenantId == evt.TenantId, ct)
                             ?? throw new InvalidOperationException("Quality inspection not found");
            if (job.MessageId != evt.MessageId || job.InspectionId != inspection.Id)
                throw new InvalidDataException("Event does not match job");
            if (job.JobStatus == 2)
            {
                db.Add(new InboxMessage { TenantId = evt.TenantId, MessageId = evt.MessageId });
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
                return;
            }
            if (job.JobStatus == 3)
                throw new InvalidOperationException("Dead-lettered quality-judge job cannot be applied");
            if (inspection.Result != 0) throw new InvalidOperationException("Inspection already judged outside this job");
            if (job.QualifiedQty < 0 || job.DefectQty < 0 || job.QualifiedQty + job.DefectQty != inspection.SampleQty || job.Result is not (1 or 2 or 3))
                throw new InvalidDataException("Invalid stored quality-judge payload");
            var subcontractReceipt = await ValidateSubcontractReceiptAsync(db, inspection, evt.TenantId, ct);
            if (subcontractReceipt != null && job.Result == 1 &&
                (job.QualifiedQty != inspection.SampleQty || job.DefectQty != 0))
                throw new InvalidDataException("PASS subcontract inspection must qualify the complete sample");

            job.JobStatus = 1;
            job.Attempts = attempt + 1;
            inspection.Result = job.Result;
            inspection.QualifiedQty = job.QualifiedQty;
            inspection.DefectQty = job.DefectQty;
            inspection.DefectItemId = job.DefectItemId;
            inspection.Remark = job.JudgeRemark;
            inspection.InspectorId = job.RequestedBy;
            inspection.InspectTime = DateTime.UtcNow;
            if (subcontractReceipt != null && job.Result == 1)
                await ReleaseSubcontractQuarantineAsync(db, inspection, subcontractReceipt, evt.TenantId, ct);
            job.JobStatus = 2;
            job.CompletedAt = DateTime.UtcNow;
            job.Error = null;
            db.Add(new InboxMessage { TenantId = evt.TenantId, MessageId = evt.MessageId });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Quality-judge event processing failed");
            try
            {
                var nextAttempt = attempt + 1;
                if (nextAttempt < MaxAttempts)
                {
                    var headers = ea.BasicProperties.Headers == null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(ea.BasicProperties.Headers);
                    headers["x-smes-attempt"] = nextAttempt;
                    await channel.BasicPublishAsync(QualityJudgeRabbitTopology.Exchange, ea.RoutingKey, true,
                        new BasicProperties { Persistent = true, ContentType = "application/json", MessageId = ea.BasicProperties.MessageId, Type = ea.BasicProperties.Type, Headers = headers }, ea.Body, ct);
                }
                else
                {
                    await channel.BasicPublishAsync(QualityJudgeRabbitTopology.Exchange, QualityJudgeRabbitTopology.Queue + ".dead", true,
                        new BasicProperties { Persistent = true, ContentType = "application/json", MessageId = ea.BasicProperties.MessageId,
                            Type = "quality.inspection.judge.dead.v1", Headers = new Dictionary<string, object?> { ["x-error"] = Encoding.UTF8.GetBytes(ex.Message[..Math.Min(ex.Message.Length, 400)]) } }, ea.Body, ct);
                }
                if (evt != null)
                {
                    using var scope = _scopes.CreateScope();
                    var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                    tenant.TenantId = evt.TenantId;
                    var db = scope.ServiceProvider.GetRequiredService<SmesDbContext>();
                    var job = await db.Set<QualityJudgeJob>().FirstOrDefaultAsync(x => x.Id == evt.JobId && x.TenantId == evt.TenantId, ct);
                    if (job != null)
                    {
                        job.Attempts = nextAttempt;
                        job.Error = ex.Message[..Math.Min(ex.Message.Length, 450)];
                        if (nextAttempt >= MaxAttempts) job.JobStatus = 3;
                        else if (job.JobStatus != 2) job.JobStatus = 0;
                        await db.SaveChangesAsync(ct);
                    }
                }
                await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
            }
            catch (Exception retryError)
            {
                _logger.LogError(retryError, "Quality-judge retry/DLQ publication failed; requeueing");
                if (channel.IsOpen) await channel.BasicNackAsync(ea.DeliveryTag, false, true, ct);
            }
        }
    }

    private static int HeaderInt(IDictionary<string, object?>? headers, string key)
    {
        if (headers == null || !headers.TryGetValue(key, out var raw)) return 0;
        return raw switch { byte b => b, int i => i, long l => (int)l, byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var v) => v, _ => 0 };
    }

    private static async Task<StockBill?> ValidateSubcontractReceiptAsync(SmesDbContext db,
        QualityInspection inspection, long tenantId, CancellationToken ct)
    {
        if (inspection.SourceType != SubcontractQualitySource.InspectionSourceType) return null;
        if (inspection.SourceId is not long receiptBillId || receiptBillId <= 0 || inspection.Qty <= 0 ||
            inspection.SampleQty <= 0 || inspection.SampleQty > inspection.Qty)
            throw new InvalidDataException("Subcontract inspection has invalid receipt identity or quantity");

        var bill = await db.Set<StockBill>().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == receiptBillId && x.TenantId == tenantId, ct)
                   ?? throw new InvalidOperationException("Subcontract receipt bill not found");
        if (bill.BillType != 5 || bill.BillStatus != 1 || bill.SourceType != SubcontractQualitySource.StockBillSourceType ||
            bill.SourceId is not long orderId || bill.TotalQty != inspection.Qty || inspection.SourceCode != bill.Code ||
            bill.Lines.Count != 1 || bill.Lines[0].Qty != inspection.Qty ||
            bill.Lines[0].ProductId != inspection.ProductId || (bill.Lines[0].BatchNo ?? "") != (inspection.BatchNo ?? ""))
            throw new InvalidDataException("Subcontract inspection does not match its exact receipt bill");

        var order = await db.Set<SubcontractOrder>()
            .FirstOrDefaultAsync(x => x.Id == orderId && x.TenantId == tenantId, ct)
                    ?? throw new InvalidOperationException("Subcontract order not found for receipt");
        if (bill.SourceCode != order.Code || bill.WarehouseId != order.TargetWarehouseId ||
            bill.Lines[0].ProductId != order.OutputProductId || order.ReceivedQty < inspection.Qty ||
            order.ReceivedQty > order.IssuedQty || order.IssuedQty > order.OrderQty)
            throw new InvalidDataException("Subcontract receipt is inconsistent with its source order");
        return bill;
    }

    private static async Task ReleaseSubcontractQuarantineAsync(SmesDbContext db,
        QualityInspection inspection, StockBill receiptBill, long tenantId, CancellationToken ct)
    {
        var line = receiptBill.Lines[0];
        var stock = await db.Set<InventoryStock>().FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.ProductId == line.ProductId && x.WarehouseId == receiptBill.WarehouseId &&
            x.BatchNo == (line.BatchNo ?? "") && x.Location == (line.Location ?? ""), ct)
                    ?? throw new InvalidOperationException("Quarantined subcontract stock balance not found");
        if (stock.Qty < inspection.Qty || stock.LockedQty < inspection.Qty || stock.LockedQty > stock.Qty)
            throw new InvalidOperationException("Quarantined subcontract quantity is not present in locked stock");

        stock.LockedQty -= inspection.Qty;
    }
}
