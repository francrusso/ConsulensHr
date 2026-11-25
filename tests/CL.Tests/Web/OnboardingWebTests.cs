using System.Net;
using CL.Domain.Aziende;
using CL.Domain.Common;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace CL.Tests.Web;

public sealed class OnboardingWebTests : IDisposable
{
    private readonly AppFactory _app = new();
    private Guid _aziendaId;

    public void Dispose() => _app.Dispose();

    private static async Task<string> Testo(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

    private async Task<HttpClient> ReferenteNuovo(TipoAvvio tipo = TipoAvvio.NuovaAttivita)
    {
        _aziendaId = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            a.ImpostaTipoAvvio(tipo);
            db.Add(a);
            await db.SaveChangesAsync();
            return a.Id;
        });
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, _aziendaId, profiloCompleto: false);
        var c = _app.CreateClient();
        await c.Accedi("hr@acme.test");
        return c;
    }

    private static Task<HttpResponseMessage> Passo(HttpClient c, string azione, Dictionary<string, string> campi, string? pagina = null) =>
        c.InviaForm(pagina ?? "/Onboarding", $"/Onboarding/{azione}", campi);

    private static readonly Dictionary<string, string> Societari = new()
    {
        ["RagioneSociale"] = "Acme S.r.l.", ["FormaGiuridica"] = "S.r.l.", ["CodiceAteco"] = "47.11.10", ["Pec"] = "acme@pec.it",
        ["NumeroRea"] = "mi-123456", ["DataInizioAttivita"] = "2015-03-01",
    };
    private static readonly Dictionary<string, string> Sedi = new()
    {
        ["LegaleVia"] = "Via Roma 1", ["LegaleCap"] = "20121", ["LegaleComune"] = "Milano", ["LegaleProvincia"] = "mi",
        ["Unita[0].Nome"] = "Magazzino", ["Unita[0].Via"] = "Via Po 5", ["Unita[0].Cap"] = "10123", ["Unita[0].Comune"] = "Torino",
        ["Unita[0].Provincia"] = "TO", ["Unita[0].ReferenteNome"] = "Luca Bianchi",
    };
    private static readonly Dictionary<string, string> Legale = new()
    {
        ["Nome"] = "Mario", ["Cognome"] = "Rossi", ["CodiceFiscale"] = "rssmra85t10a562s", ["Email"] = "mario@acme.test",
    };
    private static readonly Dictionary<string, string> Posizioni = new()
    {
        ["Ccnl"] = "Commercio e terziario", ["MatricolaInps"] = "4912345678", ["InailCodiceDitta"] = "12345678", ["InailPat"] = "87654321",
        ["GiorniLavorativiSettimana"] = "5", ["OreSettimanali"] = "40",
    };
    private static readonly Dictionary<string, string> Paghe = new()
    {
        ["Iban"] = "IT60 X054 2811 1010 0000 0123 456", ["GiornoPagamento"] = "27", ["ReferenteNome"] = "Laura Bianchi",
        ["ReferenteEmail"] = "paghe@acme.test",
    };

    [Fact]
    public async Task Referente_al_primo_accesso_compila_il_profilo_prima_di_usare_il_portale()
    {
        var hr = await ReferenteNuovo();

        // Qualsiasi pagina porta al percorso guidato, con il menù ridotto.
        var html = WebUtility.HtmlDecode(await hr.GetStringAsync("/Dipendenti"));
        Assert.Contains("Benvenuto in Consulens HR", html);
        Assert.Contains("1. Dati societari", html);
        Assert.DoesNotContain(">Dipendenti</div>", html);

        // Non si salta al riepilogo.
        Assert.Contains("1. Dati societari", WebUtility.HtmlDecode(await hr.GetStringAsync("/Onboarding?passo=6")));

        Assert.Contains("2. Sedi", await Testo(await Passo(hr, "Societari", new(Societari))));
        Assert.Contains("3. Legale rappresentante", await Testo(await Passo(hr, "Sedi", new(Sedi))));
        Assert.Contains("4. Contratto e posizioni", await Testo(await Passo(hr, "Legale", new(Legale))));
        Assert.Contains("5. Paghe", await Testo(await Passo(hr, "Posizioni", new(Posizioni))));
        html = await Testo(await Passo(hr, "Paghe", new(Paghe)));
        Assert.Contains("6. Riepilogo", html);
        Assert.Contains("Via Po 5, 10123 Torino (TO)", html);
        Assert.Contains("IT60X0542811101000000123456", html);

        // Senza presa visione della privacy non si completa.
        html = await Testo(await Passo(hr, "Conferma", new()));
        Assert.Contains("presa visione dell'informativa privacy", html);

        html = await Testo(await Passo(hr, "Conferma", new() { ["privacy"] = "true" }));
        Assert.Contains("Il profilo dell'azienda è completo", html);
        Assert.Contains("Panoramica", html);

        // Ora il portale è libero, e i dati sono arrivati all'anagrafica dell'azienda.
        var dipendenti = await hr.GetAsync("/Dipendenti");
        Assert.Equal("/Dipendenti", dipendenti.RequestMessage!.RequestUri!.AbsolutePath);
        var (azienda, profilo) = await _app.ConDb(async db =>
            (await db.Aziende.SingleAsync(), await db.ProfiliAzienda.SingleAsync()));
        Assert.True(profilo.Completato);
        Assert.Equal("hr@acme.test", profilo.CompletatoDa);
        Assert.Equal("MI-123456", profilo.NumeroRea);
        Assert.Equal("MI", profilo.SedeLegaleProvincia);
        Assert.Equal("RSSMRA85T10A562S", profilo.LegaleCodiceFiscale!.Valore);
        Assert.Equal("Torino", azienda.Comune); // l'anagrafica prende la sede dove si lavora
        Assert.Equal("TO", azienda.Provincia);
        Assert.Equal("10123", azienda.Cap);
        Assert.Empty(_app.ErroriServer);
    }

    [Fact]
    public async Task Chi_cambia_consulente_compila_anche_il_subentro_con_titolari_e_voci_inail()
    {
        // L'azienda creata come nuova attività dichiara nell'onboarding di essere già operativa.
        var hr = await ReferenteNuovo();
        Assert.Contains("Attività già operativa", WebUtility.HtmlDecode(await hr.GetStringAsync("/Onboarding")));
        await Passo(hr, "Societari", new(Societari) { ["TipoAvvio"] = "CambioConsulente" });
        Assert.Equal(TipoAvvio.CambioConsulente, (await _app.ConDb(db => db.Aziende.SingleAsync())).TipoAvvio);
        await Passo(hr, "Sedi", new(Sedi));

        // Titolari effettivi diversi dal legale rappresentante: almeno uno, quote entro il 100%.
        var legale = new Dictionary<string, string>(Legale) { ["LegaleUnicoTitolare"] = "false" };
        Assert.Contains("3. Legale rappresentante", await Testo(await Passo(hr, "Legale", legale)));
        legale["Titolari[0].Nome"] = "Anna"; legale["Titolari[0].Cognome"] = "Verdi";
        legale["Titolari[0].CodiceFiscale"] = "VRDNNA80A41F205K"; legale["Titolari[0].Quota"] = "60";
        Assert.Contains("4. Contratto e posizioni", await Testo(await Passo(hr, "Legale", legale)));

        var posizioni = new Dictionary<string, string>(Posizioni)
        {
            ["Csc"] = "70708", ["CodiciAutorizzazione"] = "3N", ["VociInail[0].Voce"] = "0722", ["VociInail[0].Tasso"] = "4,5",
        };
        var html = await Testo(await Passo(hr, "Posizioni", posizioni));
        Assert.Contains("5. Paghe", html);

        // Dopo le paghe arriva il passo del cambio consulente, poi il riepilogo (7).
        html = await Testo(await Passo(hr, "Paghe", new(Paghe)
        {
            ["Referenti[0].Ambito"] = "RisorseUmane", ["Referenti[0].Nome"] = "Giulia Neri", ["Referenti[0].Email"] = "hr@acme.test",
            ["Referenti[1].Nome"] = "Senza contatti",
        }));
        Assert.Contains("Indica email o telefono di Senza contatti", html);
        html = await Testo(await Passo(hr, "Paghe", new(Paghe)
        {
            ["Referenti[0].Ambito"] = "RisorseUmane", ["Referenti[0].Nome"] = "Giulia Neri", ["Referenti[0].Email"] = "hr@acme.test",
        }));
        Assert.Contains("6. Cambio consulente", html);
        html = await Testo(await Passo(hr, "Subentro", new()
        {
            ["ConsulentePrecedente"] = "Studio Neri", ["DataSubentro"] = "2026-12-01", ["UltimoMeseElaborato"] = "2026-11",
        }));
        Assert.Contains("7. Riepilogo", html);
        Assert.Contains("Verdi Anna (60%)", html);
        Assert.Contains("0722", html);
        Assert.Contains("Studio Neri", html);
        Assert.Contains("Giulia Neri · hr@acme.test", html);

        var p = await _app.ConDb(db => db.ProfiliAzienda.SingleAsync());
        Assert.True(p.Subentro);
        Assert.Equal("70708", p.Csc);
        Assert.Equal(4.5m, Assert.Single(p.VociInail).Tasso);
        Assert.Equal("Magazzino", Assert.Single(p.UnitaLocali).Nome);
        Assert.Equal(new DateOnly(2026, 11, 1), p.UltimoMeseElaborato);
        Assert.Empty(_app.ErroriServer);
    }

    [Fact]
    public async Task Una_nuova_attivita_non_vede_il_passo_del_cambio_consulente()
    {
        var hr = await ReferenteNuovo();
        await Passo(hr, "Societari", new(Societari));
        await Passo(hr, "Sedi", new(Sedi));
        await Passo(hr, "Legale", new(Legale));
        await Passo(hr, "Posizioni", new(Posizioni));
        var html = await Testo(await Passo(hr, "Paghe", new(Paghe)));
        Assert.Contains("6. Riepilogo", html);
        Assert.DoesNotContain("Cambio consulente", html);
    }

    [Fact]
    public async Task Dati_non_validi_restano_sul_passo_con_l_errore()
    {
        var hr = await ReferenteNuovo();
        await Passo(hr, "Societari", new(Societari));

        var sedi = new Dictionary<string, string>(Sedi) { ["LegaleCap"] = "201", ["Unita[0].Via"] = "" };
        var html = await Testo(await Passo(hr, "Sedi", sedi));
        Assert.Contains("2. Sedi", html);
        Assert.Contains("CAP di 5 cifre", html);
        Assert.Contains("Inserisci l'indirizzo", html);

        await Passo(hr, "Sedi", new(Sedi));
        html = await Testo(await Passo(hr, "Legale", new(Legale) { ["CodiceFiscale"] = "ABC" }));
        Assert.Contains("3. Legale rappresentante", html);
        Assert.Equal(3, (await _app.ConDb(db => db.ProfiliAzienda.SingleAsync())).Passo);
    }

    [Fact]
    public async Task Lo_studio_vede_lo_stato_e_puo_compilare_per_conto_dell_azienda()
    {
        var hr = await ReferenteNuovo();
        await _app.CreaUtente("consulente@test.it", Ruoli.Consulente);
        var consulente = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await consulente.Accedi("consulente@test.it");

        // La scheda dell'azienda è la stessa pagina "Profilo azienda" del referente, con in più le azioni dello studio.
        var scheda = WebUtility.HtmlDecode(await consulente.GetStringAsync($"/Aziende/Dettaglio/{_aziendaId}"));
        Assert.Contains("Profilo azienda", scheda);
        Assert.Contains("ACME S.R.L.", scheda);
        Assert.Contains("Da completare", scheda);
        Assert.Contains("1. Dati societari", scheda);
        Assert.Contains("Referenti con accesso al portale", scheda);
        Assert.Contains("Disattiva", scheda);
        Assert.Contains("Profilo da completare", WebUtility.HtmlDecode(await consulente.GetStringAsync("/Aziende")));

        var pagina = $"/Onboarding?aziendaId={_aziendaId}";
        var html = await Testo(await Passo(consulente, "Societari", new(Societari) { ["aziendaId"] = _aziendaId.ToString() }, pagina));
        Assert.Contains("2. Sedi", html);

        scheda = WebUtility.HtmlDecode(await consulente.GetStringAsync($"/Aziende/Dettaglio/{_aziendaId}"));
        Assert.Contains("2. Sedi", scheda);

        // Il referente vede la stessa pagina, senza le azioni dello studio.
        var suo = WebUtility.HtmlDecode(await hr.GetStringAsync("/Onboarding"));
        Assert.Contains("2. Sedi", suo);
        Assert.DoesNotContain("Referenti con accesso al portale", suo);
        Assert.DoesNotContain("Disattiva", suo);
    }

    [Fact]
    public void Profilo_incompleto_non_si_conferma()
    {
        var p = new ProfiloAzienda(Guid.NewGuid());
        p.ImpostaDatiSocietari(new("S.r.l.", null, "47.11", null, "pec@test.it", null));
        var ex = Assert.Throws<DomainException>(() => p.Completa(true, "x", DateTime.UtcNow));
        Assert.Contains("sede legale", ex.Message);
        Assert.Throws<DomainException>(() => p.ImpostaSedi(new("Via", "2012", "Milano", "MI"), []));
        Assert.Throws<DomainException>(() => p.ImpostaSedi(new("Via", "20121", "Milano", "Milano"), []));
        Assert.Throws<DomainException>(() => p.ImpostaPosizioni(new("CCNL", "1", "2", "3", null, null, null, 7, 40)));
        Assert.Equal(2, p.Passo);
    }

    [Fact]
    public async Task Durante_il_percorso_guidato_funziona_la_ricerca_dei_comuni()
    {
        // I passi delle sedi (e della residenza del dipendente) cercano comune e via su /Indirizzi.
        var c = await ReferenteNuovo();
        var r = await c.GetAsync("/Indirizzi/Comuni?q=Milano");
        Assert.Equal("application/json", r.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Milano (MI)", await r.Content.ReadAsStringAsync());
    }
}
