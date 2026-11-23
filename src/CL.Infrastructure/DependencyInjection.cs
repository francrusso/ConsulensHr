using CL.Infrastructure.Archivio;
using CL.Infrastructure.Email;
using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using CL.Infrastructure.Registro;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CL.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<PerimetroAziende>();
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' non configurata.");

        // PostgreSQL (oggi su Supabase). Usare la connessione diretta o il "session pooler" (porta 5432):
        // il "transaction pooler" (6543) non supporta i prepared statement usati da Npgsql.
        // "Data Source=file.db" = modalità demo locale su SQLite (solo per provare l'interfaccia, schema creato senza migrazioni).
        if (UsaSqliteDemo(connectionString))
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
        else
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(ConnessioneRobusta(connectionString),
                pg => pg.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)
                       .MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema)
                       // Errori di rete momentanei (connessione chiusa dal pooler, riavvio di Supabase): si riprova da soli.
                       .EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null)));

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

        // Link di invito e di recupero password validi 3 giorni.
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromDays(3));
        // Utente disattivato o password cambiata: le sessioni aperte decadono entro 5 minuti.
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));

        services.Configure<OpzioniEmail>(configuration.GetSection("Email"));
        services.AddSingleton<IInvioEmail, InvioEmailSmtp>();
        services.AddSingleton<CodaEmail>();
        services.AddHostedService<InvioCodaEmail>();

        services.Configure<OpzioniArchivio>(configuration.GetSection("Archivio"));
        services.AddSingleton<IArchivioDocumenti, ArchivioSuDisco>();

        services.AddHttpClient<IRicercaPartitaIva, RicercaVies>(c =>
        {
            c.BaseAddress = new Uri(RicercaVies.IndirizzoServizio);
            c.Timeout = TimeSpan.FromSeconds(10);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("ConsulensHR/1.0");
        });

        services.AddHttpClient<CL.Infrastructure.Indirizzi.IRicercaIndirizzi, CL.Infrastructure.Indirizzi.RicercaPhoton>(c =>
        {
            c.BaseAddress = new Uri(CL.Infrastructure.Indirizzi.RicercaPhoton.IndirizzoServizio);
            c.Timeout = TimeSpan.FromSeconds(6);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("ConsulensHR/1.0");
        });

        services.AddSingleton(TimeProvider.System);
        return services;
    }

    /// <summary>
    /// Il pooler di Supabase chiude le connessioni rimaste ferme: senza accorgimenti la prima richiesta dopo una pausa
    /// trova la connessione già chiusa ("Connection reset by peer"). Le connessioni inattive si chiudono prima (60 s),
    /// quelle aperte si tengono vive e si rinnovano ogni 10 minuti. I valori scritti nella stringa di connessione restano.
    /// </summary>
    internal static string ConnessioneRobusta(string connectionString)
    {
        var b = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        var scritti = connectionString.ToLowerInvariant();
        if (!scritti.Contains("idle lifetime")) b.ConnectionIdleLifetime = 60;
        if (!scritti.Contains("keepalive")) b.KeepAlive = 30;
        if (!scritti.Contains("connection lifetime")) b.ConnectionLifetime = 600;
        return b.ConnectionString;
    }

    public static bool UsaSqliteDemo(string connectionString) =>
        connectionString.TrimStart().StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase);
}
