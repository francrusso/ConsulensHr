using System.Net;
using CL.Domain.Assenze;
using CL.Domain.Aziende;
using CL.Domain.Dipendenti;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;

namespace CL.Tests.Web;

public sealed class PanoramicaWebTests : IDisposable
{
    private readonly AppFactory _app = new();
    public void Dispose() => _app.Dispose();

    /// <summary>Due aziende; in Acme Rossi ha 2 giorni di ferie approvati nel mese corrente, in Altra Verdi nessuno.</summary>
    private async Task<(Guid Acme, Guid Altra)> Dati()
    {
        var anno = DateTime.Today.Year;
        return await _app.ConDb(async db =>
        {
            var acme = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var altra = new Azienda("Altra S.p.A.", PartitaIva.Parse("12345678903"));
            var rossi = new Dipendente(acme.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 12, 10));
            var verdi = new Dipendente(altra.Id, "Luigi", "Verdi", CodiceFiscale.Parse("VRDNNA88C41F839V"), new DateOnly(1988, 3, 1));
            foreach (var d in new[] { rossi, verdi }) d.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(anno, 1, 1), null, 28_000m);
            var g = new DateOnly(anno, DateTime.Today.Month, 1);
            while (!CalendarioLavorativo.IsLavorativo(g, 5) || !CalendarioLavorativo.IsLavorativo(g.AddDays(1), 5)) g = g.AddDays(1);
            var ferie = PianificatoreAssenze.Crea(rossi, acme, TipoAssenza.Ferie, g, g.AddDays(1), [], DateTime.UtcNow);
            ferie.Approva("hr", DateTime.UtcNow);
            db.AddRange(acme, altra, rossi, verdi, ferie);
            await db.SaveChangesAsync();
            return (acme.Id, altra.Id);
        });
    }

    [Fact]
    public async Task Referente_vede_grafici_della_propria_azienda()
    {
        var (acme, _) = await Dati();
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, acme);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");

        var html = WebUtility.HtmlDecode(await hr.GetStringAsync("/"));
        Assert.Contains("Assenze negli ultimi 12 mesi", html);
        Assert.Contains("Ferie " + DateTime.Today.Year + " per dipendente", html);
        Assert.Contains("Rossi Mario", html);
        Assert.DoesNotContain("Verdi Luigi", html);
        // Dati dei grafici: nel mese corrente 2 giorni di ferie da 8 ore.
        Assert.Matches("\"name\":\"Ferie\",\"data\":\\[(0,){11}16\\]", html);
        Assert.Contains("\"name\":\"Godute\",\"data\":[16]", html);
        Assert.Contains("apexcharts.min.js", html);
        Assert.Empty(_app.ErroriServer);
    }

    [Fact]
    public async Task Studio_vede_tutte_le_aziende_o_ne_filtra_una()
    {
        var (acme, altra) = await Dati();
        await _app.CreaUtente("studio@test.it", Ruoli.Consulente);
        var studio = _app.CreateClient();
        await studio.Accedi("studio@test.it");

        var html = WebUtility.HtmlDecode(await studio.GetStringAsync("/"));
        Assert.Contains("Rossi Mario", html);
        Assert.Contains("Verdi Luigi", html);
        Assert.Contains("Aziende attive", html);
        Assert.Contains("0 su 2", html); // presenze del mese: nessuna azienda ha inviato

        html = WebUtility.HtmlDecode(await studio.GetStringAsync($"/?aziendaId={altra}"));
        Assert.Contains("Verdi Luigi", html);
        Assert.DoesNotContain("Rossi Mario", html);
        Assert.Contains("Nessuna assenza approvata negli ultimi 12 mesi", html);
        Assert.Empty(_app.ErroriServer);
    }
}
