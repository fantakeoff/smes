using Microsoft.EntityFrameworkCore;
using Smes.Domain.Identity;
using Smes.Domain.MasterData;

namespace Smes.Infrastructure.Seed;

public static partial class DbSeeder
{
    private sealed class MenuNode
    {
        public string Title = "";
        public string Path = "";
        public string? Component;
        public string Icon = "ep:menu";
        public string Permission = "";
        public MenuNode[] Children = Array.Empty<MenuNode>();
    }

    private static MenuNode M(string title, string path, string comp, string perm, string icon = "ep:document")
        => new() { Title = title, Path = path, Component = comp, Permission = perm, Icon = icon };

    private static MenuNode Dir(string title, string path, string icon, params MenuNode[] children)
        => new() { Title = title, Path = path, Icon = icon, Children = children };

    private static readonly MenuNode[] Menus =
    {
        Dir("仪表板", "/dashboard", "ep:home-filled",
            M("分析页", "/dashboard/analysis", "dashboard/analysis/index", "dashboard:analysis:view", "ep:data-analysis"),
            M("工作台", "/dashboard/workbench", "dashboard/workbench/index", "dashboard:workbench:view", "ep:monitor")),
        Dir("基础数据", "/basic", "ep:goods",
            M("产品分类", "/basic/category", "basic/category/index", "basic:category:list", "ep:collection-tag"),
            M("产品品牌", "/basic/brand", "basic/brand/index", "basic:brand:list", "ep:price-tag"),
            M("产品管理", "/basic/product", "basic/product/index", "basic:product:list", "ep:box"),
            M("计量单位", "/basic/unit", "basic/unit/index", "basic:unit:list", "ep:scale-to-original"),
            M("仓库信息", "/basic/warehouse", "basic/warehouse/index", "basic:warehouse:list", "ep:house"),
            M("客户信息", "/basic/customer", "basic/customer/index", "basic:customer:list", "ep:user"),
            M("供应商信息", "/basic/supplier", "basic/supplier/index", "basic:supplier:list", "ep:office-building"),
            M("车间产线", "/basic/workshop", "basic/workshop/index", "basic:workshop:list", "ep:operation"),
            M("工序管理", "/basic/process", "basic/process/index", "basic:process:list", "ep:set-up"),
            M("工艺路线", "/basic/routing", "basic/routing/index", "basic:routing:list", "ep:guide"),
            M("BOM管理", "/basic/bom", "basic/bom/index", "basic:bom:list", "ep:share"),
            M("不良品项", "/basic/defect", "basic/defect/index", "basic:defect:list", "ep:warning"),
            M("工厂日历", "/basic/calendar", "basic/calendar/index", "basic:calendar:list", "ep:calendar")),
        Dir("销售管理", "/sales", "ep:shopping-cart",
            M("销售订单", "/sales/order", "sales/order/index", "sales:order:list", "ep:document"),
            M("销售出库", "/sales/outbound", "sales/outbound/index", "sales:outbound:list", "ep:van"),
            M("销售退货", "/sales/return", "sales/return/index", "sales:return:list", "ep:refresh-left")),
        Dir("采购管理", "/purchase", "ep:shopping-bag",
            M("采购申请", "/purchase/requisition", "purchase/requisition/index", "purchase:requisition:list", "ep:list"),
            M("采购订单", "/purchase/order", "purchase/order/index", "purchase:order:list", "ep:document"),
            M("采购收货", "/purchase/receipt", "purchase/receipt/index", "purchase:receipt:list", "ep:download"),
            M("采购退货", "/purchase/return", "purchase/return/index", "purchase:return:list", "ep:refresh-right")),
        Dir("生产管理", "/production", "ep:cpu",
            M("生产计划", "/production/plan", "production/plan/index", "production:plan:list", "ep:calendar"),
            M("生产工单", "/production/workorder", "production/workorder/index", "production:workorder:list", "ep:tickets"),
            M("生产领料", "/production/issue", "production/issue/index", "production:issue:list", "ep:takeaway-box"),
            M("委外加工", "/production/subcontract", "production/subcontract/index", "production:subcontract:list", "ep:van"),
            M("生产入库", "/production/receipt", "production/receipt/index", "production:receipt:list", "ep:box"),
            M("报工记录", "/production/report", "production/report/index", "production:report:list", "ep:edit-pen"),
            M("计划排程", "/production/schedule", "production/schedule/index", "production:schedule:list", "ep:sort")),
        Dir("质量管理", "/quality", "ep:circle-check",
            M("检验项目", "/quality/item", "quality/item/index", "quality:item:list", "ep:list"),
            M("检验方案", "/quality/scheme", "quality/scheme/index", "quality:scheme:list", "ep:files"),
            M("检验任务", "/quality/inspection", "quality/inspection/index", "quality:inspection:list", "ep:finished"),
            M("质量看板", "/quality/board", "quality/board/index", "quality:board:view", "ep:data-line")),
        Dir("库存管理", "/warehouse", "ep:files",
            M("库存余额", "/warehouse/stock", "warehouse/stock/index", "warehouse:stock:list", "ep:coin"),
            M("库存流水", "/warehouse/transaction", "warehouse/transaction/index", "warehouse:transaction:list", "ep:sort"),
            M("出入库单", "/warehouse/bill", "warehouse/bill/index", "warehouse:bill:list", "ep:document"),
            M("库存调拨", "/warehouse/transfer", "warehouse/transfer/index", "warehouse:transfer:list", "ep:switch"),
            M("库存盘点", "/warehouse/take", "warehouse/take/index", "warehouse:take:list", "ep:check"),
            M("库存预警", "/warehouse/alert", "warehouse/alert/index", "warehouse:alert:list", "ep:bell")),
        Dir("设备管理", "/equipment", "ep:monitor",
            M("设备分类", "/equipment/category", "equipment/category/index", "equipment:category:list", "ep:collection"),
            M("设备台账", "/equipment/list", "equipment/list/index", "equipment:list:list", "ep:platform"),
            M("点检保养计划", "/equipment/plan", "equipment/plan/index", "equipment:plan:list", "ep:calendar"),
            M("点检保养任务", "/equipment/task", "equipment/task/index", "equipment:task:list", "ep:finished"),
            M("设备报修", "/equipment/repair", "equipment/repair/index", "equipment:repair:list", "ep:tools"),
            M("设备报警", "/equipment/alarm", "equipment/alarm/index", "equipment:alarm:list", "ep:warning")),
        Dir("工装管理", "/tooling", "ep:suitcase",
            M("工装台账", "/tooling/list", "tooling/list/index", "tooling:list:list", "ep:box"),
            M("工装领用", "/tooling/receive", "tooling/receive/index", "tooling:receive:list", "ep:download"),
            M("工装归还", "/tooling/return", "tooling/return/index", "tooling:return:list", "ep:upload")),
        Dir("异常管理", "/exception", "ep:warning",
            M("异常提报", "/exception/report", "exception/report/index", "exception:report:list", "ep:edit"),
            M("异常处理", "/exception/handle", "exception/handle/index", "exception:handle:list", "ep:finished")),
        Dir("消息中心", "/message", "ep:message",
            M("系统通知", "/message/notice", "message/notice/index", "message:notice:list", "ep:bell"),
            M("我的消息", "/message/inbox", "message/inbox/index", "message:inbox:list", "ep:chat-dot-round")),
        Dir("报表分析", "/report", "ep:trend-charts",
            M("生产报表", "/report/production", "report/production/index", "report:production:view", "ep:data-line"),
            M("工单进度看板", "/report/board", "report/board/index", "report:board:view", "ep:monitor")),
        Dir("系统管理", "/system", "ep:setting",
            M("用户管理", "/system/user", "system/user/index", "system:user:list", "ep:user"),
            M("角色管理", "/system/role", "system/role/index", "system:role:list", "ep:avatar"),
            M("菜单管理", "/system/menu", "system/menu/index", "system:menu:list", "ep:menu"),
            M("部门管理", "/system/dept", "system/dept/index", "system:dept:list", "ep:office-building"),
            M("数据字典", "/system/dict", "system/dict/index", "system:dict:list", "ep:notebook"),
            M("系统参数", "/system/config", "system/config/index", "system:config:list", "ep:setting"),
            M("操作日志", "/system/log", "system/log/index", "system:log:list", "ep:document"))
    };

