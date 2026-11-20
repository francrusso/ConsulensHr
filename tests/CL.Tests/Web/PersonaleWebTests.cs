using System.Net;
using CL.Domain.Aziende;
using CL.Domain.Common;
using CL.Domain.Dipendenti;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace CL.Tests.Web;

public sealed class PersonaleWebTests : IDisposable
{
    private readonly AppFactory _app = new();
    public void Dispose() => _app.Dispose();

    private static async Task<string> Testo(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

    [Fact]
    public async Task Switch_tra_personale_dipendente_e_professionisti_esterni()
    {
        var anno = DateTime.Today.Year;
        var (azienda, rossi, bianchi) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var r = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 1, 1));
            r.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(anno, 1, 1), null, 28_000m);
            var b = new Dipendente(a.Id, "Laura", "Bianchi", CodiceFiscale.Parse("RSSLRA15A41F839L"), new DateOnly(1985, 1, 1));
            db.AddRange(a, r, b);
            await db.SaveChangesAsync();
            return (a.Id, r.Id, b.Id);
        });
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, azienda);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");

        // Lavoro dipendente senza CCNL: rifiutato. Partita IVA senza CCNL: accettata, senza ferie da maturare.
        var campi = new Dictionary<string, string>
        {
            ["DipendenteId"] = bianchi.ToString(), ["Tipo"] = "TempoIndeterminato", ["DataInizio"] = $"{anno}-02-01",
            ["RetribuzioneLordaAnnua"] = "30000", ["Mansione"] = "Consulenza grafica",
        };
        Assert.Contains("Inserisci il CCNL", await Testo(await hr.InviaForm($"/Dipendenti/Assumi/{bianchi}", "/Dipendenti/Assumi", new(campi))));
        var html = await Testo(await hr.InviaForm($"/Dipendenti/Assumi/{bianchi}", "/Dipendenti/Assumi", new(campi) { ["Tipo"] = "LavoroAutonomo" }));
        Assert.Contains("Incarico registrato", html);
        Assert.Contains("Professionista esterno", html);
        var contratto = (await _app.ConDb(db => db.Dipendenti.SingleAsync(d => d.Id == bianchi))).Contratti.Single();
        Assert.Equal(0m, contratto.SpettanzaAnnua(CL.Domain.Assenze.TipoAssenza.Ferie));

        html = WebUtility.HtmlDecode(await hr.GetStringAsync("/Dipendenti"));
        Assert.Contains("Personale dipendente", html);
        Assert.Contains("Rossi Mario", html);
        Assert.DoesNotContain("Bianchi Laura", html);
        Assert.Contains("Scadenza contratto", html);
        Assert.Contains("28.000", html); // RAL del contratto in corso
        Assert.Contains("Mensile lordo", html);
        Assert.Contains("Indeterminato", html);

        html = WebUtility.HtmlDecode(await hr.GetStringAsync("/Dipendenti?esterni=true"));
        Assert.Contains("Bianchi Laura", html);
        Assert.Contains("Partita IVA / lavoro autonomo", html);
        Assert.Contains("Compenso annuo", html);
        Assert.DoesNotContain("Rossi Mario", html);
        Assert.Contains("Aggiungi professionista", html);

        // Gli esterni non hanno presenze da comunicare.
        var foglio = WebUtility.HtmlDecode(await hr.GetStringAsync($"/Presenze/Foglio?anno={anno}&mese={DateTime.Today.Month}"));
        Assert.Contains("Rossi Mario", foglio);
        Assert.DoesNotContain("Bianchi Laura", foglio);
        Assert.Empty(_app.ErroriServer);
    }

    [Fact]
    public void Contratto_da_dipendente_richiede_ccnl_quello_esterno_no()
    {
        var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
        var d = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 1, 1));
        Assert.Throws<DomainException>(() => d.Assumi(TipoContratto.TempoIndeterminato, "", "", new DateOnly(2026, 1, 1), null, 28_000m));
        var c = d.Assumi(TipoContratto.Collaborazione, "", "", new DateOnly(2026, 1, 1), null, 12_000m);
        Assert.False(c.Tipo.IsSubordinato());
        Assert.Equal(0m, c.OreFerieAnnue);
    }
}
