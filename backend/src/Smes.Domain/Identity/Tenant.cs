namespace Smes.Domain.Identity;

/// <summary>Tenant directory entry; intentionally not tenant-scoped itself.</summary>
public sealed class Tenant
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Status { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