    /// <summary>在已有租户的菜单树中增量注册新模块；只授权给管理员角色。</summary>
    public static async Task EnsureFeatureMenusAsync(SmesDbContext db)
    {
        var parent = await db.Set<SysMenu>().FirstOrDefaultAsync(x => x.Path == "/production");
        if (parent == null || await db.Set<SysMenu>().AnyAsync(x => x.Path == "/production/subcontract")) return;
        var menu = new SysMenu
        {
            ParentId = parent.Id, Title = "委外加工", Path = "/production/subcontract",
            Component = "production/subcontract/index", Icon = "ep:van", Type = 2,
            Permission = "production:subcontract:list", Sort = parent.Sort + 1,
            Visible = true, Status = 1
        };
        db.Add(menu);
        await db.SaveChangesAsync();
        var adminRoles = await db.Set<SysRole>().Where(x => x.Code == "admin").Select(x => x.Id).ToListAsync();
        foreach (var roleId in adminRoles) db.Add(new SysRoleMenu { RoleId = roleId, MenuId = menu.Id });
        await db.SaveChangesAsync();
    }

    /// <summary>幂等种子：首次启动写入管理员、菜单、演示基础数据与业务数据。</summary>
    public static async Task SeedAsync(SmesDbContext db, string? adminPassword, string? demoUserPassword)
    {
        if (db.Set<SysUser>().Any())
        {
            return;
        }

        ValidateSeedPassword(adminPassword, "Seed:AdminPassword");
        ValidateSeedPassword(demoUserPassword, "Seed:DemoUserPassword");

        var now = DateTime.Now;

        // ---------- 组织 ----------
        var deptRoot = new SysDept { Name = "爱思智造有限公司", Code = "HQ", ParentId = 0, Sort = 1 };
        db.Add(deptRoot);
        await db.SaveChangesAsync();
        var depts = new[] { "总经办", "生产部", "品质部", "销售部", "采购部", "仓储部", "设备部" }
            .Select((n, i) => new SysDept { Name = n, Code = $"D{i + 1:00}", ParentId = deptRoot.Id, Sort = i + 1 })
            .ToList();
        db.AddRange(depts);
        await db.SaveChangesAsync();

        // ---------- 角色与用户 ----------
        var roleAdmin = new SysRole { Name = "超级管理员", Code = "admin", Sort = 1, DataScope = 1, Remark = "拥有全部权限" };
        var roleProd = new SysRole { Name = "生产主管", Code = "prod", Sort = 2, DataScope = 2 };
        var roleQuality = new SysRole { Name = "品质工程师", Code = "quality", Sort = 3, DataScope = 2 };
        var roleWarehouse = new SysRole { Name = "仓管员", Code = "warehouse", Sort = 4, DataScope = 3 };
        var roleSales = new SysRole { Name = "销售员", Code = "sales", Sort = 5, DataScope = 3 };
        db.AddRange(roleAdmin, roleProd, roleQuality, roleWarehouse, roleSales);
        await db.SaveChangesAsync();

        var hash = BCrypt.Net.BCrypt.HashPassword(demoUserPassword);
        var admin = new SysUser
        {
            Username = "admin", PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
            Nickname = "系统管理员", IsAdmin = true, DeptId = depts[0].Id, Phone = "13800000000",
            Email = "admin@aisi-mom.local", Avatar = ""
        };
        var users = new List<SysUser>
        {
            new() { Username = "zhangsan", PasswordHash = hash, Nickname = "张三", DeptId = depts[1].Id, Phone = "13800000001" },
            new() { Username = "lisi", PasswordHash = hash, Nickname = "李四", DeptId = depts[2].Id, Phone = "13800000002" },
            new() { Username = "wangwu", PasswordHash = hash, Nickname = "王五", DeptId = depts[5].Id, Phone = "13800000003" },
            new() { Username = "zhaoliu", PasswordHash = hash, Nickname = "赵六", DeptId = depts[3].Id, Phone = "13800000004" }
        };
        db.Add(admin);
        db.AddRange(users);
        await db.SaveChangesAsync();
        db.AddRange(
            new SysUserRole { UserId = admin.Id, RoleId = roleAdmin.Id },
            new SysUserRole { UserId = users[0].Id, RoleId = roleProd.Id },
            new SysUserRole { UserId = users[1].Id, RoleId = roleQuality.Id },
            new SysUserRole { UserId = users[2].Id, RoleId = roleWarehouse.Id },
            new SysUserRole { UserId = users[3].Id, RoleId = roleSales.Id });

        // ---------- 菜单 ----------
        var sort = 0;
        async Task AddMenuAsync(MenuNode node, long parentId, int type)
        {
            var menu = new SysMenu
            {
                ParentId = parentId, Title = node.Title, Path = node.Path, Component = node.Component,
                Icon = node.Icon, Type = type, Permission = node.Permission, Sort = ++sort,
                Visible = true, Status = 1
            };
            db.Add(menu);
            await db.SaveChangesAsync();
            db.Add(new SysRoleMenu { RoleId = roleAdmin.Id, MenuId = menu.Id });
            foreach (var child in node.Children)
            {
                await AddMenuAsync(child, menu.Id, child.Children.Length > 0 ? 1 : 2);
            }
        }
        foreach (var menu in Menus)
        {
            await AddMenuAsync(menu, 0, 1);
        }

        // ---------- 计量单位 ----------
        var units = new[] { ("个", "PCS"), ("件", "EA"), ("台", "SET"), ("套", "KIT"), ("千克", "KG"), ("米", "M"), ("箱", "BOX"), ("张", "SHT") }
            .Select((u, i) => new Unit { Name = u.Item1, Code = u.Item2, UnitType = i < 4 ? 1 : 2, Precision = 0 })
            .ToList();
        db.AddRange(units);
        await db.SaveChangesAsync();

        // ---------- 仓库 ----------
        var warehouses = new List<Warehouse>
        {
            new() { Code = "WH01", Name = "原材料仓", Type = 1, Location = "A区", IsDefault = true },
            new() { Code = "WH02", Name = "成品仓", Type = 3, Location = "B区" },
            new() { Code = "WH03", Name = "半成品仓", Type = 2, Location = "C区" },
            new() { Code = "WH04", Name = "不良品仓", Type = 5, Location = "D区" }
        };
        db.AddRange(warehouses);
        await db.SaveChangesAsync();

        // ---------- 客户 / 供应商 ----------
        var customers = new List<Customer>
        {
            new() { Code = "C001", Name = "合肥美菱电器股份有限公司", ShortName = "美菱电器", Contact = "刘经理", Phone = "0551-66660001", Address = "安徽省合肥市经开区", Level = 3, CreditLimit = 5000000 },
            new() { Code = "C002", Name = "格力电器（合肥）有限公司", ShortName = "格力合肥", Contact = "陈经理", Phone = "0551-66660002", Address = "合肥市高新区", Level = 3, CreditLimit = 8000000 },
            new() { Code = "C003", Name = "苏州汇川技术有限公司", ShortName = "汇川技术", Contact = "王工", Phone = "0512-66660003", Address = "苏州市吴中区", Level = 2, CreditLimit = 3000000 },
            new() { Code = "C004", Name = "宁波奥克斯空调有限公司", ShortName = "奥克斯", Contact = "赵经理", Phone = "0574-66660004", Address = "宁波市鄞州区", Level = 2, CreditLimit = 2000000 },
            new() { Code = "C005", Name = "上海海立（集团）股份有限公司", ShortName = "海立集团", Contact = "孙经理", Phone = "021-66660005", Address = "上海市浦东新区", Level = 1, CreditLimit = 1500000 }
        };
        db.AddRange(customers);

        var suppliers = new List<Supplier>
        {
            new() { Code = "S001", Name = "宝武钢铁集团有限公司", ShortName = "宝武钢铁", Contact = "周经理", Phone = "021-88880001", SupplierType = 1, Level = 3 },
            new() { Code = "S002", Name = "宁波韵升磁业有限公司", ShortName = "韵升磁业", Contact = "吴经理", Phone = "0574-88880002", SupplierType = 1, Level = 2 },
            new() { Code = "S003", Name = "深圳信维通信股份有限公司", ShortName = "信维通信", Contact = "郑经理", Phone = "0755-88880003", SupplierType = 1, Level = 2 },
            new() { Code = "S004", Name = "昆山精密机械加工厂", ShortName = "昆山精机", Contact = "冯厂长", Phone = "0512-88880004", SupplierType = 2, Level = 1 },
            new() { Code = "S005", Name = "杭州海康智能装备有限公司", ShortName = "海康装备", Contact = "蒋经理", Phone = "0571-88880005", SupplierType = 3, Level = 1 }
        };
        db.AddRange(suppliers);
        await db.SaveChangesAsync();

        // ---------- 车间 / 工序 ----------
        var workshops = new List<Workshop>
        {
            new() { Code = "WS01", Name = "机加工车间", WorkshopType = 1, Manager = "张三", Sort = 1 },
            new() { Code = "WS02", Name = "装配车间", WorkshopType = 1, Manager = "李强", Sort = 2 },
            new() { Code = "WS03", Name = "包装车间", WorkshopType = 1, Manager = "王敏", Sort = 3 },
            new() { Code = "WS04", Name = "SMT产线", WorkshopType = 2, ParentId = 0, Manager = "陈刚", Sort = 4 }
        };
        db.AddRange(workshops);
        await db.SaveChangesAsync();

        var processes = new List<Process>
        {
            new() { Code = "OP10", Name = "下料", ProcessType = 1, StandardHours = 0.5m, Price = 2.5m, WorkshopId = workshops[0].Id },
            new() { Code = "OP20", Name = "机加工", ProcessType = 1, StandardHours = 1.2m, Price = 6m, WorkshopId = workshops[0].Id },
            new() { Code = "OP30", Name = "焊接", ProcessType = 1, StandardHours = 0.8m, Price = 4m, WorkshopId = workshops[0].Id },
            new() { Code = "OP40", Name = "喷涂", ProcessType = 1, StandardHours = 0.6m, Price = 3m, WorkshopId = workshops[1].Id },
            new() { Code = "OP50", Name = "装配", ProcessType = 2, StandardHours = 2m, Price = 8m, WorkshopId = workshops[1].Id },
            new() { Code = "OP60", Name = "测试", ProcessType = 3, StandardHours = 0.4m, Price = 2m, WorkshopId = workshops[1].Id },
            new() { Code = "OP70", Name = "包装", ProcessType = 4, StandardHours = 0.3m, Price = 1m, WorkshopId = workshops[2].Id }
        };
        db.AddRange(processes);
        await db.SaveChangesAsync();

        // ---------- 产品分类 / 品牌 / 产品 ----------
        var catRoot = new ProductCategory { Name = "压缩机零部件", Code = "PC01", Sort = 1 };
        var catElec = new ProductCategory { Name = "电子产品", Code = "PC02", Sort = 2 };
        db.AddRange(catRoot, catElec);
        await db.SaveChangesAsync();
        var catMetal = new ProductCategory { Name = "机加件", Code = "PC0101", ParentId = catRoot.Id, Sort = 1 };
        var catMotor = new ProductCategory { Name = "电机件", Code = "PC0102", ParentId = catRoot.Id, Sort = 2 };
        db.AddRange(catMetal, catMotor);
        await db.SaveChangesAsync();

        var brands = new[] { ("爱思", "ASI"), ("美菱", "ML"), ("汇川", "HC") }
            .Select(b => new ProductBrand { Name = b.Item1, Code = b.Item2 }).ToList();
        db.AddRange(brands);
        await db.SaveChangesAsync();

        var rand = new Random(20261103);
        var productNames = new[]
        {
            ("压缩机壳体", "CK-120A", 1, catMetal.Id, 128.00m, 96.00m),
            ("压缩机上盖", "SG-120A", 2, catMetal.Id, 45.00m, 32.00m),
            ("曲轴", "QZ-88", 2, catMetal.Id, 86.00m, 62.00m),
            ("连杆", "LG-45", 2, catMetal.Id, 32.00m, 21.00m),
            ("活塞", "HS-60", 2, catMetal.Id, 25.00m, 16.50m),
            ("气缸体", "QG-90", 1, catMetal.Id, 156.00m, 118.00m),
            ("定子铁芯", "DZ-75", 1, catMotor.Id, 210.00m, 168.00m),
            ("转子组件", "ZZ-75", 1, catMotor.Id, 185.00m, 142.00m),
            ("电机端盖", "DG-75", 2, catMotor.Id, 58.00m, 40.00m),
            ("接线端子", "JD-12", 3, catElec.Id, 6.50m, 3.20m),
            ("控制主板", "KB-200", 1, catElec.Id, 320.00m, 245.00m),
            ("温度传感器", "WD-01", 3, catElec.Id, 18.00m, 9.50m),
            ("压力传感器", "YL-02", 3, catElec.Id, 42.00m, 26.00m),
            ("变频控制器", "BP-300", 1, catElec.Id, 680.00m, 520.00m),
            ("铜管组件", "TG-16", 3, catMetal.Id, 12.00m, 7.80m),
            ("减震橡胶垫", "JZ-08", 3, catMotor.Id, 2.20m, 0.95m),
            ("紧固螺栓M8", "LS-M8", 3, catMetal.Id, 0.80m, 0.35m),
            ("密封圈", "MF-30", 3, catMetal.Id, 1.50m, 0.60m)
        };
        var products = new List<Product>();
        var i2 = 0;
        foreach (var p in productNames)
        {
            i2++;
            products.Add(new Product
            {
                Code = $"P2026{i2:0000}", Name = p.Item1, Spec = p.Item2, ProductType = p.Item3 == 3 ? 3 : p.Item3,
                CategoryId = p.Item4, BrandId = brands[i2 % 3].Id, UnitId = units[0].Id,
                SalePrice = p.Item5, CostPrice = p.Item6, SafetyStock = p.Item3 == 3 ? 500 : 50,
                Barcode = $"6901234{i2:00000}"
            });
        }
        db.AddRange(products);
        await db.SaveChangesAsync();

        // ---------- BOM 与工艺路线 ----------
        var routing1 = new Routing { ProductId = products[0].Id, Version = "V1.0", IsDefault = true };
        var routing2 = new Routing { ProductId = products[6].Id, Version = "V1.0", IsDefault = true };
        var routing3 = new Routing { ProductId = products[13].Id, Version = "V1.0", IsDefault = true };
        db.AddRange(routing1, routing2, routing3);
        await db.SaveChangesAsync();
        db.AddRange(
            new RoutingStep { RoutingId = routing1.Id, Seq = 10, ProcessId = processes[0].Id, WorkshopId = workshops[0].Id, StandardHours = 0.5m, IsKey = false },
            new RoutingStep { RoutingId = routing1.Id, Seq = 20, ProcessId = processes[1].Id, WorkshopId = workshops[0].Id, StandardHours = 1.2m, IsKey = true },
            new RoutingStep { RoutingId = routing1.Id, Seq = 30, ProcessId = processes[4].Id, WorkshopId = workshops[1].Id, StandardHours = 2m, IsKey = true },
            new RoutingStep { RoutingId = routing1.Id, Seq = 40, ProcessId = processes[5].Id, WorkshopId = workshops[1].Id, StandardHours = 0.4m, IsKey = true },
            new RoutingStep { RoutingId = routing1.Id, Seq = 50, ProcessId = processes[6].Id, WorkshopId = workshops[2].Id, StandardHours = 0.3m, IsKey = false },
            new RoutingStep { RoutingId = routing2.Id, Seq = 10, ProcessId = processes[0].Id, WorkshopId = workshops[0].Id, StandardHours = 0.6m },
            new RoutingStep { RoutingId = routing2.Id, Seq = 20, ProcessId = processes[3].Id, WorkshopId = workshops[1].Id, StandardHours = 0.8m },
            new RoutingStep { RoutingId = routing2.Id, Seq = 30, ProcessId = processes[5].Id, WorkshopId = workshops[1].Id, StandardHours = 0.5m },
            new RoutingStep { RoutingId = routing3.Id, Seq = 10, ProcessId = processes[4].Id, WorkshopId = workshops[1].Id, StandardHours = 1.5m },
            new RoutingStep { RoutingId = routing3.Id, Seq = 20, ProcessId = processes[5].Id, WorkshopId = workshops[1].Id, StandardHours = 0.6m });

        var boms = new[] { products[0].Id, products[6].Id, products[13].Id }
            .Select((pid, i) => new Bom { ProductId = pid, Version = "V1.0", IsDefault = true, Name = products[i * 6].Name + "BOM" })
            .ToList();
        db.AddRange(boms);
        await db.SaveChangesAsync();
        foreach (var bom in boms)
        {
            db.AddRange(
                new BomLine { BomId = bom.Id, Seq = 1, MaterialId = products[1].Id, Qty = 1, UnitId = units[0].Id, LossRate = 1 },
                new BomLine { BomId = bom.Id, Seq = 2, MaterialId = products[2].Id, Qty = 1, UnitId = units[0].Id, LossRate = 1.5m },
                new BomLine { BomId = bom.Id, Seq = 3, MaterialId = products[9].Id, Qty = 2, UnitId = units[0].Id, LossRate = 2 },
                new BomLine { BomId = bom.Id, Seq = 4, MaterialId = products[16].Id, Qty = 8, UnitId = units[0].Id, LossRate = 3 });
        }

        var defects = new[] { ("外观粗糙", "外观"), ("尺寸超差", "尺寸"), ("表面毛刺较多", "外观"), ("锡膏涂覆不均", "工艺"), ("焊点虚焊", "焊接"), ("装配间隙过大", "装配"), ("功能测试失败", "功能") }
            .Select((d, i) => new DefectItem { Code = $"D{i + 1:000}", Name = d.Item1, Category = d.Item2 }).ToList();
        db.AddRange(defects);

        db.AddRange(new SysNumberRule { Code = "SO", Name = "销售订单", Prefix = "SO", DateFormat = "yyyyMMdd", SeqLength = 4, CurrentSeq = 8, Example = "SO202611030001" },
            new SysNumberRule { Code = "PO", Name = "采购订单", Prefix = "PO", DateFormat = "yyyyMMdd", SeqLength = 4, CurrentSeq = 6 },
            new SysNumberRule { Code = "PLAN", Name = "生产计划", Prefix = "PL", DateFormat = "yyyyMMdd", SeqLength = 3, CurrentSeq = 4 },
            new SysNumberRule { Code = "WO", Name = "生产工单", Prefix = "WO", DateFormat = "yyyyMMdd", SeqLength = 4, CurrentSeq = 10 },
            new SysNumberRule { Code = "BILL", Name = "出入库单", Prefix = "CK", DateFormat = "yyyyMMdd", SeqLength = 4, CurrentSeq = 12 },
            new SysNumberRule { Code = "QC", Name = "检验单", Prefix = "QC", DateFormat = "yyyyMMdd", SeqLength = 3, CurrentSeq = 10 },
            new SysNumberRule { Code = "REP", Name = "维修单", Prefix = "WX", DateFormat = "yyyyMMdd", SeqLength = 3, CurrentSeq = 3 },
            new SysNumberRule { Code = "EX", Name = "异常单", Prefix = "YC", DateFormat = "yyyyMMdd", SeqLength = 3, CurrentSeq = 8 },
            new SysNumberRule { Code = "TAKE", Name = "盘点单", Prefix = "PD", DateFormat = "yyyyMMdd", SeqLength = 3, CurrentSeq = 2 });

        db.AddRange(
            new SysConfig { Code = "company.name", Name = "企业名称", Value = "爱思智造有限公司", Group = "企业信息" },
            new SysConfig { Code = "company.address", Name = "企业地址", Value = "安徽省合肥市经济技术开发区繁华大道 100 号", Group = "企业信息" },
            new SysConfig { Code = "company.contact", Name = "联系电话", Value = "0551-66668888", Group = "企业信息" },
            new SysConfig { Code = "quality.aql", Name = "默认AQL", Value = "1.0", Group = "质量参数" },
            new SysConfig { Code = "production.autoIssue", Name = "工单下达自动生成领料单", Value = "false", Group = "生产参数" });

        await db.SaveChangesAsync();
        await SeedBusinessAsync(db, products, customers, suppliers, warehouses, workshops, processes, units, users, defects);
    }

    internal static void ValidateSeedPassword(string? password, string settingName)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12 ||
            !password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit))
            throw new InvalidOperationException($"{settingName} must contain at least 12 characters, including upper and lower case letters and a digit.");
    }
}
