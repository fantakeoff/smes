using Microsoft.EntityFrameworkCore;
using Smes.Domain.Identity;
using Smes.Domain.MasterData;
using Smes.Domain.Production;
using Smes.Domain.Purchase;
using Smes.Domain.Warehouse;
using Smes.Infrastructure.Tenancy;

namespace Smes.Infrastructure.Seed;

/// <summary>Creates stable tenant-scoped fixtures for three manufacturing scenarios.</summary>
public static class IndustryScenarioSeeder
{
    public const string Prefix = "E2E_";
    private static readonly (string Key, string Label, string Product, string Material, string Process)[] Scenarios =
    [
        ("machinery", "离散机加工", "机加工轴承座", "合金钢坯", "CNC车削"),
        ("electronics", "电子装配", "控制板组件", "PCB板", "SMT贴装"),
        ("food", "食品批次", "果汁成品批次", "浓缩果汁", "灌装")
    ];

    public static async Task SeedAsync(SmesDbContext db, ITenantContext tenantContext,
        string? adminPassword, string? demoUserPassword, string? scenarioPassword,
        CancellationToken cancellationToken = default)
    {
        DbSeeder.ValidateSeedPassword(scenarioPassword, "Seed:ScenarioPassword");
        DbSeeder.ValidateSeedPassword(adminPassword, "Seed:AdminPassword");
        DbSeeder.ValidateSeedPassword(demoUserPassword, "Seed:DemoUserPassword");
        foreach (var scenario in Scenarios)
        {
            var tenant = await db.Set<Tenant>().FirstOrDefaultAsync(x => x.Code == scenario.Key, cancellationToken);
            if (tenant is null)
            {
                tenant = new Tenant { Code = scenario.Key, Name = scenario.Label + "测试租户", Status = 1 };
                db.Add(tenant);
                await db.SaveChangesAsync(cancellationToken);
            }

            tenantContext.TenantId = tenant.Id;
            tenantContext.TenantCode = tenant.Code;

            // Each tenant receives the standard menu/role/demo foundation first.
            await DbSeeder.SeedAsync(db, adminPassword, demoUserPassword);
            await SeedTenantAsync(db, tenant.Id, scenario, scenarioPassword!, cancellationToken);
        }
    }

