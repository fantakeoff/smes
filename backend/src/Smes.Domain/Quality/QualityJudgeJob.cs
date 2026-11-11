using Smes.Domain.Common;

namespace Smes.Domain.Quality;

/// <summary>异步质检判定任务：0 queued / 1 processing / 2 completed / 3 dead-lettered.</summary>
public class QualityJudgeJob : BaseEntity
{
    public long InspectionId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public int Result { get; set; }
    public decimal QualifiedQty { get; set; }
    public decimal DefectQty { get; set; }
    public long? DefectItemId { get; set; }
    public long RequestedBy { get; set; }
    public string? JudgeRemark { get; set; }
    public int JobStatus { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid MessageId { get; set; }
}
