using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CL.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' non configurata.");

        // PostgreSQL (oggi su Supabase). Usare la connessione diretta o il "session pooler" (porta 5432):
        // il "transaction pooler" (6543) non supporta i prepared statement usati da Npgsql.
        // "Data Source=file.db" = modalità demo locale su SQLite (solo per provare l'interfaccia, schema creato senza migrazioni).
        if (UsaSqliteDemo(connectionString))
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
        else
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString,
                pg => pg.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)
                       .MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema)));

        services.AddIdentity<ApplicationUser, IdentityRole>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.SignIn.RequireConfirmedAccount = false;
                o.Password.RequiredLength = 10;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<ClaimsFactory>();

        services.AddSingleton(TimeProvider.System);
        return services;
    }

    public static bool UsaSqliteDemo(string connectionString) =>
        connectionString.TrimStart().StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase);
}
