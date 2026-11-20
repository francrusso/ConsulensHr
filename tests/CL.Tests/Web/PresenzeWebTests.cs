using System.Net;
using System.Text;
using CL.Domain.Assenze;
using CL.Domain.Aziende;
using CL.Domain.Dipendenti;
using CL.Domain.Presenze;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace CL.Tests.Web;

public sealed class PresenzeWebTests : IDisposable
{
    private readonly AppFactory _app = new();
    private Guid _aziendaId, _dipId;
    private readonly int _anno = DateTime.Today.Year, _mese = DateTime.Today.Month;

    public void Dispose() => _app.Dispose();

    private static async Task<string> Testo(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
    private string Foglio => $"/Presenze/Foglio?anno={_anno}&mese={_mese}";

    /// <summary>Acme con Rossi in forza e un giorno di ferie approvato nel mese corrente.</summary>
    private async Task Dati()
    {
        (_aziendaId, _dipId) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var d = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 12, 10));
            d.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(_anno, 1, 1), null, 28_000m);
            d.ImpostaMatricola("42");
            var giorno = new DateOnly(_anno, _mese, 1);
            while (!CalendarioLavorativo.IsLavorativo(giorno, 5)) giorno = giorno.AddDays(1);
            var ferie = PianificatoreAssenze.Crea(d, a, TipoAssenza.Ferie, giorno, giorno, [], DateTime.UtcNow);
            ferie.Approva("hr", DateTime.UtcNow);
            db.AddRange(a, d, ferie);
            await db.SaveChangesAsync();
            return (a.Id, d.Id);
        });
    }

    private async Task<HttpClient> Accedi(string email, string ruolo, Guid? aziendaId = null)
    {
        await _app.CreaUtente(email, ruolo, aziendaId);
        var c = _app.CreateClient();
        await c.Accedi(email);
        return c;
    }

    [Fact]
    public async Task L_azienda_compila_e_invia_lo_studio_esporta_elabora_e_riapre()
    {
        await Dati();
        var hr = await Accedi("hr@acme.test", Ruoli.Azienda, _aziendaId);

        var html = WebUtility.HtmlDecode(await hr.GetStringAsync(Foglio));
        Assert.Contains("Rossi Mario", html);
        Assert.Contains("matr. 42", html);
        Assert.Matches(@"gruppo-portale"">8</td>", html); // il giorno di ferie approvato è già nel foglio

        var campi = new Dictionary<string, string>
        {
            ["anno"] = _anno.ToString(), ["mese"] = _mese.ToString(), ["righe[0].DipendenteId"] = _dipId.ToString(),
            ["righe[0].OreStraordinario"] = "5,5", ["righe[0].BuoniPasto"] = "18", ["righe[0].Premi"] = "150", ["righe[0].Note"] = "Trasferta a Roma",
            ["note"] = "Nuova assunzione dal 1° del mese prossimo", ["invia"] = "false",
        };
        Assert.Contains("Bozza salvata", await Testo(await hr.InviaForm(Foglio, "/Presenze/Salva", new(campi))));
        Assert.Contains("value=\"5,5\"", await hr.GetStringAsync(Foglio));

        html = await Testo(await hr.InviaForm(Foglio, "/Presenze/Salva", new(campi) { ["invia"] = "true" }));
        Assert.Contains("inviate al consulente", html);
        Assert.Contains("chiedi al consulente di riaprirla", html);
        var c = await _app.ConDb(db => db.ComunicazioniPresenze.SingleAsync());
        Assert.Equal(StatoComunicazione.Inviata, c.Stato);
        Assert.Equal(8m, c.Righe.Single().OreFerie);

        // Inviata: l'azienda non può più cambiarla.
        html = await Testo(await hr.InviaForm(Foglio, "/Presenze/Salva", new(campi) { ["righe[0].OreStraordinario"] = "99" }));
        Assert.Contains("già stata inviata", html);

        // Il consulente la vede nel cruscotto, scarica il CSV, la elabora e la riapre.
        var consulente = await Accedi("consulente@test.it", Ruoli.Consulente);
        Assert.Contains("Ricevute da 1 aziende su 1", WebUtility.HtmlDecode(await consulente.GetStringAsync($"/Presenze?anno={_anno}&mese={_mese}")));

        var csv = await consulente.GetAsync($"/Presenze/Esporta?aziendaId={_aziendaId}&anno={_anno}&mese={_mese}");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        var testoCsv = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync());
        Assert.StartsWith("﻿Matricola;", testoCsv);
        Assert.Contains("\"42\";\"Rossi Mario\";RSSMRA85T10A562S;8;0;0;0;0;5,5;0;18;150;0;0;\"Trasferta a Roma\"", testoCsv);

        var foglioStudio = $"/Presenze/Foglio?aziendaId={_aziendaId}&anno={_anno}&mese={_mese}";
        var dati = new Dictionary<string, string> { ["aziendaId"] = _aziendaId.ToString(), ["anno"] = _anno.ToString(), ["mese"] = _mese.ToString() };
        Assert.Contains("Mese segnato come elaborato", await Testo(await consulente.InviaForm(foglioStudio, "/Presenze/Elabora", new(dati))));
        Assert.Contains("Comunicazione riaperta", await Testo(await consulente.InviaForm(foglioStudio, "/Presenze/Riapri", new(dati) { ["motivo"] = "Mancano le ore di sabato" })));

        Assert.Contains("Bozza salvata", await Testo(await hr.InviaForm(Foglio, "/Presenze/Salva", new(campi) { ["righe[0].OreStraordinario"] = "7" })));
        Assert.Equal(7m, (await _app.ConDb(db => db.ComunicazioniPresenze.SingleAsync())).Righe.Single().OreStraordinario);
        Assert.Empty(_app.ErroriServer);
    }

    [Fact]
    public async Task Ogni_azienda_vede_solo_le_proprie_presenze_e_il_dipendente_nessuna()
    {
        await Dati();
        var altra = await _app.ConDb(async db =>
        {
            var a = new Azienda("Altra S.p.A.", PartitaIva.Parse("12345678903"));
            db.Add(a);
            await db.SaveChangesAsync();
            return a.Id;
        });
        var hrAltra = await Accedi("hr@altra.test", Ruoli.Azienda, altra);
        // L'aziendaId nella richiesta è ignorato: il referente vede sempre la propria azienda.
        var html = WebUtility.HtmlDecode(await hrAltra.GetStringAsync($"/Presenze/Foglio?aziendaId={_aziendaId}&anno={_anno}&mese={_mese}"));
        Assert.Contains("Altra S.p.A.", html);
        Assert.DoesNotContain("Rossi Mario", html);

        // Un dipendente estraneo nel form viene ignorato.
        await hrAltra.InviaForm(Foglio, "/Presenze/Salva", new()
        {
            ["anno"] = _anno.ToString(), ["mese"] = _mese.ToString(), ["righe[0].DipendenteId"] = _dipId.ToString(),
            ["righe[0].OreStraordinario"] = "10", ["invia"] = "false",
        });
        Assert.Empty((await _app.ConDb(db => db.ComunicazioniPresenze.SingleAsync())).Righe);

        await _app.CreaUtente("mario@acme.test", Ruoli.Dipendente);
        var dip = _app.CreateClient();
        await dip.Accedi("mario@acme.test");
        var r = await dip.GetAsync(Foglio);
        Assert.Contains("/Account/AccessoNegato", r.RequestMessage!.RequestUri!.AbsolutePath);
    }
}
