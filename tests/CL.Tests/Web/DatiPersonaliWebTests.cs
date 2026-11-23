using System.Net;
using CL.Domain.Aziende;
using CL.Domain.Common;
using CL.Domain.Dipendenti;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CL.Tests.Web;

public sealed class DatiPersonaliWebTests : IDisposable
{
    private readonly AppFactory _app = new();
    private Guid _aziendaId, _dipId;

    public void Dispose() => _app.Dispose();

    private static async Task<string> Testo(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

    private async Task Dati()
    {
        (_aziendaId, _dipId) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var d = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 12, 10));
            d.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(DateTime.Today.Year, 1, 1), null, 28_000m);
            db.AddRange(a, d);
            await db.SaveChangesAsync();
            return (a.Id, d.Id);
        });
    }

    private async Task<HttpClient> DipendenteNuovo()
    {
        await Dati();
        var u = await _app.CreaUtente("mario@acme.test", Ruoli.Dipendente);
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var x = (await users.FindByIdAsync(u.Id))!;
            x.DipendenteId = _dipId;
            x.AziendaId = _aziendaId;
            await users.UpdateAsync(x);
        }
        var c = _app.CreateClient();
        await c.Accedi("mario@acme.test");
        return c;
    }

    private static Task<HttpResponseMessage> Passo(HttpClient c, string azione, Dictionary<string, string> campi, string pagina = "/DatiPersonali") =>
        c.InviaForm(pagina, $"/DatiPersonali/{azione}", campi);

    private static readonly Dictionary<string, string> Anagrafica = new()
    {
        ["ComuneNascita"] = "Napoli", ["ProvinciaNascita"] = "na", ["Cittadinanza"] = "Italiana", ["Telefono"] = "333 1234567",
    };
    private static readonly Dictionary<string, string> Residenza = new()
    {
        ["Via"] = "Via Toledo 10", ["Cap"] = "80134", ["Comune"] = "Napoli", ["Provincia"] = "NA", ["DomicilioDiverso"] = "false",
    };
    private static readonly Dictionary<string, string> Pagamento = new()
    {
        ["Iban"] = "IT60 X054 2811 1010 0000 0123 456", ["Tfr"] = "FondoPensione", ["FondoPensione"] = "Fon.Te.",
    };
    private static Dictionary<string, string> Famiglia(string cfFiglio = "RSSLRA15A41F839L") => new()
    {
        ["RichiedeDetrazioni"] = "true",
        ["Familiari[0].Relazione"] = "Coniuge", ["Familiari[0].Nome"] = "Anna", ["Familiari[0].Cognome"] = "Verdi",
        ["Familiari[0].CodiceFiscale"] = "VRDNNA88C41F839V", ["Familiari[0].DataNascita"] = "1988-03-01", ["Familiari[0].PercentualeCarico"] = "100",
        ["Familiari[1].Relazione"] = "Figlio", ["Familiari[1].Nome"] = "Laura", ["Familiari[1].Cognome"] = "Rossi",
        ["Familiari[1].CodiceFiscale"] = cfFiglio, ["Familiari[1].DataNascita"] = "2015-01-01", ["Familiari[1].PercentualeCarico"] = "50",
        ["Familiari[2].Relazione"] = "Figlio", ["Familiari[2].PercentualeCarico"] = "100", // riga vuota: ignorata
    };
    private static readonly Dictionary<string, string> Altri = new()
    {
        ["TitoloStudio"] = "Diploma", ["TipoDocumento"] = "Carta d'identità", ["NumeroDocumento"] = "ca1234567",
        ["ScadenzaDocumento"] = DateTime.Today.AddYears(3).ToString("yyyy-MM-dd"),
    };

    [Fact]
    public async Task Dipendente_al_primo_accesso_compila_i_dati_per_le_paghe()
    {
        var mario = await DipendenteNuovo();

        var html = WebUtility.HtmlDecode(await mario.GetStringAsync("/Assenze"));
        Assert.Contains("1. Dati anagrafici", html);
        Assert.Contains("I miei dati", html);
        Assert.DoesNotContain(">Presenze e assenze</div>", html);

        Assert.Contains("2. Residenza", await Testo(await Passo(mario, "Anagrafica", new(Anagrafica))));
        Assert.Contains("3. Stipendio e TFR", await Testo(await Passo(mario, "Residenza", new(Residenza))));

        // Fondo pensione scelto ma non indicato.
        html = await Testo(await Passo(mario, "Pagamento", new(Pagamento) { ["FondoPensione"] = "" }));
        Assert.Contains("Indica il fondo pensione", html);
        Assert.Contains("4. Detrazioni e familiari", await Testo(await Passo(mario, "Pagamento", new(Pagamento))));

        // Codice fiscale del figlio sbagliato: resta sul passo.
        html = await Testo(await Passo(mario, "Famiglia", Famiglia(cfFiglio: "XYZ")));
        Assert.Contains("4. Detrazioni e familiari", html);
        Assert.Contains("5. Altri dati", await Testo(await Passo(mario, "Famiglia", Famiglia())));
        html = await Testo(await Passo(mario, "AltriDati", new(Altri)));
        Assert.Contains("6. Riepilogo", html);
        Assert.Contains("Figlio/a Laura Rossi (01/01/2015, 50%)", html);
        Assert.Contains("fondo pensione Fon.Te.", html);

        Assert.Contains("dichiarazione di veridicità", await Testo(await Passo(mario, "Conferma", new())));
        html = await Testo(await Passo(mario, "Conferma", new() { ["dichiarazione"] = "true" }));
        Assert.Contains("I tuoi dati sono stati inviati al consulente", html);

        // Portale libero; IBAN, telefono e residenza arrivati all'anagrafica usata dal consulente.
        var assenze = await mario.GetAsync("/Assenze");
        Assert.Equal("/Assenze", assenze.RequestMessage!.RequestUri!.AbsolutePath);
        var (dip, profilo) = await _app.ConDb(async db => (await db.Dipendenti.SingleAsync(), await db.ProfiliDipendente.SingleAsync()));
        Assert.Equal("IT60X0542811101000000123456", dip.Iban!.Valore);
        Assert.Equal("333 1234567", dip.Telefono);
        Assert.Equal("Via Toledo 10, 80134 Napoli (NA)", dip.IndirizzoCompleto);
        Assert.Equal(2, profilo.Familiari.Count);
        Assert.Equal("NA", profilo.ProvinciaNascita);

        // Modifica successiva: il consulente viene avvisato.
        html = await Testo(await Passo(mario, "Residenza", new(Residenza) { ["Via"] = "Via Chiaia 3" }, "/DatiPersonali?passo=2"));
        Assert.Contains("il consulente è stato avvisato", html);
        Assert.Empty(_app.ErroriServer);
    }

    [Fact]
    public async Task Referente_vede_i_dati_dei_propri_dipendenti_ma_non_di_altre_aziende()
    {
        await Dati();
        await _app.CompletaDatiPersonali(_dipId);
        var altra = await _app.ConDb(async db =>
        {
            var a = new Azienda("Altra S.p.A.", PartitaIva.Parse("12345678903"));
            db.Add(a);
            await db.SaveChangesAsync();
            return a.Id;
        });

        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, _aziendaId);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");
        var html = WebUtility.HtmlDecode(await hr.GetStringAsync($"/DatiPersonali?dipendenteId={_dipId}&passo=3")); // scheda "Stipendio e TFR"
        Assert.Contains("Dati personali di Rossi Mario", html);
        Assert.Contains("IT60X0542811101000000123456", html);
        Assert.Contains("Completi", WebUtility.HtmlDecode(await hr.GetStringAsync($"/Dipendenti/Dettaglio/{_dipId}")));

        await _app.CreaUtente("hr@altra.test", Ruoli.Azienda, altra);
        var estraneo = _app.CreateClient();
        await estraneo.Accedi("hr@altra.test");
        var r = await estraneo.GetAsync($"/DatiPersonali?dipendenteId={_dipId}");
        Assert.DoesNotContain("Rossi Mario", await Testo(r));
    }

    [Fact]
    public void Regole_su_familiari_e_tfr()
    {
        var p = new ProfiloDipendente(Guid.NewGuid());
        var cf = CodiceFiscale.Parse("VRDNNA88C41F839V");
        var coniuge = new DatiFamiliare(RelazioneFamiliare.Coniuge, "Anna", "Verdi", cf, new(1988, 3, 1), 100, false);
        Assert.Throws<DomainException>(() => p.ImpostaFamiglia(true,
            [coniuge, coniuge with { CodiceFiscale = CodiceFiscale.Parse("RSSMRA85T10A562S") }], "x", DateTime.UtcNow));
        Assert.Throws<DomainException>(() => p.ImpostaFamiglia(true, [coniuge with { PercentualeCarico = 30 }], "x", DateTime.UtcNow));
        Assert.Throws<DomainException>(() => p.ImpostaPagamento(Iban.Parse("IT60X0542811101000000123456"), null, DestinazioneTfr.FondoPensione, " ", "x", DateTime.UtcNow));
        Assert.Throws<DomainException>(() => p.Completa(true, "x", DateTime.UtcNow));
        Assert.Equal(1, p.Passo);
    }
}
