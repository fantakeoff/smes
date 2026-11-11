namespace Smes.Infrastructure.Tenancy;

public interface ITenantContext
{
    long? TenantId { get; set; }
    string? TenantCode { get; set; }
}

/// <summary>Scoped mutable tenant context. Populate from verified JWT claims in the API pipeline.</summary>
public sealed class TenantContext : ITenantContext
{
    public long? TenantId { get; set; }
    public string? TenantCode { get; set; }
}
