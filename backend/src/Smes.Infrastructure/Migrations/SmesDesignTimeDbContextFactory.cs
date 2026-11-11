using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Smes.Infrastructure.Tenancy;

namespace Smes.Infrastructure.Migrations;

/// <summary>
/// Keeps EF tooling from booting Program.cs or touching the application's configured database.
/// Set SMES_MIGRATIONS_CONNECTION_STRING explicitly only for an isolated migration target.
/// </summary>
public sealed class SmesDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SmesDbContext>
{
    public SmesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SMES_MIGRATIONS_CONNECTION_STRING")
            ?? "Server=127.0.0.1;Port=3306;Database=smes_migration_design_only;User ID=design;Password=design;CharSet=utf8mb4";

        var options = new DbContextOptionsBuilder<SmesDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 4, 0)))
            .Options;

        return new SmesDbContext(options, new TenantContext());
    }
}
