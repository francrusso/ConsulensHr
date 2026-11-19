using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CL.Infrastructure.Persistence;

/// <summary>Usata solo da "dotnet ef" per generare le migrazioni: non apre connessioni.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=consulenzalavoro",
                pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema))
            .Options);
}
