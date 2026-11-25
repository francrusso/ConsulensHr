using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CL.Domain.Aziende;
using CL.Domain.Dipendenti;
using CL.Domain.Documenti;
using CL.Domain.Organizzazione;
using CL.Infrastructure.Archivio;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CL.Tests.Web;

public sealed class AziendeWebTests : IDisposable
{
    private readonly AppFactory _app = new();
    public void Dispose() => _app.Dispose();

    private static async Task<string> Testo(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0, 0x1F, 0x15, 0xC4,
        0x89, 0, 0, 0, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0, 1, 0, 0, 5, 0, 1, 0x0D, 0x0A, 0x2D, 0xB4, 0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    private async Task<HttpClient> Studio()
    {
        await _app.CreaUtente("consulente@test.it", Ruoli.Consulente);
        var c = _app.CreateClient();
        await c.Accedi("consulente@test.it");
        return c;
    }

    private static async Task<HttpResponseMessage> Salva(HttpClient c, Dictionary<string, string> campi, (string Nome, byte[] Dati)? logo = null)
    {
        using var form = new MultipartFormDataContent { { new StringContent(await c.TokenDa("/Aziende/Nuova")), "__RequestVerificationToken" } };
        foreach (var (k, v) in campi) form.Add(new StringContent(v), k);
        if (logo is { } l)
        {
            var file = new ByteArrayContent(l.Dati);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "Logo", l.Nome);
        }
        return await c.PostAsync("/Aziende/Salva", form);
    }

    [Fact]
    public async Task Ricerca_per_partita_iva_compila_i_dati_del_registro()
    {
        var consulente = await Studio();
        async Task<JsonElement> Cerca(string piva) =>
            JsonDocument.Parse(await consulente.GetStringAsync($"/Aziende/CercaPartitaIva?partitaIva={piva}")).RootElement;

        var trovata = await Cerca("IT 01234567897");
        Assert.Equal("trovata", trovata.GetProperty("esito").GetString());
        Assert.Equal("Acme S.r.l.", trovata.GetProperty("ragioneSociale").GetString());
        Assert.Equal("20121", trovata.GetProperty("cap").GetString());
        Assert.Equal("MI", trovata.GetProperty("provincia").GetString());

        Assert.Equal("non-valida", (await Cerca("01234567890")).GetProperty("esito").GetString()); // cifra di controllo errata
        Assert.Equal("non-disponibile", (await Cerca("12345678903")).GetProperty("esito").GetString());

        await Salva(consulente, new() { ["RagioneSociale"] = "Acme S.r.l.", ["PartitaIva"] = "01234567897" });
        Assert.Equal("esistente", (await Cerca("01234567897")).GetProperty("esito").GetString());
    }

    [Fact]
    public async Task Controlli_formali_sui_campi_dell_azienda()
    {
        var consulente = await Studio();
        var validi = new Dictionary<string, string>
        {
            ["RagioneSociale"] = "Acme S.r.l.", ["PartitaIva"] = "01234567897", ["Indirizzo"] = "Via Roma 1", ["Cap"] = "20121",
            ["Comune"] = "Milano", ["Provincia"] = "MI", ["Email"] = "info@acme.it",
        };

        Assert.Contains("CAP di 5 cifre", await Testo(await Salva(consulente, new(validi) { ["Cap"] = "2012" })));
        Assert.Contains("La provincia \"XX\" non esiste", await Testo(await Salva(consulente, new(validi) { ["Provincia"] = "XX" })));
        Assert.Contains("Email non valida", await Testo(await Salva(consulente, new(validi) { ["Email"] = "info@acme" })));
        Assert.Contains("cifra di controllo errata", await Testo(await Salva(consulente, new(validi) { ["CodiceFiscale"] = "01234567890" })));
        Assert.Contains("controllo", await Testo(await Salva(consulente, new(validi) { ["CodiceFiscale"] = "RSSMRA85T10A562X" })));
        Assert.Contains("partita IVA", await Testo(await Salva(consulente, new(validi) { ["PartitaIva"] = "01234567890" })));
        Assert.Equal(0, await _app.ConDb(db => db.Aziende.CountAsync()));

        // Ditta individuale: codice fiscale personale di 16 caratteri accettato.
        var html = await Testo(await Salva(consulente, new(validi) { ["CodiceFiscale"] = "rssmra85t10a562s" }));
        Assert.Contains("Azienda Acme S.r.l. creata", html);
        Assert.Contains("P.IVA 01234567897 · Milano", html); // si arriva al profilo dell'azienda
        Assert.Equal("RSSMRA85T10A562S", (await _app.ConDb(db => db.Aziende.SingleAsync())).CodiceFiscale);
    }

