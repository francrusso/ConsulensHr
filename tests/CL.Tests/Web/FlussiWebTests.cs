using System.Net;
using CL.Domain.Aziende;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace CL.Tests.Web;

public sealed class FlussiWebTests : IDisposable
{
    private readonly AppFactory _app = new();

    public void Dispose() => _app.Dispose();

    private static async Task<string> Testo(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

    private HttpClient Client(bool redirect = true) =>
        _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = redirect });

    [Fact]
    public async Task Senza_login_si_viene_mandati_al_login()
    {
        var r = await Client(redirect: false).GetAsync("/Dipendenti");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.StartsWith("/Account/Login", r.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Password_errata_mostra_errore()
    {
        await _app.CreaUtente("consulente@studio.test", Ruoli.Consulente);
        var client = Client();
        var r = await client.InviaForm("/Account/Login", "/Account/Login",
            new() { ["Email"] = "consulente@studio.test", ["Password"] = "sbagliata-123" });
        Assert.Contains("Email o password non corretti", await Testo(r));
    }

    [Fact]
    public async Task Consulente_crea_azienda_dipendente_e_contratto()
    {
        await _app.CreaUtente("consulente@studio.test", Ruoli.Consulente);
        var client = Client();
        await client.Accedi("consulente@studio.test");

        // Azienda
        var r = await client.InviaForm("/Aziende/Nuova", "/Aziende/Salva", new()
        {
            ["RagioneSociale"] = "Acme S.r.l.", ["PartitaIva"] = "01234567897", ["Comune"] = "Pisa",
            ["PatronoGiorno"] = "17", ["PatronoMese"] = "6",
        });
        var html = await Testo(r);
        Assert.Contains("Azienda Acme S.r.l. creata", html);
        Assert.Contains("17 giugno", html);
        var aziendaId = await _app.ConDb(db => db.Aziende.Select(a => a.Id).SingleAsync());

        // Partita IVA duplicata
        r = await client.InviaForm("/Aziende/Nuova", "/Aziende/Salva", new() { ["RagioneSociale"] = "Doppia", ["PartitaIva"] = "01234567897" });
        Assert.Contains("Esiste già un'azienda con questa partita IVA", await Testo(r));

        // Dipendente: codice fiscale errato, poi corretto
        var campi = new Dictionary<string, string>
        {
            ["AziendaId"] = aziendaId.ToString(), ["Nome"] = "Mario", ["Cognome"] = "Rossi",
            ["CodiceFiscale"] = "RSSMRA85T10A562X", ["DataNascita"] = "1985-12-10",
            ["Iban"] = "IT60 X054 2811 1010 0000 0123 456",
        };
        r = await client.InviaForm("/Dipendenti/Nuovo", "/Dipendenti/Salva", campi);
        Assert.Contains("Carattere di controllo del codice fiscale non valido", await Testo(r));

        campi["CodiceFiscale"] = "rssmra85t10a562s";
        r = await client.InviaForm("/Dipendenti/Nuovo", "/Dipendenti/Salva", campi);
        html = await Testo(r);
        Assert.Contains("Ora registra il contratto di assunzione", html); // redirect alla pagina Assumi
        var dipId = await _app.ConDb(db => db.Dipendenti.Select(d => d.Id).SingleAsync());

        // Contratto part-time 50% con importi all'italiana
        r = await client.InviaForm($"/Dipendenti/Assumi/{dipId}", "/Dipendenti/Assumi", new()
        {
            ["DipendenteId"] = dipId.ToString(), ["Tipo"] = "TempoIndeterminato", ["DataInizio"] = "2026-01-01",
            ["Ccnl"] = "Commercio", ["Livello"] = "4", ["RetribuzioneLordaAnnua"] = "28.000,50", ["MensilitaAnnue"] = "14",
            ["OreSettimanaliFullTime"] = "40", ["PercentualePartTime"] = "50", ["GiorniLavorativiSettimana"] = "5",
            ["OreFerieAnnue"] = "160", ["OreRolAnnue"] = "72", ["OreExFestivitaAnnue"] = "32",
        });
        html = await Testo(r);
        Assert.Contains("Contratto registrato", html);
        Assert.Contains("In forza", html);
        Assert.Contains("Ferie 80 h", html);

        var contratto = await _app.ConDb(db => db.Contratti.SingleAsync());
        Assert.Equal(28000.50m, contratto.RetribuzioneLordaAnnua);

        // Elenco
        html = WebUtility.HtmlDecode(await client.GetStringAsync("/Dipendenti"));
        Assert.Contains("Rossi Mario", html);
        Assert.Contains("RSSMRA85T10A562S", html);
    }

    [Fact]
    public async Task Referente_azienda_vede_solo_i_propri_dipendenti()
    {
        var (mia, altra) = await _app.ConDb(async db =>
        {
            var a1 = new Azienda("Mia S.r.l.", PartitaIva.Parse("01234567897"));
            var a2 = new Azienda("Altra S.p.A.", PartitaIva.Parse("12345678903"));
            db.AddRange(a1, a2,
                new CL.Domain.Dipendenti.Dipendente(a1.Id, "Anna", "Bianchi", CodiceFiscale.Parse("VRDLGU90A01H501S"), new DateOnly(1990, 1, 1)),
                new CL.Domain.Dipendenti.Dipendente(a2.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 12, 10)));
            await db.SaveChangesAsync();
            return (a1.Id, a2.Id);
        });
        await _app.CreaUtente("hr@mia.test", Ruoli.Azienda, mia);
        var client = Client(redirect: false);
        await client.Accedi("hr@mia.test");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Dipendenti"));
        Assert.Contains("Bianchi Anna", html);
        Assert.DoesNotContain("Rossi Mario", html);

        var altroId = await _app.ConDb(db => db.Dipendenti.Where(d => d.AziendaId == altra).Select(d => d.Id).SingleAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Dipendenti/Dettaglio/{altroId}")).StatusCode);

        var aziende = await client.GetAsync("/Aziende");
        Assert.Equal(HttpStatusCode.Redirect, aziende.StatusCode);
        Assert.StartsWith("/Account/AccessoNegato", aziende.Headers.Location!.PathAndQuery);

        // Anche forzando l'azienda nel form, il dipendente finisce nella propria
        await client.InviaForm("/Dipendenti/Nuovo", "/Dipendenti/Salva", new()
        {
            ["AziendaId"] = altra.ToString(), ["Nome"] = "Luca", ["Cognome"] = "Verdi",
            ["CodiceFiscale"] = "RSSMRA85T10A562S", ["DataNascita"] = "1985-12-10",
        });
        var luca = await _app.ConDb(db => db.Dipendenti.SingleAsync(d => d.Cognome == "Verdi"));
        Assert.Equal(mia, luca.AziendaId);
    }
}
