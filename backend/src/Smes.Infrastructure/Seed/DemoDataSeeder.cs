using Microsoft.EntityFrameworkCore;
using Smes.Domain.Equipment;
using Smes.Domain.Exceptions;
using Smes.Domain.Identity;
using Smes.Domain.MasterData;
using Smes.Domain.Production;
using Smes.Domain.Purchase;
using Smes.Domain.Quality;
using Smes.Domain.Sales;
using Smes.Domain.Tooling;
using Smes.Domain.Warehouse;

namespace Smes.Infrastructure.Seed;

public static partial class DbSeeder
{
    /// <summary>演示业务数据：订单、计划、工单、库存、质检、设备等，便于看板与报表开箱即用。</summary>
    private static async Task SeedBusinessAsync(SmesDbContext db, List<Product> products, List<Customer> customers,
        List<Supplier> suppliers, List<Warehouse> warehouses, List<Workshop> workshops, List<Process> processes,
        List<Unit> units, List<SysUser> users, List<DefectItem> defects)
    {
        var rand = new Random(625);
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        // ---------- 销售订单 ----------
        var salesOrders = new List<SalesOrder>();
        for (var i = 0; i < 8; i++)
        {
            var customer = customers[i % customers.Count];
            var orderDate = i < 3 ? today : today.AddDays(-rand.Next(0, 25));
            var lines = new List<SalesOrderLine>();
            var lineCount = rand.Next(1, 4);
            for (var j = 0; j < lineCount; j++)
            {
                var p = products[rand.Next(0, 6)];
                var qty = rand.Next(20, 200);
                lines.Add(new SalesOrderLine
                {
                    Seq = j + 1, ProductId = p.Id, Qty = qty, Price = p.SalePrice,
                    Amount = qty * p.SalePrice, DeliveryDate = orderDate.AddDays(rand.Next(7, 25))
                });
            }
            salesOrders.Add(new SalesOrder
            {
                Code = $"SO{orderDate:yyyyMMdd}{i + 1:0000}", CustomerId = customer.Id, Name = customer.ShortName,
                OrderDate = orderDate, DeliveryDate = lines.Max(l => l.DeliveryDate),
                OrderStatus = i < 2 ? 3 : i < 5 ? 1 : i == 7 ? 0 : 2,
                TotalQty = lines.Sum(l => l.Qty), TotalAmount = lines.Sum(l => l.Amount),
                SalesmanId = users[3].Id, PaymentMethod = "月结30天", Address = customer.Address,
                Contact = customer.Contact, Phone = customer.Phone, Lines = lines
            });
        }
        db.AddRange(salesOrders);

        // ---------- 采购订单 ----------
        var purchaseOrders = new List<PurchaseOrder>();
        for (var i = 0; i < 6; i++)
        {
            var supplier = suppliers[i % suppliers.Count];
            var orderDate = today.AddDays(-rand.Next(0, 20));
            var lines = new List<PurchaseOrderLine>();
            for (var j = 0; j < 2; j++)
            {
                var p = products[rand.Next(9, products.Count)];
                var qty = rand.Next(200, 1500);
                lines.Add(new PurchaseOrderLine
                {
                    Seq = j + 1, ProductId = p.Id, Qty = qty, Price = p.CostPrice,
                    Amount = qty * p.CostPrice, ExpectedDate = orderDate.AddDays(rand.Next(3, 12))
                });
            }
            purchaseOrders.Add(new PurchaseOrder
            {
                Code = $"PO{orderDate:yyyyMMdd}{i + 1:0000}", SupplierId = supplier.Id, Name = supplier.ShortName,
                OrderDate = orderDate, ExpectedDate = lines.Max(l => l.ExpectedDate),
                OrderStatus = i < 2 ? 3 : i < 4 ? 2 : 1,
                TotalQty = lines.Sum(l => l.Qty), TotalAmount = lines.Sum(l => l.Amount), BuyerId = users[2].Id
            });
        }
        db.AddRange(purchaseOrders);
        await db.SaveChangesAsync();

        // ---------- 生产计划 ----------
        var plans = new List<ProductionPlan>();
        for (var i = 0; i < 4; i++)
        {
            var planDate = today.AddDays(-i * 3);
            var lines = new List<ProductionPlanLine>();
            for (var j = 0; j < 3; j++)
            {
                var p = products[(i + j) % 6];
                lines.Add(new ProductionPlanLine
                {
                    Seq = j + 1, ProductId = p.Id, Qty = rand.Next(50, 400),
                    DeliveryDate = planDate.AddDays(rand.Next(3, 12))
                });
            }
            plans.Add(new ProductionPlan
            {
                Code = $"PL{planDate:yyyyMMdd}{i + 1:000}", Name = $"周生产计划{i + 1}", PlanDate = planDate,
                StartDate = planDate, EndDate = planDate.AddDays(7), WorkshopId = workshops[i % 3].Id,
                PlanStatus = i == 0 ? 2 : i == 1 ? 1 : 3, SourceType = i % 2 == 0 ? 1 : 2,
                TotalQty = lines.Sum(l => l.Qty), Lines = lines
            });
        }
        db.AddRange(plans);
        await db.SaveChangesAsync();

        // ---------- 生产工单 ----------
        var workOrders = new List<WorkOrder>();
        var woIndex = 0;
        foreach (var plan in plans)
        {
            foreach (var line in plan.Lines)
            {
                woIndex++;
                var qty = line.Qty;
                var status = woIndex <= 2 ? 3 : woIndex <= 6 ? 2 : woIndex <= 8 ? 1 : 0;
                workOrders.Add(new WorkOrder
                {
                    Code = $"WO{plan.PlanDate:yyyyMMdd}{woIndex:0000}", Name = $"工单-{products.First(p => p.Id == line.ProductId).Name}",
                    PlanId = plan.Id, PlanLineId = line.Id, ProductId = line.ProductId, Qty = qty,
                    CompletedQty = status == 3 ? qty : status == 2 ? qty * 0.35m : 0,
                    WorkshopId = plan.WorkshopId, StartDate = plan.StartDate, EndDate = plan.EndDate,
                    WorkOrderStatus = status, Priority = woIndex % 4 + 1, SourceCode = plan.Code
                });
            }
        }
        db.AddRange(workOrders);
        await db.SaveChangesAsync();

        foreach (var wo in workOrders)
        {
            var bomLines = db.Set<BomLine>().Where(x => x.BomId == db.Set<Bom>().Where(b => b.IsDefault).Select(b => b.Id).First()).ToList();
            foreach (var bl in bomLines.Take(3))
            {
                db.Add(new WorkOrderMaterial
                {
                    WorkOrderId = wo.Id, ProductId = bl.MaterialId, UnitId = bl.UnitId,
                    RequiredQty = Math.Round(wo.Qty * bl.Qty * (1 + bl.LossRate / 100), 2),
                    IssuedQty = wo.WorkOrderStatus >= 2 ? Math.Round(wo.Qty * bl.Qty, 2) : 0
                });
            }
        }

        // ---------- 报工记录（近 12 天） ----------
        var reports = new List<WorkReport>();
        foreach (var wo in workOrders.Where(w => w.WorkOrderStatus >= 2))
        {
            var days = rand.Next(3, 10);
            var per = Math.Max(1, wo.Qty / days);
            for (var d = 0; d < days; d++)
            {
                var qty = Math.Min(per, wo.Qty);
                var defect = rand.Next(0, 4);
                reports.Add(new WorkReport
                {
                    Code = $"BG{wo.Code[2..]}{d:00}", Name = "报工",
                    WorkOrderId = wo.Id, ProcessId = processes[rand.Next(0, 5)].Id, WorkerId = users[0].Id,
                    WorkshopId = wo.WorkshopId, Qty = qty, QualifiedQty = qty - defect, DefectQty = defect,
                    WorkHours = Math.Round(qty * 0.05m, 2), ReportTime = today.AddDays(-d).AddHours(8 + rand.Next(0, 9)),
                    DefectItemId = defect > 0 ? defects[rand.Next(0, defects.Count)].Id : null
                });
            }
        }
        db.AddRange(reports);
        await db.SaveChangesAsync();

        // ---------- 库存 ----------
        var stocks = new List<InventoryStock>();
        foreach (var p in products)
        {
            stocks.Add(new InventoryStock
            {
                ProductId = p.Id, WarehouseId = p.ProductType == 1 ? warehouses[1].Id : warehouses[0].Id,
                Qty = p.ProductType == 3 ? rand.Next(30, 420) : rand.Next(120, 2600), LockedQty = rand.Next(0, 40), AvgCost = p.CostPrice,
                Location = $"{(char)('A' + rand.Next(0, 4))}-{rand.Next(1, 9):00}-{rand.Next(1, 5):00}",
                LastInAt = today.AddDays(-rand.Next(0, 6))
            });
        }
        db.AddRange(stocks);

        var stockBills = new List<StockBill>();
        var billTypes = new[] { 1, 2, 3, 4, 4, 1, 3, 6 };
        for (var i = 0; i < 12; i++)
        {
            var p = products[rand.Next(0, products.Count)];
            var type = billTypes[i % billTypes.Length];
            var inbound = type is 1 or 3 or 5;
            var billDate = today.AddDays(-rand.Next(0, 10));
            stockBills.Add(new StockBill
            {
                Code = $"CK{billDate:yyyyMMdd}{i + 1:0000}", Name = "出入库单", BillType = type,
                WarehouseId = inbound ? warehouses[0].Id : warehouses[1].Id, BillDate = billDate, BillStatus = 1,
                OperatorId = users[2].Id, TotalQty = rand.Next(20, 300), AuditAt = billDate.AddHours(10), AuditBy = 1,
                Lines = new List<StockBillLine>
                {
                    new() { Seq = 1, ProductId = p.Id, Qty = rand.Next(20, 300), UnitId = units[0].Id, Price = p.CostPrice, Amount = p.CostPrice * rand.Next(20, 300), BatchNo = $"B{billDate:yyMMdd}{i:00}" }
                }
            });
        }
        db.AddRange(stockBills);
        await db.SaveChangesAsync();

        var transactions = new List<InventoryTransaction>();
        foreach (var bill in stockBills)
        {
            var inbound = bill.BillType is 1 or 3 or 5;
            transactions.Add(new InventoryTransaction
            {
                BillId = bill.Id, BillCode = bill.Code, BillType = bill.BillType,
                ProductId = bill.Lines[0].ProductId, WarehouseId = bill.WarehouseId,
                Direction = inbound ? 1 : -1, Qty = bill.Lines[0].Qty,
                BeforeQty = rand.Next(100, 2000), AfterQty = rand.Next(100, 2000),
                Price = bill.Lines[0].Price, OccurredAt = bill.BillDate.AddHours(10), OperatorId = users[2].Id
            });
        }
        db.AddRange(transactions);

        // ---------- 检验 ----------
        var items = new List<InspectionItem>
        {
            new() { Code = "QI01", Name = "外观检查", ItemType = 3, Method = "目测", Standard = "无划伤、无变形", Unit = "" },
            new() { Code = "QI02", Name = "尺寸测量", ItemType = 1, Method = "卡尺", Standard = "±0.05mm", Unit = "mm", MinValue = -0.05m, MaxValue = 0.05m },
            new() { Code = "QI03", Name = "硬度检测", ItemType = 1, Method = "硬度计", Standard = "HRC 45-50", Unit = "HRC", MinValue = 45, MaxValue = 50 },
            new() { Code = "QI04", Name = "绝缘电阻", ItemType = 1, Method = "兆欧表", Standard = "≥100MΩ", Unit = "MΩ", MinValue = 100, MaxValue = 9999 },
            new() { Code = "QI05", Name = "功能测试", ItemType = 2, Method = "测试台", Standard = "运行正常", Unit = "" }
        };
        db.AddRange(items);
        await db.SaveChangesAsync();

        var schemes = new List<InspectionScheme>();
        foreach (var (type, name) in new[] { (1, "来料检验方案"), (2, "过程检验方案"), (3, "出货检验方案") })
        {
            var scheme = new InspectionScheme
            {
                Code = $"QC-S{type}", Name = name, InspectionType = type, AqlLevel = "1.0", SamplePlan = "GB/T 2828.1 一般检验水平 II",
                IsDefault = true, Items = items.Take(4).Select((it, idx) => new InspectionSchemeItem
                {
                    Seq = idx + 1, ItemId = it.Id, Standard = it.Standard, MinValue = it.MinValue, MaxValue = it.MaxValue
                }).ToList()
            };
            schemes.Add(scheme);
        }
        db.AddRange(schemes);
        await db.SaveChangesAsync();

        var inspections = new List<QualityInspection>();
        for (var i = 0; i < 12; i++)
        {
            var p = products[rand.Next(0, products.Count)];
            var type = i % 3 + 1;
            var qty = rand.Next(50, 500);
            var defect = i % 5 == 0 ? rand.Next(2, 12) : 0;
            inspections.Add(new QualityInspection
            {
                Code = $"QC{today:yyyyMMdd}{i + 1:000}", Name = "检验单", InspectionType = type,
                SourceType = type == 1 ? "采购订单" : type == 2 ? "生产工单" : "销售订单",
                SourceId = i, SourceCode = type == 1 ? purchaseOrders[i % purchaseOrders.Count].Code : workOrders[i % workOrders.Count].Code,
                ProductId = p.Id, BatchNo = $"B{today:yyMMdd}{i:00}", Qty = qty, SampleQty = Math.Max(3, qty / 20),
                QualifiedQty = qty - defect, DefectQty = defect, Result = i < 3 ? 0 : defect > 0 ? 2 : 1,
                InspectorId = users[1].Id, InspectTime = i < 3 ? null : today.AddDays(-rand.Next(0, 4)),
                DefectItemId = defect > 0 ? defects[rand.Next(0, defects.Count)].Id : null,
                SchemeId = schemes[type - 1].Id,
                Lines = schemes[type - 1].Items.Select((si, idx) => new QualityInspectionLine
                {
                    Seq = idx + 1, ItemId = si.ItemId, Standard = si.Standard,
                    ActualValue = idx == 0 ? "合格" : rand.Next(45, 52).ToString(),
                    Result = 1
                }).ToList()
            });
        }
        db.AddRange(inspections);

        // ---------- 设备 ----------
        var eqCats = new[] { "加工设备", "检测设备", "装配设备" }
            .Select((n, i) => new EquipmentCategory { Name = n, Code = $"EQ{i + 1:00}", Sort = i + 1 }).ToList();
        db.AddRange(eqCats);
        await db.SaveChangesAsync();

        var equipment = new List<Equipment>();
        var eqNames = new[] { "数控加工中心1#", "数控加工中心2#", "注塑机1#", "焊接机器人", "三坐标测量仪", "装配线1#", "测试台1#", "包装机1#" };
        for (var i = 0; i < eqNames.Length; i++)
        {
            equipment.Add(new Equipment
            {
                Code = $"EQ{i + 1:000}", Name = eqNames[i], CategoryId = eqCats[i % 3].Id,
                Model = $"M-{rand.Next(100, 999)}", SerialNo = $"SN{2020 + i}{rand.Next(1000, 9999)}",
                WorkshopId = workshops[i % 3].Id, EquipmentStatus = i == 2 ? 2 : i == 5 ? 3 : 1,
                PurchaseDate = today.AddYears(-rand.Next(1, 5)), Vendor = "海康装备", Location = $"车间{i % 3 + 1}-{i + 1:00}",
                RunHours = rand.Next(800, 9000), LastMaintainAt = today.AddDays(-rand.Next(3, 40))
            });
        }
        db.AddRange(equipment);
        await db.SaveChangesAsync();

        var plansM = new List<MaintenancePlan>();
        for (var i = 0; i < 6; i++)
        {
            plansM.Add(new MaintenancePlan
            {
                Code = $"MP{i + 1:000}", Name = (i % 2 == 0 ? "日常点检-" : "月度保养-") + equipment[i].Name,
                EquipmentId = equipment[i].Id, MaintenanceType = i % 2 == 0 ? 1 : 2,
                CycleDays = i % 2 == 0 ? 1 : 30, NextDate = today.AddDays(i % 2 == 0 ? 0 : rand.Next(1, 15)),
                Items = "润滑、紧固、清洁、精度校验", OwnerId = users[0].Id
            });
        }
        db.AddRange(plansM);
        await db.SaveChangesAsync();

        var tasks = new List<MaintenanceTask>();
        foreach (var p in plansM)
        {
            for (var i = 0; i < 2; i++)
            {
                tasks.Add(new MaintenanceTask
                {
                    PlanId = p.Id, EquipmentId = p.EquipmentId, MaintenanceType = p.MaintenanceType,
                    TaskDate = today.AddDays(-i), ExecutorId = users[0].Id, TaskStatus = i == 0 ? 0 : 1,
                    Result = i == 0 ? null : "正常", Items = p.Items, Cost = i == 0 ? 0 : rand.Next(0, 500),
                    FinishAt = i == 0 ? null : today.AddDays(-i).AddHours(16)
                });
            }
        }
        db.AddRange(tasks);

        db.AddRange(
            new RepairOrder { Code = $"WX{today:yyyyMMdd}001", Name = "设备维修", EquipmentId = equipment[2].Id, FaultDesc = "液压系统压力不足，动作缓慢", Level = 2, ReporterId = users[0].Id, ReportTime = today.AddDays(-1).AddHours(9), HandlerId = users[0].Id, RepairStatus = 1, DowntimeMinutes = 180 },
            new RepairOrder { Code = $"WX{today:yyyyMMdd}002", Name = "设备维修", EquipmentId = equipment[5].Id, FaultDesc = "传送带异响", Level = 1, ReporterId = users[0].Id, ReportTime = today.AddDays(-4).AddHours(14), HandlerId = users[0].Id, RepairStatus = 2, Solution = "更换轴承并加注润滑脂", FinishTime = today.AddDays(-4).AddHours(17), Cost = 320, DowntimeMinutes = 120 },
            new RepairOrder { Code = $"WX{today:yyyyMMdd}003", Name = "设备维修", EquipmentId = equipment[1].Id, FaultDesc = "主轴报警 ALM-102", Level = 1, ReporterId = users[0].Id, ReportTime = today.AddHours(8), RepairStatus = 0 });

        var alarms = new List<EquipmentAlarm>();
        var alarmTypes = new[] { 1, 2, 3, 4 };
        for (var i = 0; i < 7; i++)
        {
            alarms.Add(new EquipmentAlarm
            {
                Code = $"AL{i + 1:000}", Name = "设备报警", EquipmentId = equipment[i % equipment.Count].Id,
                AlarmCode = $"ALM-{100 + i}", AlarmType = alarmTypes[i % 4],
                Content = new[] { "主轴温度超上限", "润滑油压力低于下限", "设备网络离线", "伺服驱动器故障" }[i % 4],
                Level = i < 2 ? 3 : 1, AlarmStatus = i < 3 ? 0 : i < 5 ? 1 : i == 5 ? 2 : 3,
                OccurredAt = today.AddHours(-i * 3), RecoveredAt = i >= 3 ? today.AddHours(-i * 3 + 1) : null
            });
        }
        db.AddRange(alarms);

        // ---------- 工装 ----------
        var toolCats = new[] { "夹具", "模具", "量具" }.Select((n, i) => new ToolCategory { Name = n, Code = $"TC{i + 1:00}", Sort = i + 1 }).ToList();
        db.AddRange(toolCats);
        await db.SaveChangesAsync();
        var tools = new List<Tool>();
        for (var i = 0; i < 6; i++)
        {
            tools.Add(new Tool
            {
                Code = $"T{i + 1:000}", Name = new[] { "压缩机壳体夹具", "端盖冲压模", "曲轴检具", "装配定位工装", "气密测试工装", "游标卡尺" }[i],
                CategoryId = toolCats[i % 3].Id, Spec = $"TZ-{rand.Next(10, 99)}", TotalQty = rand.Next(2, 10),
                AvailableQty = rand.Next(0, 3), UnitId = units[0].Id, ToolStatus = i == 5 ? 2 : 1,
                Location = $"工具间-{i + 1:00}", PurchaseDate = today.AddYears(-1), ServiceLifeDays = 365
            });
        }
        db.AddRange(tools);
        await db.SaveChangesAsync();
        db.AddRange(
            new ToolTransaction { Code = $"LY{today:yyyyMMdd}001", Name = "工装领用", ToolId = tools[0].Id, TransactionType = 1, Qty = 1, UserId = users[0].Id, OccurDate = today.AddDays(-2), ExpectedReturnDate = today.AddDays(5), TransactionStatus = 1 },
            new ToolTransaction { Code = $"LY{today:yyyyMMdd}002", Name = "工装领用", ToolId = tools[3].Id, TransactionType = 1, Qty = 2, UserId = users[0].Id, OccurDate = today.AddDays(-6), ExpectedReturnDate = today.AddDays(-1), TransactionStatus = 1 },
            new ToolTransaction { Code = $"GH{today:yyyyMMdd}001", Name = "工装归还", ToolId = tools[1].Id, TransactionType = 2, Qty = 1, UserId = users[0].Id, OccurDate = today.AddDays(-1), ReturnDate = today.AddDays(-1), TransactionStatus = 1 });

        // ---------- 异常 ----------
        var exceptions = new List<ExceptionRecord>();
        for (var i = 0; i < 8; i++)
        {
            exceptions.Add(new ExceptionRecord
            {
                Code = $"YC{today:yyyyMMdd}{i + 1:000}", Name = "现场异常",
                ExceptionType = i % 5 + 1,
                Title = new[] { "设备突发停机", "来料尺寸超差", "物料短缺", "现场安全隐患", "工装损坏" }[i % 5],
                Description = "现场巡检发现，已通知相关责任人处理。", Level = i < 2 ? 3 : i < 5 ? 2 : 1,
                Source = "车间巡检", ReporterId = users[0].Id, ReportTime = today.AddDays(-rand.Next(0, 7)).AddHours(rand.Next(8, 18)),
                HandlerId = i < 3 ? null : users[1].Id, ExceptionStatus = i < 3 ? 0 : i < 6 ? 1 : 2,
                Solution = i >= 6 ? "已完成整改并验证" : null, CloseTime = i >= 6 ? today.AddDays(-1) : null
            });
        }
        db.AddRange(exceptions);

        // ---------- 消息 ----------
        db.AddRange(
            new SysNotice { Title = "关于 2026 年 11 月生产计划安排的通知", Content = "请各车间按照生产计划组织生产，确保本月交付目标达成。", Type = 2, PublisherId = users[0].Id, PublishTime = today.AddDays(-2).AddHours(9) },
            new SysNotice { Title = "质量管理体系内部审核通知", Content = "定于本周五进行内部质量审核，请各部门提前准备相关资料。", Type = 1, PublisherId = users[1].Id, PublishTime = today.AddDays(-1).AddHours(10) },
            new SysNotice { Title = "设备年度保养计划发布", Content = "设备部已发布年度保养计划，请各车间配合执行。", Type = 1, PublisherId = users[0].Id, PublishTime = today.AddDays(-5) });
        db.AddRange(
            new SysMessage { ReceiverId = 1, Title = "工单 WO 已下达", Content = "您负责的工单已下达，请及时安排生产。", Type = 3, Link = "/production/workorder", IsRead = false },
            new SysMessage { ReceiverId = 1, Title = "库存预警", Content = "产品 P20260010 库存低于安全库存。", Type = 3, Link = "/warehouse/alert", IsRead = false },
            new SysMessage { ReceiverId = 1, Title = "审批提醒", Content = "您有 1 条异常处理待办。", Type = 2, Link = "/exception/handle", IsRead = true, ReadAt = today });

        await db.SaveChangesAsync();
    }

}
