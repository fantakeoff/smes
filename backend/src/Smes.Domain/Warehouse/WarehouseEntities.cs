using Smes.Domain.Common;

namespace Smes.Domain.Warehouse
{
    /// <summary>
    /// 出入库单。Type：1 采购入库 / 2 生产领料 / 3 生产入库 / 4 销售出库 / 5 其他入库 /
    /// 6 其他出库 / 7 调拨 / 8 盘盈 / 9 盘亏 / 10 采购退货 / 11 销售退货
    /// </summary>
    public class StockBill : BaseEntity
    {
        public int BillType { get; set; }
        public long WarehouseId { get; set; }
        /// <summary>调拨入库仓</summary>
        public long? ToWarehouseId { get; set; }
        public DateTime BillDate { get; set; } = DateTime.Today;
        /// <summary>0 草稿 / 1 已审核 / 2 已作废</summary>
        public int BillStatus { get; set; }
        public string? SourceType { get; set; }
        public long? SourceId { get; set; }
        public string? SourceCode { get; set; }
        public decimal TotalQty { get; set; }
        public decimal TotalAmount { get; set; }
        public long? OperatorId { get; set; }
        public DateTime? AuditAt { get; set; }
        public long? AuditBy { get; set; }
        public List<StockBillLine> Lines { get; set; } = new();
    }

    public class StockBillLine : BaseEntity
    {
        public long BillId { get; set; }
        public int Seq { get; set; }
        public long ProductId { get; set; }
        public decimal Qty { get; set; }
        public long? UnitId { get; set; }
        public decimal Price { get; set; }
        public decimal Amount { get; set; }
        public string? BatchNo { get; set; }
        public string? Location { get; set; }
    }

    public class InventoryStock : BaseEntity
    {
        public long ProductId { get; set; }
        public long WarehouseId { get; set; }
        public decimal Qty { get; set; }
        public decimal LockedQty { get; set; }
        public string? BatchNo { get; set; }
        public string? Location { get; set; }
        public decimal AvgCost { get; set; }
        public DateTime LastInAt { get; set; } = DateTime.Now;
    }

    /// <summary>库存流水（每次审核出入库单写入）</summary>
    public class InventoryTransaction : BaseEntity
    {
        public long? BillId { get; set; }
        public string? BillCode { get; set; }
        public int BillType { get; set; }
        public long ProductId { get; set; }
        public long WarehouseId { get; set; }
        public string? BatchNo { get; set; }
        public string? Location { get; set; }
        public string? IdempotencyKey { get; set; }
        /// <summary>1 入库 / -1 出库</summary>
        public int Direction { get; set; }
        public decimal Qty { get; set; }
        public decimal BeforeQty { get; set; }
        public decimal AfterQty { get; set; }
        public decimal Price { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.Now;
        public long? OperatorId { get; set; }
    }

    /// <summary>盘点单：0 草稿 / 1 盘点中 / 2 已完成 / 3 已作废</summary>
    public class StockTake : BaseEntity
    {
        public long WarehouseId { get; set; }
        public DateTime TakeDate { get; set; } = DateTime.Today;
        public int TakeStatus { get; set; }
        public decimal TotalDiffQty { get; set; }
        public long? OperatorId { get; set; }
        public List<StockTakeLine> Lines { get; set; } = new();
    }

    public class StockTakeLine : BaseEntity
    {
        public long TakeId { get; set; }
        public long ProductId { get; set; }
        public decimal BookQty { get; set; }
        public decimal ActualQty { get; set; }
        public decimal DiffQty { get; set; }
        public string? Location { get; set; }
    }
}
