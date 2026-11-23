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
        Assert.Contains("href=\"/Ufficio\"", home); // Presenze e assenze › Calendario
        Assert.Contains("Dove lavoro", home); // la persona vede dove lavora nella sua panoramica
        Assert.Contains("Smart working", home);

        var pagina = $"/Ufficio?anno={giorno.Year}&mese={giorno.Month}";
        var r = await dip.InviaForm(pagina, "/Ufficio/Segna", new() { ["giorno"] = giorno.ToString("yyyy-MM-dd") });
        Assert.Contains("In ufficio: 1 giorno", WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
        var segnato = await _app.ConDb(db => db.GiorniInUfficio.SingleAsync());
        Assert.Equal((_dipId, giorno), (segnato.DipendenteId, segnato.Giorno));

        // Nessuna approvazione: l'azienda lo vede subito nel calendario.
        await _app.CreaUtente("hr@test.it", Ruoli.Azienda, _aziendaId);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@test.it");
        var cal = WebUtility.HtmlDecode(await hr.GetStringAsync($"/Assenze/Calendario?anno={giorno.Year}&mese={giorno.Month}"));
        Assert.Contains("1 in ufficio", cal);
        Assert.Matches("In ufficio: (Mario Rossi|Rossi Mario)", cal);

        // Filtro per luogo: da remoto lavora negli altri giorni, in ufficio solo nel giorno segnato.
        var url = $"/Assenze/Calendario?anno={giorno.Year}&mese={giorno.Month}&luogo=";
        var remoto = WebUtility.HtmlDecode(await hr.GetStringAsync(url + "remoto"));
        Assert.Matches("Presenti: (Mario Rossi|Rossi Mario)", remoto);
        Assert.DoesNotContain("in ufficio</div>", remoto.Replace(" ", "").Replace("\n", ""));
        var ufficio = WebUtility.HtmlDecode(await hr.GetStringAsync(url + "ufficio"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(ufficio, "Presenti: (Mario Rossi|Rossi Mario)"));

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
    public async Task Chi_lavora_in_presenza_chiede_smart_working_e_l_azienda_lo_approva()
    {
        var dip = await PreparaDipendente(SedeLavoro.SedeLegale);
        var giorno = ProssimoLavorativo();
        if (giorno.Year != DateTime.Today.Year) return;

        var home = WebUtility.HtmlDecode(await dip.GetStringAsync("/"));
        Assert.Contains("href=\"/Ufficio\"", home);

        var pagina = $"/Ufficio?anno={giorno.Year}&mese={giorno.Month}";
        var html = WebUtility.HtmlDecode(await (await dip.InviaForm(pagina, "/Ufficio/Richiedi", new() { ["giorno"] = giorno.ToString("yyyy-MM-dd") })).Content.ReadAsStringAsync());
        Assert.Contains("in attesa di approvazione", html);
        var richiesta = await _app.ConDb(db => db.GiorniSmartWorking.SingleAsync());
        Assert.Equal(CL.Domain.Presenze.StatoSmartWorking.InAttesa, richiesta.Stato);
        // Chi lavora in presenza non segna i giorni in ufficio.
        await dip.InviaForm(pagina, "/Ufficio/Segna", new() { ["giorno"] = giorno.ToString("yyyy-MM-dd") });
        Assert.Equal(0, await _app.ConDb(db => db.GiorniInUfficio.CountAsync()));

        await _app.CreaUtente("hr@test.it", Ruoli.Azienda, _aziendaId);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@test.it");
        // Lo smart working sta tra le richieste, insieme a ferie e permessi.
        var elenco = WebUtility.HtmlDecode(await hr.GetStringAsync("/Assenze"));
        Assert.Contains("Smart working", elenco);
        Assert.Contains("Rossi Mario", elenco);
        Assert.Contains("Da approvare <span>(1)</span>", elenco);
        Assert.Contains($"/Assenze/DecidiSmartWorking/{richiesta.Id}", elenco);
        await hr.InviaForm("/Assenze", $"/Assenze/DecidiSmartWorking/{richiesta.Id}", new() { ["approva"] = "true" });
        Assert.Equal(CL.Domain.Presenze.StatoSmartWorking.Approvato, (await _app.ConDb(db => db.GiorniSmartWorking.SingleAsync())).Stato);

        var cal = WebUtility.HtmlDecode(await hr.GetStringAsync($"/Assenze/Calendario?anno={giorno.Year}&mese={giorno.Month}"));
        Assert.Contains("1 in smart working", cal);
        Assert.Contains("Smart working nel mese: 1 giorno", WebUtility.HtmlDecode(await dip.GetStringAsync(pagina)));

        Assert.Contains("Smart working", WebUtility.HtmlDecode(await dip.GetStringAsync("/Assenze"))); // anche tra le sue richieste
        // La persona può ritirare il giorno, anche già approvato, finché non è passato.
        await dip.InviaForm(pagina, "/Ufficio/Richiedi", new() { ["giorno"] = giorno.ToString("yyyy-MM-dd") });
        Assert.Equal(0, await _app.ConDb(db => db.GiorniSmartWorking.CountAsync()));
    }

    [Fact]
    public async Task Il_calendario_del_dipendente_mostra_ferie_permessi_e_richieste_in_attesa()
    {
        var dip = await PreparaDipendente(SedeLavoro.SedeLegale);
        var giorno = ProssimoLavorativo();
        var dopo = giorno.AddDays(1);
        while (!CalendarioLavorativo.IsLavorativo(dopo, 5)) dopo = dopo.AddDays(1);
        if (dopo.Year != DateTime.Today.Year || dopo.Month != giorno.Month) return; // serve lo stesso mese
        await _app.ConDb(async db =>
        {
            var d = await db.Dipendenti.SingleAsync();
            var a = await db.Aziende.SingleAsync();
            var ferie = PianificatoreAssenze.Crea(d, a, TipoAssenza.Ferie, giorno, giorno, [], DateTime.UtcNow); // resta in attesa
            var rol = PianificatoreAssenze.Crea(d, a, TipoAssenza.Rol, dopo, dopo, [], DateTime.UtcNow, orePermesso: 2);
            rol.Approva("hr", DateTime.UtcNow);
            db.AddRange(ferie, rol);
            return await db.SaveChangesAsync();
        });

        var html = WebUtility.HtmlDecode(await dip.GetStringAsync($"/Ufficio?anno={giorno.Year}&mese={giorno.Month}"));
        Assert.Contains("richiesta-attesa", html);         // box del giorno di ferie: in attesa
        Assert.Contains("in attesa</span>", html);
        Assert.Contains("ROL · 2 h", html);                 // permesso a ore approvato
        Assert.Contains("Chiedi ferie o permessi", html);
        Assert.Matches("Presenze e assenze", html);         // breadcrumb

        // Dal giorno si crea subito la richiesta: il modulo arriva con data e tipo già scelti.
        var dopodomani = dopo.ToString("yyyy-MM-dd");
        Assert.Contains("/Assenze/Nuova?dal=", html);
        var modulo = await dip.GetStringAsync($"/Assenze/Nuova?dal={dopodomani}&tipo=Rol&aOre=true");
        Assert.Contains($"value=\"{dopodomani}\"", modulo);

        // Malattia dal calendario: senza protocollo del certificato non si registra.
        Assert.Contains("tipo=Malattia", html);
        var senza = await dip.InviaForm($"/Assenze/Nuova?dal={dopodomani}&tipo=Malattia", "/Assenze/Nuova", new()
        {
            ["Tipo"] = "Malattia", ["Dal"] = dopodomani, ["Al"] = dopodomani,
        });
        Assert.Contains("numero di protocollo", WebUtility.HtmlDecode(await senza.Content.ReadAsStringAsync()));
        Assert.Equal(0, await _app.ConDb(db => db.RichiesteAssenza.CountAsync(r => r.Tipo == TipoAssenza.Malattia)));
    }

    [Fact]
    public async Task Lo_smart_working_concesso_dal_contratto_limita_le_richieste()
    {
        var dip = await PreparaDipendente(SedeLavoro.SedeLegale);
        await _app.ConDb(async db =>
        {
            var c = await db.Contratti.SingleAsync();
            c.ImpostaSmartWorking(1, PeriodoSmartWorking.Settimana);
            return await db.SaveChangesAsync();
        });
        var lunedi = DateOnly.FromDateTime(DateTime.Today).AddDays(7);
        while (lunedi.DayOfWeek != DayOfWeek.Monday) lunedi = lunedi.AddDays(1);
        var (primo, secondo) = (lunedi, lunedi.AddDays(1));
        while (!CalendarioLavorativo.IsLavorativo(primo, 5)) primo = primo.AddDays(1);
        secondo = primo.AddDays(1);
        if (!CalendarioLavorativo.IsLavorativo(secondo, 5) || secondo.DayOfWeek == DayOfWeek.Saturday || secondo.Year != DateTime.Today.Year) return;

        var pagina = $"/Ufficio?anno={primo.Year}&mese={primo.Month}";
        Assert.Contains("1 giorno a settimana", WebUtility.HtmlDecode(await dip.GetStringAsync(pagina)));
        Assert.Contains("Smart working 1 giorno a settimana", WebUtility.HtmlDecode(await dip.GetStringAsync("/")));
        await dip.InviaForm(pagina, "/Ufficio/Richiedi", new() { ["giorno"] = primo.ToString("yyyy-MM-dd") });
        var r = await dip.InviaForm(pagina, "/Ufficio/Richiedi", new() { ["giorno"] = secondo.ToString("yyyy-MM-dd") });
        Assert.Contains("smart working concesso dal contratto", WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
        Assert.Equal(1, await _app.ConDb(db => db.GiorniSmartWorking.CountAsync()));
    }

    [Fact]
    public async Task Da_nuova_richiesta_si_chiede_smart_working_per_piu_giorni()
    {
        var dip = await PreparaDipendente(SedeLavoro.SedeLegale);
        var modulo = WebUtility.HtmlDecode(await dip.GetStringAsync("/Assenze/Nuova"));
        Assert.Contains("value=\"SmartWorking\"", modulo);
        Assert.DoesNotContain("value=\"InUfficio\"", modulo);

        var dal = DateOnly.FromDateTime(DateTime.Today).AddDays(7);
        while (dal.DayOfWeek != DayOfWeek.Monday) dal = dal.AddDays(1);
        var al = dal.AddDays(6); // una settimana intera: contano solo i giorni lavorativi
        if (al.Year != DateTime.Today.Year) return;
        var attesi = Enumerable.Range(0, 7).Select(dal.AddDays).Count(g => CalendarioLavorativo.IsLavorativo(g, 5));

        var r = await dip.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new()
        {
            ["Tipo"] = "SmartWorking", ["Dal"] = dal.ToString("yyyy-MM-dd"), ["Al"] = al.ToString("yyyy-MM-dd"),
        });
        Assert.Contains("in attesa di approvazione", WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
        Assert.Equal(attesi, await _app.ConDb(db => db.GiorniSmartWorking.CountAsync(g => g.Stato == CL.Domain.Presenze.StatoSmartWorking.InAttesa)));
    }

    [Fact]
    public async Task Chi_lavora_a_distanza_segna_l_ufficio_da_nuova_richiesta()
    {
        var dip = await PreparaDipendente(SedeLavoro.SmartWorking);
        var modulo = WebUtility.HtmlDecode(await dip.GetStringAsync("/Assenze/Nuova"));
        Assert.Contains("Presenza in ufficio", modulo);
        var giorno = ProssimoLavorativo();
        await dip.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new() { ["Tipo"] = "InUfficio", ["Dal"] = giorno.ToString("yyyy-MM-dd") });
        Assert.Equal(1, await _app.ConDb(db => db.GiorniInUfficio.CountAsync()));
        // Chi lavora a distanza non può chiedere smart working.
        await dip.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new() { ["Tipo"] = "SmartWorking", ["Dal"] = giorno.ToString("yyyy-MM-dd") });
        Assert.Equal(0, await _app.ConDb(db => db.GiorniSmartWorking.CountAsync()));
    }

    [Fact]
    public async Task Senza_contratto_il_calendario_si_vede_ma_non_si_segna_nulla()
    {
        var dip = await PreparaDipendente(SedeLavoro.SedeLegale);
        await _app.ConDb(async db => { db.Contratti.RemoveRange(db.Contratti); return await db.SaveChangesAsync(); });
        var html = await dip.GetStringAsync("/Ufficio");
        Assert.Contains("Calendario", html);
        Assert.DoesNotContain("/Ufficio/Richiedi", html);
        Assert.DoesNotContain("/Ufficio/Segna", html);
    }
}
