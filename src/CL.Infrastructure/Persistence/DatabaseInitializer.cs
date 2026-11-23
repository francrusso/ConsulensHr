using CL.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CL.Infrastructure.Persistence;

/// <summary>
/// Applica le migrazioni, crea i ruoli e, se configurato in "Seed:Consulente", il primo utente consulente.
/// La password del primo utente va messa negli user-secrets, mai in appsettings.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InizializzaAsync(IServiceProvider services, bool applicaMigrazioni)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));

        var db = sp.GetRequiredService<AppDbContext>();
        if (db.Database.IsSqlite())
            await db.Database.EnsureCreatedAsync(); // modalità demo locale
        else if (applicaMigrazioni)
            await db.Database.MigrateAsync();

        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var esistenti = await roleManager.Roles.Select(r => r.Name).ToListAsync(); // una query invece di una per ruolo
        foreach (var ruolo in Ruoli.Tutti.Except(esistenti))
            await roleManager.CreateAsync(new IdentityRole(ruolo!));

        var config = sp.GetRequiredService<IConfiguration>();
        await SuperAmministratoreAsync(sp, config, logger);
        var email = config["Seed:Consulente:Email"];
        var password = config["Seed:Consulente:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;

        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        if (await userManager.FindByEmailAsync(email) is not null) return;

        var utente = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, Nome = "Amministratore" };
        var esito = await userManager.CreateAsync(utente, password);
        if (!esito.Succeeded)
        {
            logger.LogError("Creazione consulente iniziale fallita: {Errori}",
                string.Join("; ", esito.Errors.Select(e => e.Description)));
            return;
        }
        await userManager.AddToRoleAsync(utente, Ruoli.Consulente);
        logger.LogInformation("Creato il consulente iniziale {Email}", email);
    }

    /// <summary>
    /// Super amministratore da "Seed:Amministratore:Email" (user-secrets). Se l'utente esiste già riceve il ruolo;
    /// altrimenti si crea con "Seed:Amministratore:Password". Ha anche il ruolo consulente, così usa tutte le pagine dello studio.
    /// </summary>
    private static async Task SuperAmministratoreAsync(IServiceProvider sp, IConfiguration config, ILogger logger)
    {
        var email = config["Seed:Amministratore:Email"];
        if (string.IsNullOrWhiteSpace(email)) return;
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var utente = await userManager.FindByEmailAsync(email);
        if (utente is null)
        {
            var password = config["Seed:Amministratore:Password"];
            if (string.IsNullOrWhiteSpace(password)) { logger.LogWarning("Seed:Amministratore:Password mancante: super amministratore non creato."); return; }
            utente = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, Nome = "Super amministratore" };
            var esito = await userManager.CreateAsync(utente, password);
            if (!esito.Succeeded)
            {
                logger.LogError("Creazione super amministratore fallita: {Errori}", string.Join("; ", esito.Errors.Select(e => e.Description)));
                return;
            }
            logger.LogInformation("Creato il super amministratore {Email}", email);
        }
        foreach (var ruolo in new[] { Ruoli.Amministratore, Ruoli.Consulente })
            if (!await userManager.IsInRoleAsync(utente, ruolo)) await userManager.AddToRoleAsync(utente, ruolo);
    }
}
