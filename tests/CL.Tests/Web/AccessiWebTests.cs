using System.Net;
using System.Text.RegularExpressions;
using CL.Domain.Aziende;
using CL.Domain.Dipendenti;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CL.Tests.Web;

/// <summary>Inviti, attivazione, disattivazione e password. Nei test l'email non è configurata: il link compare a schermo.</summary>
public sealed partial class AccessiWebTests : IDisposable
{
    private const string NuovaPassword = "Nuova-Password-2026";
    private readonly AppFactory _app = new();

    public void Dispose() => _app.Dispose();

    [GeneratedRegex("id=\"linkInvito\" value=\"([^\"]+)\"")]
    private static partial Regex LinkInvito();

    private static async Task<string> Testo(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

    private static string Link(string html)
    {
        var m = LinkInvito().Match(html);
        Assert.True(m.Success, "Link di invito non mostrato");
        return new Uri(m.Groups[1].Value).PathAndQuery;
    }

    private async Task<(Guid Azienda, Guid Dipendente)> Dati(string? emailDipendente = "mario.rossi@acme.test", string piva = "01234567897")
    {
        return await _app.ConDb(async db =>
        {
            var a = new Azienda($"Azienda {piva}", PartitaIva.Parse(piva));
            var d = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 12, 10));
            d.AggiornaRecapiti(emailDipendente, null);
            db.AddRange(a, d);
            await db.SaveChangesAsync();
            return (a.Id, d.Id);
        });
    }

    private async Task<HttpClient> Consulente()
    {
        await _app.CreaUtente("consulente@test.it", Ruoli.Consulente);
        var c = _app.CreateClient();
        await c.Accedi("consulente@test.it");
        return c;
    }

    /// <summary>Apre il link e sceglie la password: restituisce il client già autenticato.</summary>
    private async Task<(HttpClient Client, string Html)> Attiva(string link)
    {
        var c = _app.CreateClient();
        var pagina = WebUtility.HtmlDecode(await c.GetStringAsync(link));
        var r = await c.InviaForm(link, "/Account/ImpostaPassword", new()
        {
            ["Uid"] = Regex.Match(pagina, "name=\"Uid\" value=\"([^\"]+)\"").Groups[1].Value,
            ["Token"] = Regex.Match(pagina, "name=\"Token\" value=\"([^\"]+)\"").Groups[1].Value,
            ["Password"] = NuovaPassword, ["Conferma"] = NuovaPassword,
        });
        return (c, await Testo(r));
    }

    [Fact]
    public async Task Consulente_invita_referente_che_attiva_e_vede_solo_la_sua_azienda()
    {
        var (aziendaId, _) = await Dati();
        await Dati(null, "12345678903"); // seconda azienda, non deve vederla
        var consulente = await Consulente();
        await _app.CompletaProfilo(aziendaId); // il percorso guidato ha i suoi test

        var r = await consulente.InviaForm($"/Aziende/Dettaglio/{aziendaId}", "/Utenti/InvitaReferente", new()
        {
            ["aziendaId"] = aziendaId.ToString(), ["Nome"] = "Laura", ["Cognome"] = "Bianchi", ["Email"] = "laura@acme.test",
        });
        var html = await Testo(r);
        Assert.Contains("Accesso creato per laura@acme.test", html);
        Assert.Contains("Invito in attesa", html);

        var (referente, home) = await Attiva(Link(html));
        Assert.Contains("il tuo accesso è attivo", home);
        Assert.Contains("Referente azienda", home);

        var dipendenti = WebUtility.HtmlDecode(await referente.GetStringAsync("/Dipendenti?tutti=true"));
        Assert.Contains("Rossi Mario", dipendenti);
        Assert.DoesNotContain("Azienda 12345678903", dipendenti);

        // Il link è monouso
        var riuso = WebUtility.HtmlDecode(await _app.CreateClient().GetStringAsync(Link(html)));
        Assert.Contains("Link non valido", riuso);
    }

    [Fact]
    public async Task Referente_invita_dipendente_collegato_alla_sua_scheda()
    {
        var (aziendaId, dipId) = await Dati();
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, aziendaId);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");

        var r = await hr.InviaForm($"/Dipendenti/Dettaglio/{dipId}", "/Utenti/InvitaDipendente", new() { ["dipendenteId"] = dipId.ToString() });
        var html = await Testo(r);
        Assert.Contains("Accesso creato per mario.rossi@acme.test", html);
        await _app.CompletaDatiPersonali(dipId); // il percorso guidato del dipendente ha i suoi test

        var (dip, home) = await Attiva(Link(html));
        Assert.Contains("Dipendente", home);
        var ferie = WebUtility.HtmlDecode(await dip.GetStringAsync("/Assenze"));
        Assert.Contains("Richieste", ferie);

        var (userId, collegato) = await _app.ConDb(async db =>
        {
            var u = await db.Users.SingleAsync(x => x.Email == "mario.rossi@acme.test");
            var d = await db.Dipendenti.SingleAsync(x => x.Id == dipId);
            return (u.Id, d.UserId);
        });
        Assert.Equal(userId, collegato);

        // Secondo invito per lo stesso dipendente: rifiutato
        r = await hr.InviaForm($"/Dipendenti/Dettaglio/{dipId}", "/Utenti/InvitaDipendente", new() { ["dipendenteId"] = dipId.ToString() });
        Assert.Contains("ha già un accesso", await Testo(r));
    }

    [Fact]
    public async Task Dipendente_senza_email_non_si_puo_invitare()
    {
        var (_, dipId) = await Dati(emailDipendente: null);
        var consulente = await Consulente();
        var r = await consulente.InviaForm($"/Dipendenti/Dettaglio/{dipId}", "/Utenti/InvitaDipendente", new() { ["dipendenteId"] = dipId.ToString() });
        Assert.Contains("serve la sua email", await Testo(r));
        Assert.False(await _app.ConDb(db => db.Users.AnyAsync(u => u.DipendenteId == dipId)));
    }

    [Fact]
    public async Task Utente_disattivato_non_entra_e_il_suo_link_non_vale()
    {
        var (aziendaId, _) = await Dati();
        var consulente = await Consulente();
        var html = await Testo(await consulente.InviaForm("/Utenti", "/Utenti/InvitaReferente", new()
        {
            ["aziendaId"] = aziendaId.ToString(), ["Nome"] = "Laura", ["Cognome"] = "Bianchi", ["Email"] = "laura@acme.test",
        }));
        var link = Link(html);
        var id = await _app.ConDb(db => db.Users.Where(u => u.Email == "laura@acme.test").Select(u => u.Id).SingleAsync());

        var r = await consulente.InviaForm("/Utenti", $"/Utenti/CambiaStato/{id}", new() { ["ritorno"] = "/Utenti" });
        Assert.Contains("disattivato", await Testo(r));
        Assert.Contains("Link non valido", WebUtility.HtmlDecode(await _app.CreateClient().GetStringAsync(link)));

        // Il consulente non può disattivare sé stesso
        var mio = await _app.ConDb(db => db.Users.Where(u => u.Email == "consulente@test.it").Select(u => u.Id).SingleAsync());
        r = await consulente.InviaForm("/Utenti", $"/Utenti/CambiaStato/{mio}", new() { ["ritorno"] = "/Utenti" });
        Assert.Contains("Non puoi disattivare il tuo stesso accesso", await Testo(r));
    }

    [Fact]
    public async Task Referente_non_gestisce_accessi_di_altre_aziende()
    {
        var (aziendaA, _) = await Dati();
        var (aziendaB, _) = await Dati(null, "12345678903");
        await _app.CreaUtente("hr@a.test", Ruoli.Azienda, aziendaA);
        var altro = await _app.CreaUtente("hr@b.test", Ruoli.Azienda, aziendaB);
        var hr = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await hr.Accedi("hr@a.test");

        var r = await hr.InviaForm("/Dipendenti", $"/Utenti/CambiaStato/{altro.Id}", new());
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);

        r = await hr.InviaForm("/Dipendenti", "/Utenti/InvitaReferente", new()
        {
            ["aziendaId"] = aziendaB.ToString(), ["Nome"] = "X", ["Cognome"] = "Y", ["Email"] = "x@b.test",
        });
        Assert.Contains("AccessoNegato", r.Headers.Location?.ToString()); // Forbid → pagina di accesso negato
        Assert.False(await _app.ConDb(db => db.Users.AnyAsync(u => u.Email == "x@b.test")));

        // La pagina utenti del consulente è solo per i consulenti
        r = await hr.GetAsync("/Utenti");
        Assert.Contains("AccessoNegato", r.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Email_gia_usata_non_crea_un_secondo_accesso()
    {
        var (aziendaId, _) = await Dati();
        var consulente = await Consulente();
        var r = await consulente.InviaForm("/Utenti", "/Utenti/InvitaReferente", new()
        {
            ["aziendaId"] = aziendaId.ToString(), ["Nome"] = "Studio", ["Cognome"] = "Doppio", ["Email"] = "consulente@test.it",
        });
        Assert.Contains("Esiste già un accesso", await Testo(r));
    }

    [Fact]
    public async Task Password_dimenticata_non_rivela_se_l_email_esiste()
    {
        await _app.CreaUtente("consulente@test.it", Ruoli.Consulente);
        var c = _app.CreateClient();
        var esistente = await Testo(await c.InviaForm("/Account/PasswordDimenticata", "/Account/PasswordDimenticata", new() { ["Email"] = "consulente@test.it" }));
        var inesistente = await Testo(await c.InviaForm("/Account/PasswordDimenticata", "/Account/PasswordDimenticata", new() { ["Email"] = "nessuno@test.it" }));
        const string Risposta = "corrisponde a un accesso attivo, riceverai a breve";
        Assert.Contains(Risposta, esistente);
        Assert.Contains(Risposta, inesistente);

        Assert.Contains("Link non valido", WebUtility.HtmlDecode(await c.GetStringAsync("/Account/ImpostaPassword?uid=x&token=abc")));
    }

    [Fact]
    public async Task Profilo_cambia_password_solo_con_quella_attuale()
    {
        var consulente = await Consulente();
        var r = await consulente.InviaForm("/Account/Profilo", "/Account/Profilo", new()
        {
            ["PasswordAttuale"] = "sbagliata-123", ["NuovaPassword"] = NuovaPassword, ["Conferma"] = NuovaPassword,
        });
        Assert.Contains("La password attuale non è corretta", await Testo(r));

        r = await consulente.InviaForm("/Account/Profilo", "/Account/Profilo", new()
        {
            ["PasswordAttuale"] = AppFactory.PasswordTest, ["NuovaPassword"] = NuovaPassword, ["Conferma"] = NuovaPassword,
        });
        Assert.Contains("Password aggiornata", await Testo(r));

        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await users.CheckPasswordAsync((await users.FindByEmailAsync("consulente@test.it"))!, NuovaPassword));
    }
}
