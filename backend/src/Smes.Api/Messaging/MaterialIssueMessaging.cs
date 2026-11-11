using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Smes.Api.Services;
using Smes.Domain.Production;
using Smes.Domain.Warehouse;
using Smes.Infrastructure;

namespace Smes.Api.Messaging;

public sealed record MaterialIssueRequested(Guid MessageId, long JobId, long TenantId, long BillId, long CreatedBy);

/// <summary>Separate durable topic queue; WorkReportOutboxDispatcher publishes all outbox routing keys.</summary>
public sealed class MaterialIssueTopicConsumer(IServiceScopeFactory scopes, IConfiguration config,
    ILogger<MaterialIssueTopicConsumer> logger) : BackgroundService
{
    private const string Exchange = "smes.events";
    private const string Queue = "smes.production.material-issue.v1";
    private const string DlqRoute = Queue + ".dead";
    private const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var factory = new ConnectionFactory { HostName = config["RabbitMq:Host"] ?? throw new InvalidOperationException("RabbitMq:Host is required"),
                    Port = int.TryParse(config["RabbitMq:Port"], out var port) ? port : 5672,
                    UserName = config["RabbitMq:UserName"] ?? throw new InvalidOperationException("RabbitMq:UserName is required"),
                    Password = config["RabbitMq:Password"] ?? throw new InvalidOperationException("RabbitMq:Password is required"),
                    VirtualHost = config["RabbitMq:VirtualHost"] ?? "/", AutomaticRecoveryEnabled = true };
                await using var connection = await factory.CreateConnectionAsync("smes-material-issue", stoppingToken);
                await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true), stoppingToken);
                await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, true, false, cancellationToken: stoppingToken);
                await channel.QueueDeclareAsync(Queue, true, false, false, arguments: new Dictionary<string, object?>(), cancellationToken: stoppingToken);
                await channel.QueueBindAsync(Queue, Exchange, "tenant.*.production.material-issue.requested.v1", cancellationToken: stoppingToken);
                await channel.QueueDeclareAsync(Queue + ".dlq", true, false, false, arguments: new Dictionary<string, object?>(), cancellationToken: stoppingToken);
                await channel.QueueBindAsync(Queue + ".dlq", Exchange, DlqRoute, cancellationToken: stoppingToken);
                await channel.BasicQosAsync(0, 10, false, stoppingToken);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, ea) => Handle(channel, ea, stoppingToken);
                await channel.BasicConsumeAsync(Queue, false, consumer, stoppingToken);
                while (!stoppingToken.IsCancellationRequested && channel.IsOpen) await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Material-issue consumer disconnected; retrying"); await Task.Delay(3000, stoppingToken); }
        }
    }

    private async Task Handle(IChannel channel, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var attempt = GetAttempt(ea.BasicProperties.Headers);
        MaterialIssueRequested? evt = null;
        try
        {
            evt = JsonSerializer.Deserialize<MaterialIssueRequested>(ea.Body.Span) ?? throw new InvalidDataException("领料事件无效");
            if (!Guid.TryParse(ea.BasicProperties.MessageId, out var headerId) || headerId != evt.MessageId || evt.MessageId == Guid.Empty)
                throw new InvalidDataException("领料事件 MessageId 不匹配");
            using var scope = scopes.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<Smes.Infrastructure.Tenancy.ITenantContext>(); tenant.TenantId = evt.TenantId;
            var db = scope.ServiceProvider.GetRequiredService<SmesDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            if (await db.Set<InboxMessage>().AnyAsync(x => x.MessageId == evt.MessageId && x.TenantId == evt.TenantId, ct))
            { await tx.CommitAsync(ct); await channel.BasicAckAsync(ea.DeliveryTag, false, ct); return; }
            var job = await db.Set<MaterialIssueJob>().FirstOrDefaultAsync(x => x.Id == evt.JobId && x.TenantId == evt.TenantId, ct)
                ?? throw new InvalidOperationException("领料任务不存在");
            if (job.BillId != evt.BillId) throw new InvalidDataException("任务与领料单不匹配");
            if (job.JobStatus == 2) { db.Add(new InboxMessage { TenantId = evt.TenantId, MessageId = evt.MessageId }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); await channel.BasicAckAsync(ea.DeliveryTag, false, ct); return; }
            var bill = await db.Set<StockBill>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == evt.BillId && x.TenantId == evt.TenantId, ct)
                ?? throw new InvalidOperationException("领料草稿不存在");
            var lineIds = bill.Lines.Select(l => l.Id).ToList();
            var links = await db.Set<MaterialIssueLineLink>().Where(x => lineIds.Contains(x.BillLineId) && x.TenantId == evt.TenantId).ToListAsync(ct);
            if (bill.BillType != 2 || bill.BillStatus != 0 || links.Count != bill.Lines.Count) throw new InvalidOperationException("领料单状态或物料映射无效");
            var materialIds = links.Select(l => l.WorkOrderMaterialId).ToList();
            var mats = await db.Set<WorkOrderMaterial>().Where(x => materialIds.Contains(x.Id) && x.WorkOrderId == job.WorkOrderId && x.TenantId == evt.TenantId).ToDictionaryAsync(x => x.Id, ct);
            if (links.Any(l => !mats.ContainsKey(l.WorkOrderMaterialId))) throw new InvalidOperationException("工单用料行不存在");
            foreach (var link in links)
            {
                var mat = mats[link.WorkOrderMaterialId];
                if (bill.Lines.Single(x => x.Id == link.BillLineId).Qty != link.Qty || mat.IssuedQty + link.Qty > mat.RequiredQty)
                    throw new InvalidOperationException("领料数量与工单剩余需求不匹配");
            }
            job.JobStatus = 1;
            var stock = scope.ServiceProvider.GetRequiredService<StockService>();
            await stock.ApplyAsync(bill, evt.CreatedBy);
            foreach (var link in links) mats[link.WorkOrderMaterialId].IssuedQty += link.Qty;
            var wo = await db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == job.WorkOrderId && x.TenantId == evt.TenantId, ct);
            if (wo?.WorkOrderStatus == 1) wo.WorkOrderStatus = 2;
            job.JobStatus = 2; job.Attempts = attempt + 1; job.Error = null; job.CompletedAt = DateTime.UtcNow;
            db.Add(new InboxMessage { TenantId = evt.TenantId, MessageId = evt.MessageId });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Material issue processing failed");
            try
            {
                if (attempt + 1 < MaxAttempts)
                {
                    var props = new BasicProperties { Persistent = true, ContentType = "application/json", MessageId = ea.BasicProperties.MessageId, Type = ea.BasicProperties.Type,
                        Headers = ea.BasicProperties.Headers == null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(ea.BasicProperties.Headers) };
                    props.Headers["x-smes-attempt"] = attempt + 1;
                    await channel.BasicPublishAsync(Exchange, ea.RoutingKey, true, props, ea.Body, ct);
                }
                else
                {
                    var props = new BasicProperties { Persistent = true, MessageId = ea.BasicProperties.MessageId,
                        Headers = new Dictionary<string, object?> { ["x-error"] = Encoding.UTF8.GetBytes(ex.Message[..Math.Min(400, ex.Message.Length)]) } };
                    await channel.BasicPublishAsync(Exchange, DlqRoute, true, props, ea.Body, ct);
                    if (evt != null)
                    {
                        using var scope = scopes.CreateScope(); var tenant = scope.ServiceProvider.GetRequiredService<Smes.Infrastructure.Tenancy.ITenantContext>(); tenant.TenantId = evt.TenantId;
                        var db = scope.ServiceProvider.GetRequiredService<SmesDbContext>();
                        var job = await db.Set<MaterialIssueJob>().FirstOrDefaultAsync(x => x.Id == evt.JobId && x.TenantId == evt.TenantId, ct);
                        if (job != null) { job.JobStatus = 3; job.Attempts = attempt + 1; job.Error = ex.Message[..Math.Min(450, ex.Message.Length)]; await db.SaveChangesAsync(ct); }
                    }
                }
                await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
            }
            catch (Exception publishError) { logger.LogError(publishError, "Material issue retry/DLQ publish failed"); if (channel.IsOpen) await channel.BasicNackAsync(ea.DeliveryTag, false, true, ct); }
        }
    }

    private static int GetAttempt(IDictionary<string, object?>? headers)
    {
        if (headers == null || !headers.TryGetValue("x-smes-attempt", out var value)) return 0;
        return value switch { byte b => b, int i => i, long l => (int)l, byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed, _ => 0 };
    }
}
