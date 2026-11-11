using Smes.Domain.Common;

namespace Smes.Domain.MasterData;

public class Unit : BaseEntity
{
    /// <summary>1 计数 / 2 重量 / 3 长度 / 4 时间</summary>
    public int UnitType { get; set; } = 1;
    public int Precision { get; set; }
}

public class Warehouse : BaseEntity
{
    /// <summary>1 原料仓 / 2 半成品仓 / 3 成品仓 / 4 待检仓 / 5 不良品仓</summary>
    public int Type { get; set; } = 1;
    public string? Location { get; set; }
    public long? ManagerId { get; set; }
    public bool IsDefault { get; set; }
}

public class Customer : BaseEntity
{
    public string? ShortName { get; set; }
    public string? Contact { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public decimal CreditLimit { get; set; }
    /// <summary>1 普通 / 2 重要 / 3 VIP</summary>
    public int Level { get; set; } = 1;
    public long? SalesmanId { get; set; }
    public string? TaxNo { get; set; }
}

public class Supplier : BaseEntity
{
    public string? ShortName { get; set; }
    public string? Contact { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    /// <summary>1 原材料 / 2 外协 / 3 设备 / 4 服务</summary>
    public int SupplierType { get; set; } = 1;
    public string? TaxNo { get; set; }
    public int Level { get; set; } = 1;
}

public class Workshop : BaseEntity
{
    /// <summary>1 车间 / 2 产线 / 3 工作中心</summary>
    public int WorkshopType { get; set; } = 1;
    public long ParentId { get; set; }
    public string? Manager { get; set; }
    public int Sort { get; set; }
}

/// <summary>工序</summary>
public class Process : BaseEntity
{
    /// <summary>1 加工 / 2 装配 / 3 检验 / 4 包装</summary>
    public int ProcessType { get; set; } = 1;
    public decimal StandardHours { get; set; }
    public decimal Price { get; set; }
    public long? WorkshopId { get; set; }
    public bool IsOutsourced { get; set; }
}

/// <summary>工艺路线（BOM 之外的另一套主数据，按产品+版本管理）</summary>
public class Routing : BaseEntity
{
    public long ProductId { get; set; }
    public string Version { get; set; } = "V1.0";
    public DateTime EffectiveDate { get; set; } = DateTime.Today;
    public bool IsDefault { get; set; } = true;
    public List<RoutingStep> Steps { get; set; } = new();
}

public class RoutingStep : BaseEntity
{
    public long RoutingId { get; set; }
    public int Seq { get; set; }
    public long ProcessId { get; set; }
    public long? WorkshopId { get; set; }
    public decimal StandardHours { get; set; }
    public string? Description { get; set; }
    public bool IsKey { get; set; }
}

public class Bom : BaseEntity
{
    public long ProductId { get; set; }
    public string Version { get; set; } = "V1.0";
    public DateTime EffectiveDate { get; set; } = DateTime.Today;
    public bool IsDefault { get; set; } = true;
    public List<BomLine> Lines { get; set; } = new();
}

public class BomLine : BaseEntity
{
    public long BomId { get; set; }
    public int Seq { get; set; }
    public long MaterialId { get; set; }
    public decimal Qty { get; set; }
    public long? UnitId { get; set; }
    /// <summary>损耗率 %</summary>
    public decimal LossRate { get; set; }
    public string? Remark2 { get; set; }
}

public class DefectItem : BaseEntity
{
    public string? Category { get; set; }
    public string? Description { get; set; }
    public string? Disposal { get; set; }
}

public class ProductCategory : BaseEntity
{
    public long ParentId { get; set; }
    public int Sort { get; set; }
}

public class ProductBrand : BaseEntity
{
    public string? Logo { get; set; }
}

public class Product : BaseEntity
{
    public string? Spec { get; set; }
    public long? CategoryId { get; set; }
    public long? BrandId { get; set; }
    public long? UnitId { get; set; }
    /// <summary>1 成品 / 2 半成品 / 3 原材料 / 4 辅料</summary>
    public int ProductType { get; set; } = 1;
    public decimal SalePrice { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SafetyStock { get; set; }
    public string? Barcode { get; set; }
    public string? DrawingNo { get; set; }
    public string? ImageUrl { get; set; }
    public string? AttributeJson { get; set; }
}
