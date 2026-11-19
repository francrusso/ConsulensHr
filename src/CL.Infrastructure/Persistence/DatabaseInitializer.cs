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
        foreach (var ruolo in Ruoli.Tutti)
            if (!await roleManager.RoleExistsAsync(ruolo))
                await roleManager.CreateAsync(new IdentityRole(ruolo));

        var config = sp.GetRequiredService<IConfiguration>();
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
}