    [Fact]
    public async Task Logo_caricato_visibile_allo_studio_e_alla_propria_azienda()
    {
        var consulente = await Studio();
        Assert.Contains("deve essere un'immagine",
            await Testo(await Salva(consulente, new() { ["RagioneSociale"] = "Acme S.r.l.", ["PartitaIva"] = "01234567897" }, ("logo.png", "<svg/>"u8.ToArray()))));

        var html = await Testo(await Salva(consulente, new() { ["RagioneSociale"] = "Acme S.r.l.", ["PartitaIva"] = "01234567897" }, ("logo.png", Png)));
        Assert.Contains("Azienda Acme S.r.l. creata", html);
        var acme = await _app.ConDb(db => db.Aziende.SingleAsync());
        Assert.Matches(@"^loghi/[0-9a-f-]+-[0-9a-f]{32}\.png$", acme.LogoChiave);
        Assert.Contains($"/Aziende/Logo/{acme.Id}", html);

        var logo = await consulente.GetAsync($"/Aziende/Logo/{acme.Id}");
        Assert.Equal("image/png", logo.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await logo.Content.ReadAsByteArrayAsync());

        var altra = await _app.ConDb(async db =>
        {
            var a = new Azienda("Altra S.p.A.", PartitaIva.Parse("12345678903"));
            db.Add(a);
            await db.SaveChangesAsync();
            return a.Id;
        });
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, acme.Id);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");
        Assert.Equal(HttpStatusCode.OK, (await hr.GetAsync($"/Aziende/Logo/{acme.Id}")).StatusCode);
        Assert.Contains($"/Aziende/Logo/{acme.Id}", await hr.GetStringAsync("/")); // nella barra in alto
        Assert.Equal(HttpStatusCode.NotFound, (await hr.GetAsync($"/Aziende/Logo/{altra}")).StatusCode);
    }

    /// <summary>Dipendente con accesso al portale e dati personali completi, già entrato.</summary>
    private async Task<HttpClient> Dipendente(Guid aziendaId, string email)
    {
        var dipId = await _app.ConDb(async db =>
        {
            var d = new CL.Domain.Dipendenti.Dipendente(aziendaId, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 12, 10));
            db.Add(d);
            await db.SaveChangesAsync();
            return d.Id;
        });
        var utente = await _app.CreaUtente(email, Ruoli.Dipendente, aziendaId);
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var u = await users.FindByIdAsync(utente.Id);
            u!.DipendenteId = dipId;
            await users.UpdateAsync(u);
        }
        await _app.CompletaDatiPersonali(dipId);
        var c = _app.CreateClient();
        await c.Accedi(email);
        return c;
    }

    [Fact]
    public async Task Il_dipendente_vede_in_alto_a_sinistra_il_marchio_della_sua_azienda()
    {
        var consulente = await Studio();
        await Salva(consulente, new() { ["RagioneSociale"] = "Acme S.r.l.", ["PartitaIva"] = "01234567897" }, ("logo.png", Png));
        var acme = await _app.ConDb(db => db.Aziende.SingleAsync());
        var beta = await _app.ConDb(async db =>
        {
            var a = new Azienda("Beta S.p.A.", PartitaIva.Parse("12345678903"));
            db.Add(a);
            await db.SaveChangesAsync();
            return a.Id;
        });
        const string MarchioConsulens = "/assets/images/logo-img.png";
        Assert.Contains(MarchioConsulens, await consulente.GetStringAsync("/"));

        // Con il logo: il logo dell'azienda, che il dipendente può scaricare.
        var dipAcme = await Dipendente(acme.Id, "mario@acme.test");
        var html = await dipAcme.GetStringAsync("/");
        Assert.Contains($"src=\"/Aziende/Logo/{acme.Id}\" class=\"logo-img\"", html);
        Assert.DoesNotContain(MarchioConsulens, html);
        Assert.Equal(HttpStatusCode.OK, (await dipAcme.GetAsync($"/Aziende/Logo/{acme.Id}")).StatusCode);

        // Senza logo: il nome dell'azienda.
        html = WebUtility.HtmlDecode(await (await Dipendente(beta, "anna@beta.test")).GetStringAsync("/"));
        Assert.Contains("id=\"logo-img\" title=\"Beta S.p.A.\">Beta S.p.A.</span>", html);
        Assert.DoesNotContain(MarchioConsulens, html);
    }

    [Fact]
    public async Task L_elenco_mostra_anche_le_disattivate_con_le_pillole_per_stato()
    {
        var consulente = await Studio();
        var (attiva, nuova, spenta) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Alfa S.r.l.", PartitaIva.Parse("01234567897"));
            var b = new Azienda("Beta S.r.l.", PartitaIva.Parse("12345678903"));
            var c = new Azienda("Gamma S.r.l.", PartitaIva.Parse("00743110157"));
            c.Disattiva();
            db.AddRange(a, b, c);
            await db.SaveChangesAsync();
            return (a.Id, b.Id, c.Id);
        });
        await _app.CompletaProfilo(attiva);

        var html = WebUtility.HtmlDecode(await consulente.GetStringAsync("/Aziende"));
        Assert.Contains("Alfa S.r.l.", html);
        Assert.Contains("Beta S.r.l.", html);
        Assert.Contains("Gamma S.r.l.", html); // disattivata, ma in elenco
        Assert.Contains("Tutte <span>(3)</span>", html);
        Assert.Contains("Disattivate <span>(1)</span>", html);

        html = WebUtility.HtmlDecode(await consulente.GetStringAsync("/Aziende?stato=disattivate"));
        Assert.Contains("Gamma S.r.l.", html);
        Assert.DoesNotContain("Alfa S.r.l.", html);
        html = WebUtility.HtmlDecode(await consulente.GetStringAsync("/Aziende?stato=da-completare"));
        Assert.Contains("Beta S.r.l.", html);
        Assert.DoesNotContain("Gamma S.r.l.", html);
    }

    [Fact]
    public async Task Un_azienda_disattivata_si_elimina_con_tutti_i_suoi_dati()
    {
        var consulente = await Studio();
        var (acme, altra, persona) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var b = new Azienda("Beta S.r.l.", PartitaIva.Parse("12345678903"));
            var d = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 12, 10));
            d.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(DateTime.Today.Year, 1, 1), null, 28_000m);
            var vendite = new Team(a.Id, "Vendite");
            var estero = new Team(a.Id, "Estero");
            estero.ImpostaPadre(vendite, [vendite, estero]);
            db.AddRange(a, b, d, vendite, estero);
            db.DocumentiStorici.Add(new DocumentoStorico(a.Id, TipoDocumentoStorico.Altro, 2025, "Bilancio", "bilancio.pdf", "application/pdf",
                "storico/acme-bilancio.pdf", 10, "test", DateTime.UtcNow));
            await db.SaveChangesAsync();
            return (a.Id, b.Id, d.Id);
        });
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, acme);
        var dipendente = await _app.CreaUtente("mario@acme.test", Ruoli.Dipendente);
        await _app.ConDb(async db =>
        {
            (await db.Users.SingleAsync(u => u.Id == dipendente.Id)).DipendenteId = persona;
            return await db.SaveChangesAsync();
        });
        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IArchivioDocumenti>().SalvaAsync("storico/acme-bilancio.pdf", new MemoryStream("%PDF"u8.ToArray()));

        Task<HttpResponseMessage> Elimina(string conferma) =>
            consulente.InviaForm($"/Onboarding?aziendaId={acme}", $"/Aziende/Elimina/{acme}", new() { ["conferma"] = conferma });

        // Un'azienda attiva non si elimina; il pulsante compare solo dopo la disattivazione.
        Assert.DoesNotContain("Elimina definitivamente", WebUtility.HtmlDecode(await consulente.GetStringAsync($"/Onboarding?aziendaId={acme}")));
        Assert.Contains("Prima disattiva l'azienda", await Testo(await Elimina("Acme S.r.l.")));
        await consulente.InviaForm($"/Onboarding?aziendaId={acme}", $"/Aziende/CambiaStato/{acme}", new());
        Assert.Contains("Elimina definitivamente", WebUtility.HtmlDecode(await consulente.GetStringAsync($"/Onboarding?aziendaId={acme}")));

        // Senza la ragione sociale giusta non succede nulla.
        Assert.Contains("scrivi esattamente la ragione sociale", await Testo(await Elimina("Acme")));
        Assert.True(await _app.ConDb(db => db.Aziende.AnyAsync(a => a.Id == acme)));

        var html = await Testo(await Elimina(" acme s.r.l. "));
        Assert.Contains("Acme S.r.l. è stata eliminata con tutti i suoi dati", html);
        await _app.ConDb(async db =>
        {
            Assert.False(await db.Aziende.AnyAsync(a => a.Id == acme));
            Assert.False(await db.Dipendenti.AnyAsync(d => d.Id == persona));
            Assert.False(await db.Contratti.AnyAsync(c => c.DipendenteId == persona));
            Assert.False(await db.Team.AnyAsync(t => t.AziendaId == acme));
            Assert.False(await db.DocumentiStorici.AnyAsync(d => d.AziendaId == acme));
            // Via gli accessi di referente e dipendente; il consulente e l'altra azienda restano.
            Assert.False(await db.Users.AnyAsync(u => u.Email == "hr@acme.test" || u.Email == "mario@acme.test"));
            Assert.True(await db.Users.AnyAsync(u => u.Email == "consulente@test.it"));
            Assert.True(await db.Aziende.AnyAsync(a => a.Id == altra));
            return 0;
        });
        using (var scope = _app.Services.CreateScope())
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IArchivioDocumenti>().ApriAsync("storico/acme-bilancio.pdf"));
    }
}
