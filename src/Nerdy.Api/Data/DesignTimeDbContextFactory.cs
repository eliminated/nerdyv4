using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nerdy.Api.Data;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the SQL Server model without a running app or database.
/// Migrations target SQL Server only; SQLite (development, tests) uses EnsureCreated.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NerdyDbContext>
{
    public NerdyDbContext CreateDbContext(string[] args)
    {
        // Never opened by "migrations add"; "database update" needs a real connection string via --connection.
        var options = new DbContextOptionsBuilder<NerdyDbContext>()
            .UseSqlServer("Server=localhost,1433;Database=nerdy;Integrated Security=true;TrustServerCertificate=true")
            .Options;
        return new NerdyDbContext(options);
    }
}
