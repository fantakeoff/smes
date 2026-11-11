using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Smes.Domain.Common;
using Smes.Infrastructure.Tenancy;

namespace Smes.Infrastructure;

/// <summary>
/// 单一 DbContext，实体按命名空间自动发现，新增模块只需添加实体类，
/// 无需修改本文件（便于多人/多 Agent 并行开发）。
/// </summary>
public class SmesDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;
    public SmesDbContext(DbContextOptions<SmesDbContext> options, ITenantContext tenantContext) : base(options)
        => _tenantContext = tenantContext;
    public long? CurrentTenantId => _tenantContext.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var entityTypes = typeof(BaseEntity).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericType
                        && t.Namespace != null && t.Namespace.StartsWith("Smes.Domain")
                        && t.GetProperty("Id") != null)
            .ToList();

        foreach (var type in entityTypes)
        {
            modelBuilder.Entity(type);
            if (typeof(BaseEntity).IsAssignableFrom(type))
            {
                var param = System.Linq.Expressions.Expression.Parameter(type, "e");
                var tenantId = System.Linq.Expressions.Expression.Property(param, nameof(BaseEntity.TenantId));
                var currentTenant = System.Linq.Expressions.Expression.Property(
                    System.Linq.Expressions.Expression.Constant(this), nameof(CurrentTenantId));
                var body = System.Linq.Expressions.Expression.AndAlso(
                    System.Linq.Expressions.Expression.Not(System.Linq.Expressions.Expression.Property(param, nameof(BaseEntity.IsDeleted))),
                    System.Linq.Expressions.Expression.Equal(tenantId, currentTenant));
                modelBuilder.Entity(type).HasQueryFilter(
                    System.Linq.Expressions.Expression.Lambda(body, param));
            }
        }

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties())
            {
                if (prop.ClrType == typeof(decimal) || prop.ClrType == typeof(decimal?))
                {
                    prop.SetPrecision(18);
                    prop.SetScale(4);
                }
                if (prop.ClrType == typeof(string) && prop.GetMaxLength() == null)
                {
                    prop.SetMaxLength(500);
                }
            }
        }

        // 主从表关系（只在此处集中配置，模块代码不必关心）
        modelBuilder.Entity<Smes.Domain.Sales.SalesOrder>()
            .HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Smes.Domain.Purchase.PurchaseOrder>()
            .HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Smes.Domain.Purchase.PurchaseRequisition>()
            .HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.RequisitionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Smes.Domain.Production.ProductionPlan>()
            .HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Smes.Domain.Warehouse.StockBill>()
            .HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.BillId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Smes.Domain.Warehouse.StockTake>()
            .HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.TakeId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Smes.Domain.Quality.InspectionScheme>()
            .HasMany(x => x.Items).WithOne().HasForeignKey(x => x.SchemeId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Smes.Domain.Quality.QualityInspection>()
            .HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.InspectionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Smes.Domain.MasterData.Routing>()
            .HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.RoutingId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Smes.Domain.MasterData.Bom>()
            .HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.BomId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Smes.Domain.Identity.Tenant>().Property(x => x.Code).HasMaxLength(64);
        modelBuilder.Entity<Smes.Domain.Identity.SysUser>().Property(x => x.Username).HasMaxLength(100);
        modelBuilder.Entity<Smes.Domain.Identity.SysRole>().Property(x => x.Code).HasMaxLength(100);
        modelBuilder.Entity<Smes.Domain.MasterData.Product>().Property(x => x.Code).HasMaxLength(100);
        modelBuilder.Entity<Smes.Domain.Warehouse.InventoryStock>().Property(x => x.BatchNo).HasMaxLength(100);
        modelBuilder.Entity<Smes.Domain.Warehouse.InventoryStock>().Property(x => x.Location).HasMaxLength(100);
        modelBuilder.Entity<Smes.Domain.Warehouse.InventoryTransaction>().Property(x => x.IdempotencyKey).HasMaxLength(160);
        modelBuilder.Entity<Smes.Domain.Purchase.PurchaseRequisition>().Property(x => x.Code).HasMaxLength(100);
        modelBuilder.Entity<Smes.Domain.Warehouse.StockBill>().Property(x => x.Code).HasMaxLength(500);
        modelBuilder.Entity<Smes.Domain.Production.WorkReportJob>().Property(x => x.IdempotencyKey).HasMaxLength(128);

        modelBuilder.Entity<Smes.Domain.Identity.Tenant>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<Smes.Domain.Identity.SysUser>().HasIndex(x => new { x.TenantId, x.Username }).IsUnique();
        modelBuilder.Entity<Smes.Domain.Identity.SysRole>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        modelBuilder.Entity<Smes.Domain.Identity.SysUserRole>().HasQueryFilter(x => x.TenantId == CurrentTenantId);
        modelBuilder.Entity<Smes.Domain.Identity.SysRoleMenu>().HasQueryFilter(x => x.TenantId == CurrentTenantId);
        modelBuilder.Entity<Smes.Domain.Identity.SysLog>().HasQueryFilter(x => x.TenantId == CurrentTenantId);
        modelBuilder.Entity<Smes.Domain.Identity.SysUserRole>().HasIndex(x => new { x.TenantId, x.UserId, x.RoleId }).IsUnique();
        modelBuilder.Entity<Smes.Domain.Identity.SysRoleMenu>().HasIndex(x => new { x.TenantId, x.RoleId, x.MenuId }).IsUnique();
        modelBuilder.Entity<Smes.Domain.MasterData.Product>().HasIndex(x => new { x.TenantId, x.Code });
        modelBuilder.Entity<Smes.Domain.Warehouse.StockBill>().HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        modelBuilder.Entity<Smes.Domain.Warehouse.InventoryStock>()
            .HasIndex(x => new { x.TenantId, x.ProductId, x.WarehouseId, x.BatchNo, x.Location }).IsUnique();
        modelBuilder.Entity<Smes.Domain.Warehouse.InventoryTransaction>()
            .HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        modelBuilder.Entity<Smes.Domain.Purchase.PurchaseRequisition>()
            .HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        modelBuilder.Entity<Smes.Domain.Purchase.PurchaseRequisitionLine>()
            .HasIndex(x => new { x.TenantId, x.RequisitionId, x.Seq }).IsUnique();
        modelBuilder.Entity<Smes.Domain.Purchase.PurchaseOrderLine>()
            .HasIndex(x => new { x.TenantId, x.RequisitionLineId }).IsUnique();
        modelBuilder.Entity<Smes.Domain.Production.WorkReportJob>()
            .HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        modelBuilder.Entity<Smes.Domain.Production.MaterialIssueJob>().Property(x => x.IdempotencyKey).HasMaxLength(128);
        modelBuilder.Entity<Smes.Domain.Production.MaterialIssueJob>().Property(x => x.PayloadHash).HasMaxLength(64);
        modelBuilder.Entity<Smes.Domain.Production.MaterialIssueJob>()
            .HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        modelBuilder.Entity<Smes.Domain.Production.MaterialIssueJob>()
            .HasIndex(x => new { x.TenantId, x.BillId }).IsUnique();
        modelBuilder.Entity<Smes.Domain.Production.MaterialIssueLineLink>()
            .HasIndex(x => new { x.TenantId, x.BillLineId }).IsUnique();
        modelBuilder.Entity<Smes.Domain.Production.SubcontractOrder>().Property(x => x.Code).HasMaxLength(100);
        modelBuilder.Entity<Smes.Domain.Production.SubcontractOrder>()
            .HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        modelBuilder.Entity<Smes.Domain.Quality.QualityJudgeJob>().Property(x => x.IdempotencyKey).HasMaxLength(128);
        modelBuilder.Entity<Smes.Domain.Quality.QualityJudgeJob>().Property(x => x.PayloadHash).HasMaxLength(64);
        modelBuilder.Entity<Smes.Domain.Quality.QualityJudgeJob>()
            .HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        modelBuilder.Entity<Smes.Domain.Production.InboxMessage>()
            .HasIndex(x => new { x.TenantId, x.MessageId }).IsUnique();
        modelBuilder.Entity<Smes.Domain.Production.OutboxMessage>()
            .HasIndex(x => x.MessageId).IsUnique();
        modelBuilder.Entity<Smes.Domain.Production.OutboxMessage>()
            .HasIndex(x => new { x.PublishedAt, x.CreatedAt });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ProtectTenantWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ProtectTenantWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ProtectTenantWrites()
    {
        var tenantId = _tenantContext.TenantId;
        foreach (var entry in ChangeTracker.Entries().Where(e => e.Entity is BaseEntity ||
                     e.Entity is Smes.Domain.Identity.SysUserRole || e.Entity is Smes.Domain.Identity.SysRoleMenu ||
                     e.Entity is Smes.Domain.Identity.SysLog))
        {
            var property = entry.Property(nameof(BaseEntity.TenantId));
            if (entry.State == EntityState.Added)
            {
                if (tenantId is null) throw new InvalidOperationException("Tenant context is required to create tenant data.");
                if (property.CurrentValue is long supplied && supplied != 0 && supplied != tenantId.Value)
                    throw new InvalidOperationException("Cannot create data for another tenant.");
                property.CurrentValue = tenantId.Value;
            }
            else if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                if (tenantId is null || !Equals(property.OriginalValue, tenantId.Value))
                    throw new InvalidOperationException("Cannot modify data outside the current tenant.");
                if (property.IsModified && !Equals(property.CurrentValue, property.OriginalValue))
                    throw new InvalidOperationException("Tenant ownership cannot be changed.");
            }
        }
    }
}
