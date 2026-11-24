using System.Net;
using System.Text.RegularExpressions;
using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CL.Tests.Web;

/// <summary>L'app web completa, in memoria, su SQLite: nessun accesso a Supabase.</summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    public const string PasswordTest = "Password-di-test-1";
    private readonly SqliteConnection _conn = new("DataSource=:memory:");

    /// <summary>Archivio dei PDF in una cartella temporanea, eliminata alla fine del test.</summary>
    public string CartellaArchivio { get; } = Path.Combine(Path.GetTempPath(), "clhr-test-" + Guid.NewGuid().ToString("N"));

    /// <summary>Errori registrati dal server durante il test (utile per capire un 500).</summary>
    public List<string> ErroriServer { get; } = [];

    public AppFactory() => _conn.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(l => l.AddProvider(new RaccoltaErrori(ErroriServer)));
        builder.UseSetting("ConnectionStrings:Default", "Host=non-usato");
        builder.UseSetting("Archivio:Percorso", CartellaArchivio);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_conn));
            // Nessuna chiamata a VIES durante i test.
            services.RemoveAll<CL.Infrastructure.Registro.IRicercaPartitaIva>();
            services.AddSingleton<CL.Infrastructure.Registro.IRicercaPartitaIva, RegistroFinto>();
            // Né a OpenStreetMap per i suggerimenti di indirizzo.
            services.RemoveAll<CL.Infrastructure.Indirizzi.IRicercaIndirizzi>();
            services.AddSingleton<CL.Infrastructure.Indirizzi.IRicercaIndirizzi, IndirizziFinti>();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        DatabaseInitializer.InizializzaAsync(host.Services, applicaMigrazioni: false).GetAwaiter().GetResult();
        return host;
    }

    /// <summary>
    /// Crea un utente con password <see cref="PasswordTest"/>. Per un referente segna anche il profilo
    /// dell'azienda come completo (salvo <paramref name="profiloCompleto"/> = false), altrimenti finirebbe nel percorso guidato.
    /// </summary>
    /// <param name="amministratore">Per un consulente: anche super amministratore (vede tutte le aziende). Falso = consulente
    /// con le sole aziende assegnate.</param>
    public async Task<ApplicationUser> CreaUtente(string email, string ruolo, Guid? aziendaId = null, bool profiloCompleto = true, bool amministratore = true)
    {
        if (ruolo == Ruoli.Azienda && aziendaId is { } az && profiloCompleto) await CompletaProfilo(az);
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var u = new ApplicationUser { UserName = email, Email = email, Nome = "Test", Cognome = ruolo, AziendaId = aziendaId };
        var r = await users.CreateAsync(u, PasswordTest);
        Assert.True(r.Succeeded, string.Join("; ", r.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(u, ruolo);
        if (ruolo == Ruoli.Consulente && amministratore) await users.AddToRoleAsync(u, Ruoli.Amministratore);
        return u;
    }

    /// <summary>Profilo azienda compilato con dati di prova validi (idempotente).</summary>
    public Task CompletaProfilo(Guid aziendaId) => ConDb(async db =>
    {
        if (await db.ProfiliAzienda.AnyAsync(p => p.AziendaId == aziendaId)) return 0;
        var p = new CL.Domain.Aziende.ProfiloAzienda(aziendaId);
        p.ImpostaDatiSocietari(new("S.r.l.", null, "47.11.10", null, "pec@test.it", null));
        p.ImpostaSedi(new("Via Roma 1", "20100", "Milano", "MI"), []);
        p.ImpostaLegaleRappresentante(new("Mario", "Rossi", CL.Domain.ValueObjects.CodiceFiscale.Parse("RSSMRA85T10A562S"), null, null));
        p.ImpostaPosizioni(new("Commercio", "1234567890", "12345678", "87654321", null, null, null, 5, 40));
        p.ImpostaPaghe(new(null, 27, null));
        p.Completa(true, "test", DateTime.UtcNow);
        db.ProfiliAzienda.Add(p);
        return await db.SaveChangesAsync();
    });

    /// <summary>Dati personali del dipendente compilati (altrimenti al login finisce nel percorso guidato).</summary>
    public Task CompletaDatiPersonali(Guid dipendenteId) => ConDb(async db =>
    {
        if (await db.ProfiliDipendente.AnyAsync(p => p.DipendenteId == dipendenteId)) return 0;
        var p = new CL.Domain.Dipendenti.ProfiloDipendente(dipendenteId);
        var t = DateTime.UtcNow;
        p.ImpostaAnagrafica(new("Milano", "MI", "Italiana", null, null), "test", t);
        p.ImpostaResidenza(new("Via Roma 1", "20100", "Milano", "MI"), null, "test", t);
        p.ImpostaPagamento(CL.Domain.ValueObjects.Iban.Parse("IT60X0542811101000000123456"), null, CL.Domain.Dipendenti.DestinazioneTfr.Azienda, null, "test", t);
        p.ImpostaFamiglia(true, [], "test", t);
        p.ImpostaAltriDati(new(CL.Domain.Dipendenti.TitoloStudio.Diploma, "Carta d'identità", "CA1234567", DateOnly.FromDateTime(t).AddYears(5), null), "test", t);
        p.Completa(true, "test", t);
        db.ProfiliDipendente.Add(p);
        return await db.SaveChangesAsync();
    });

    public async Task<T> ConDb<T>(Func<AppDbContext, Task<T>> f)
    {
        using var scope = Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _conn.Dispose();
            if (Directory.Exists(CartellaArchivio)) Directory.Delete(CartellaArchivio, recursive: true);
        }
    }
}