    private static async Task SeedTenantAsync(SmesDbContext db, long tenantId,
        (string Key, string Label, string Product, string Material, string Process) scenario,
        string scenarioPassword, CancellationToken ct)
    {
        var prefix = Prefix + scenario.Key.ToUpperInvariant() + "_";
        var user = await EnsureAsync(db, new SysUser
        {
            Code = prefix + "USER",
            Username = $"{scenario.Key}_operator",
            Nickname = scenario.Label + "测试员",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(scenarioPassword),
            IsAdmin = true
        }, ct);
        var unit = await EnsureAsync(db, new Unit
        {
            Code = prefix + "UNIT", Name = scenario.Key == "food" ? "千克" : "件",
            UnitType = scenario.Key == "food" ? 2 : 1
        }, ct);
        var warehouse = await EnsureAsync(db, new Warehouse
        {
            Code = prefix + "WH", Name = prefix + "原料仓", Type = 1, IsDefault = true, ManagerId = user.Id
        }, ct);
        await EnsureAsync(db, new Warehouse { Code = prefix + "WH_TRANSFER", Name = prefix + "调拨目标仓", Type = 1 }, ct);
        var supplier = await EnsureAsync(db, new Supplier
        {
            Code = prefix + "SUP", Name = prefix + "测试供应商", ShortName = prefix + "供应商", SupplierType = 1
        }, ct);
        var workshop = await EnsureAsync(db, new Workshop { Code = prefix + "WS", Name = prefix + scenario.Label + "车间" }, ct);
        var process = await EnsureAsync(db, new Process
        {
            Code = prefix + "PROC", Name = prefix + scenario.Process, WorkshopId = workshop.Id, StandardHours = 1
        }, ct);
        var material = await EnsureAsync(db, new Product
        {
            Code = prefix + "MAT", Name = prefix + scenario.Material, ProductType = 3, UnitId = unit.Id,
            CostPrice = 10, SafetyStock = 20
        }, ct);
        var product = await EnsureAsync(db, new Product
        {
            Code = prefix + "FG", Name = prefix + scenario.Product, ProductType = 1, UnitId = unit.Id,
            SalePrice = 20, CostPrice = 10
        }, ct);

        var bom = await EnsureAsync(db, new Bom
        {
            Code = prefix + "BOM", Name = prefix + scenario.Product + " BOM", ProductId = product.Id, IsDefault = true,
            Lines = [new BomLine { Code = prefix + "BOM-L1", Name = scenario.Material, Seq = 1, MaterialId = material.Id, UnitId = unit.Id, Qty = scenario.Key == "food" ? 0.5m : 1m }]
        }, ct);
        var route = await EnsureAsync(db, new Routing
        {
            Code = prefix + "ROUTE", Name = prefix + scenario.Label + "路线", ProductId = product.Id, IsDefault = true,
            Steps = [new RoutingStep { Code = prefix + "ROUTE-S1", Name = scenario.Process, Seq = 1, ProcessId = process.Id, WorkshopId = workshop.Id, StandardHours = 1 }]
        }, ct);
        var shortWorkOrder = await EnsureAsync(db, new WorkOrder
        {
            Code = prefix + "WO_SHORT", Name = prefix + "缺料工单", ProductId = product.Id, BomId = bom.Id,
            RoutingId = route.Id, Qty = 50, WorkOrderStatus = 1, WorkshopId = workshop.Id
        }, ct);
        var readyWorkOrder = await EnsureAsync(db, new WorkOrder
        {
            Code = prefix + "WO_READY", Name = prefix + "充足库存工单", ProductId = product.Id, BomId = bom.Id,
            RoutingId = route.Id, Qty = 5, WorkOrderStatus = 1, WorkshopId = workshop.Id
        }, ct);
        var materialPerUnit = scenario.Key == "food" ? 0.5m : 1m;
        await EnsureAsync(db, new WorkOrderMaterial
        {
            Code = prefix + "WO_SHORT_MAT", Name = material.Name, WorkOrderId = shortWorkOrder.Id,
            ProductId = material.Id, UnitId = unit.Id, RequiredQty = 50 * materialPerUnit
        }, ct);
        await EnsureAsync(db, new WorkOrderMaterial
        {
            Code = prefix + "WO_READY_MAT", Name = material.Name, WorkOrderId = readyWorkOrder.Id,
            ProductId = material.Id, UnitId = unit.Id, RequiredQty = 5 * materialPerUnit
        }, ct);
        await EnsureAsync(db, new InventoryStock
        {
            Code = prefix + "STOCK_READY", Name = prefix + "充足库存", ProductId = material.Id,
            WarehouseId = warehouse.Id, Qty = 5, BatchNo = scenario.Key == "food" ? prefix + "LOT-001" : "",
            Location = ""
        }, ct);
        await EnsureAsync(db, new PurchaseOrder
        {
            Code = prefix + "PO", Name = prefix + "缺料补货采购单", SupplierId = supplier.Id,
            OrderDate = DateTime.Today, ExpectedDate = DateTime.Today.AddDays(3), OrderStatus = 1,
            TotalQty = 100, TotalAmount = material.CostPrice * 100,
            Lines = [new PurchaseOrderLine { Code = prefix + "PO-L1", Name = material.Name, Seq = 1, ProductId = material.Id, Qty = 100, Price = material.CostPrice, Amount = material.CostPrice * 100 }]
        }, ct);
    }

    private static async Task<T> EnsureAsync<T>(SmesDbContext db, T entity, CancellationToken ct) where T : class
    {
        var codeProperty = typeof(T).GetProperty("Code");
        var code = codeProperty?.GetValue(entity) as string;
        if (!string.IsNullOrWhiteSpace(code))
        {
            var candidates = await db.Set<T>().ToListAsync(ct);
            var existing = candidates.FirstOrDefault(x => Equals(codeProperty!.GetValue(x), code));
            if (existing is not null) return existing;
        }
        db.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }
}
