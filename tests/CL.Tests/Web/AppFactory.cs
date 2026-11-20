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

    public async Task<ApplicationUser> CreaUtente(string email, string ruolo, Guid? aziendaId = null)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var u = new ApplicationUser { UserName = email, Email = email, Nome = "Test", Cognome = ruolo, AziendaId = aziendaId };
        var r = await users.CreateAsync(u, PasswordTest);
        Assert.True(r.Succeeded, string.Join("; ", r.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(u, ruolo);
        return u;
    }

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