internal sealed class RaccoltaErrori(List<string> errori) : ILoggerProvider, ILogger
{
    public ILogger CreateLogger(string categoria) => this;
    public void Dispose() { }
    public IDisposable? BeginScope<T>(T state) where T : notnull => null;
    public bool IsEnabled(LogLevel livello) => livello >= LogLevel.Error;
    public void Log<T>(LogLevel livello, EventId id, T state, Exception? ex, Func<T, Exception?, string> formatta)
    {
        if (!IsEnabled(livello)) return;
        lock (errori) errori.Add($"{formatta(state, ex)} {ex}");
    }
}

internal static partial class HttpTestExtensions
{
    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex Token();

    public static async Task<string> TokenDa(this HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        return Token().Match(html).Groups[1].Value;
    }

    /// <summary>Invia un form come farebbe il browser (token antiforgery incluso).</summary>
    public static async Task<HttpResponseMessage> InviaForm(this HttpClient client, string paginaForm, string azione,
        Dictionary<string, string> campi)
    {
        campi["__RequestVerificationToken"] = await client.TokenDa(paginaForm);
        return await client.PostAsync(azione, new FormUrlEncodedContent(campi));
    }

    public static async Task Accedi(this HttpClient client, string email)
    {
        var r = await client.InviaForm("/Account/Login", "/Account/Login",
            new() { ["Email"] = email, ["Password"] = AppFactory.PasswordTest });
        // Login riuscito = redirect alla panoramica (seguito o no, a seconda del client).
        if (r.StatusCode == HttpStatusCode.Redirect) return;
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.DoesNotContain("Email o password non corretti", await r.Content.ReadAsStringAsync());
    }
}

/// <summary>Registro imprese finto: conosce solo la partita IVA 01234567897; 12345678903 simula il servizio fuori uso.</summary>
internal sealed class RegistroFinto : CL.Infrastructure.Registro.IRicercaPartitaIva
{
    public Task<CL.Infrastructure.Registro.RisultatoRicerca> CercaAsync(CL.Domain.ValueObjects.PartitaIva p, CancellationToken ct = default) =>
        Task.FromResult(p.Valore switch
        {
            "01234567897" => new CL.Infrastructure.Registro.RisultatoRicerca(CL.Infrastructure.Registro.EsitoRicerca.Trovata,
                new CL.Infrastructure.Registro.DatiImpresa("Acme S.r.l.", "Via Roma 1", "20121", "Milano", "MI")),
            "12345678903" => new CL.Infrastructure.Registro.RisultatoRicerca(CL.Infrastructure.Registro.EsitoRicerca.ServizioNonDisponibile),
            _ => new CL.Infrastructure.Registro.RisultatoRicerca(CL.Infrastructure.Registro.EsitoRicerca.NonTrovata),
        });
}

/// <summary>Suggerimenti di indirizzo finti: un solo indirizzo noto, "Via Roma 10" a Milano.</summary>
internal sealed class IndirizziFinti : CL.Infrastructure.Indirizzi.IRicercaIndirizzi
{
    public Task<IReadOnlyList<CL.Infrastructure.Indirizzi.IndirizzoTrovato>> CercaAsync(string testo, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CL.Infrastructure.Indirizzi.IndirizzoTrovato>>(testo.Contains("roma", StringComparison.OrdinalIgnoreCase)
            ? [new("Via Roma 10, 20121 Milano (MI)", "Via Roma 10", "20121", "Milano", "MI")]
            : []);
}
