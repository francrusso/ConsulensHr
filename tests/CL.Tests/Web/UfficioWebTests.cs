using System.Net;
using CL.Domain.Assenze;
using CL.Domain.Aziende;
using CL.Domain.Dipendenti;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CL.Tests.Web;

/// <summary>Chi lavora in smart working o ibrido segna da sé i giorni in ufficio, senza approvazione.</summary>
public sealed class UfficioWebTests : IDisposable
{
    private readonly AppFactory _app = new();
    private Guid _aziendaId, _dipId;

    public void Dispose() => _app.Dispose();

    private async Task<HttpClient> PreparaDipendente(SedeLavoro? sede)
    {
        (_aziendaId, _dipId) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var d = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 12, 10));
            d.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(DateTime.Today.Year, 1, 1), null, 28_000m);
            d.ImpostaSede(sede);
            db.AddRange(a, d);
            await db.SaveChangesAsync();
            return (a.Id, d.Id);
        });
        var utente = await _app.CreaUtente("dip@test.it", Ruoli.Dipendente);
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var u = await users.FindByIdAsync(utente.Id);
            u!.DipendenteId = _dipId;
            await users.UpdateAsync(u);
        }
        await _app.CompletaDatiPersonali(_dipId);
        var client = _app.CreateClient();
        await client.Accedi("dip@test.it");
        return client;
    }

    private static DateOnly ProssimoLavorativo()
    {
        var g = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        while (!CalendarioLavorativo.IsLavorativo(g, 5)) g = g.AddDays(1);
        return g;
    }

    [Fact]
    public async Task Smart_worker_segna_e_toglie_un_giorno_in_ufficio_visibile_all_azienda()
    {
        var dip = await PreparaDipendente(SedeLavoro.SmartWorking);
        var giorno = ProssimoLavorativo();
        if (giorno.Year != DateTime.Today.Year) return; // fine anno: fuori dal contratto di prova

        var home = WebUtility.HtmlDecode(await dip.GetStringAsync("/"));
        Assert.Contains("Giorni in ufficio", home);

        var pagina = $"/Ufficio?anno={giorno.Year}&mese={giorno.Month}";
        var r = await dip.InviaForm(pagina, "/Ufficio/Segna", new() { ["giorno"] = giorno.ToString("yyyy-MM-dd") });
        Assert.Contains("1 giorno in ufficio", WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
        var segnato = await _app.ConDb(db => db.GiorniInUfficio.SingleAsync());
        Assert.Equal((_dipId, giorno), (segnato.DipendenteId, segnato.Giorno));

        // Nessuna approvazione: l'azienda lo vede subito nel calendario.
        await _app.CreaUtente("hr@test.it", Ruoli.Azienda, _aziendaId);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@test.it");
        var cal = WebUtility.HtmlDecode(await hr.GetStringAsync($"/Assenze/Calendario?anno={giorno.Year}&mese={giorno.Month}"));
        Assert.Contains("1 in ufficio", cal);
        Assert.Matches("In ufficio: (Mario Rossi|Rossi Mario)", cal);

        // Secondo clic: il giorno si toglie.
        await dip.InviaForm(pagina, "/Ufficio/Segna", new() { ["giorno"] = giorno.ToString("yyyy-MM-dd") });
        Assert.Equal(0, await _app.ConDb(db => db.GiorniInUfficio.CountAsync()));
    }

    [Fact]
    public async Task Giorno_non_lavorativo_non_si_segna()
    {
        var dip = await PreparaDipendente(SedeLavoro.SmartWorking);
        var sabato = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        while (sabato.DayOfWeek != DayOfWeek.Saturday) sabato = sabato.AddDays(1);

        await dip.InviaForm($"/Ufficio?anno={sabato.Year}&mese={sabato.Month}", "/Ufficio/Segna", new() { ["giorno"] = sabato.ToString("yyyy-MM-dd") });
        Assert.Equal(0, await _app.ConDb(db => db.GiorniInUfficio.CountAsync()));
    }

    [Fact]
    public async Task Chi_lavora_in_presenza_non_ha_la_pagina()
    {
        var dip = await PreparaDipendente(SedeLavoro.SedeLegale);
        var home = WebUtility.HtmlDecode(await dip.GetStringAsync("/"));
        Assert.DoesNotContain("Giorni in ufficio", home);

        var r = await dip.GetAsync("/Ufficio");
        Assert.DoesNotContain("/Ufficio", r.RequestMessage!.RequestUri!.AbsolutePath);
    }
}
