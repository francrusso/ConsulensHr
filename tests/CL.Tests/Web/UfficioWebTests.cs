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
        Assert.Contains("href=\"/Ufficio\"", home); // il calendario è la prima pagina del dipendente
        Assert.Contains("Calendario", home);

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
        Assert.Contains("Nuova richiesta", html);
        Assert.Contains(">Calendario</div>", html);          // voce di menù del dipendente

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
        Assert.Contains("Concesso dal contratto: 1 giorno a settimana", WebUtility.HtmlDecode(await dip.GetStringAsync("/")));
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
        Assert.Contains("value=\"InUfficio\"", modulo); // tutte e due le voci, per tutti

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
        // Chi lavora già a distanza: lo smart working non va approvato.
        var settimanaDopo = giorno.AddDays(7);
        await dip.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new() { ["Tipo"] = "SmartWorking", ["Dal"] = settimanaDopo.ToString("yyyy-MM-dd") });
        Assert.All(await _app.ConDb(db => db.GiorniSmartWorking.ToListAsync()), g => Assert.Equal(CL.Domain.Presenze.StatoSmartWorking.Approvato, g.Stato));
    }

    [Fact]
    public async Task La_scheda_del_dipendente_conta_i_giorni_in_ufficio_e_da_remoto()
    {
        await PreparaDipendente(SedeLavoro.SedeLegale);
        var oggi = DateOnly.FromDateTime(DateTime.Today);
        var giorno = new DateOnly(oggi.Year, 1, 10);
        while (!CalendarioLavorativo.IsLavorativo(giorno, 5)) giorno = giorno.AddDays(1);
        if (giorno >= oggi) return;
        await _app.ConDb(async db =>
        {
            var g = new CL.Domain.Presenze.GiornoSmartWorking(_dipId, giorno, DateTime.UtcNow);
            g.Approva("hr", DateTime.UtcNow);
            db.Add(g);
            return await db.SaveChangesAsync();
        });
        await _app.CreaUtente("hr@test.it", Ruoli.Azienda, _aziendaId);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@test.it");
        var html = WebUtility.HtmlDecode(await hr.GetStringAsync($"/Dipendenti/Dettaglio/{_dipId}?sezione=ferie"));
        Assert.Contains("Assenze e presenze", html);
        Assert.Matches(@"Giorni da remoto</div>\s*<div class=""valore-indicatore"">1</div>", html);
        Assert.Contains("Giorni in ufficio", html);
        Assert.Contains(giorno.ToString("ddd d"), html);   // dettaglio del mese
        Assert.Contains("Smart working", html);
        Assert.Empty(_app.ErroriServer);
    }

    [Fact]
    public async Task L_azienda_registra_e_approva_subito_lo_smart_working_dal_modulo()
    {
        await PreparaDipendente(SedeLavoro.SedeLegale);
        await _app.CreaUtente("hr@test.it", Ruoli.Azienda, _aziendaId);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@test.it");
        var modulo = WebUtility.HtmlDecode(await hr.GetStringAsync("/Assenze/Nuova"));
        Assert.Contains("Nuova richiesta", modulo);
        Assert.Contains(">Smart working</option>", modulo);
        Assert.Contains(">Presenza in ufficio</option>", modulo);

        var giorno = ProssimoLavorativo();
        await hr.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new()
        {
            ["DipendenteId"] = _dipId.ToString(), ["Tipo"] = "SmartWorking", ["Dal"] = giorno.ToString("yyyy-MM-dd"), ["ApprovaSubito"] = "true",
        });
        var g = await _app.ConDb(db => db.GiorniSmartWorking.SingleAsync());
        Assert.Equal(CL.Domain.Presenze.StatoSmartWorking.Approvato, g.Stato);

        // Anche la presenza in ufficio si registra per chiunque.
        var altro = giorno.AddDays(1);
        while (!CalendarioLavorativo.IsLavorativo(altro, 5)) altro = altro.AddDays(1);
        await hr.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new()
        {
            ["DipendenteId"] = _dipId.ToString(), ["Tipo"] = "InUfficio", ["Dal"] = altro.ToString("yyyy-MM-dd"),
        });
        Assert.Equal(1, await _app.ConDb(db => db.GiorniInUfficio.CountAsync()));
    }

    [Fact]
    public async Task Nei_calendari_le_festivita_sono_in_rosso_con_il_nome()
    {
        var dip = await PreparaDipendente(SedeLavoro.SedeLegale);
        var anno = DateTime.Today.Year;
        var mio = WebUtility.HtmlDecode(await dip.GetStringAsync($"/Ufficio?anno={anno}&mese=12"));
        Assert.Contains("giorno-festa", mio);
        Assert.Contains("Santo Stefano", mio);

        await _app.CreaUtente("hr@test.it", Ruoli.Azienda, _aziendaId);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@test.it");
        var cal = WebUtility.HtmlDecode(await hr.GetStringAsync($"/Assenze/Calendario?anno={anno}&mese=12"));
        Assert.Contains("cal-festa", cal);
        Assert.Contains("Natale", cal);
        Assert.Contains("Tipo di richiesta", WebUtility.HtmlDecode(await hr.GetStringAsync("/Assenze/Nuova")));
    }

    [Fact]
    public async Task Il_dipendente_segnala_uno_straordinario_che_approvato_va_nel_riepilogo()
    {
        var dip = await PreparaDipendente(SedeLavoro.SedeLegale);
        var oggi = DateOnly.FromDateTime(DateTime.Today);
        var giorno = oggi;
        while (!CalendarioLavorativo.IsLavorativo(giorno, 5)) giorno = giorno.AddDays(-1);
        if (giorno.Month != oggi.Month) return;

        var r = await dip.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new()
        {
            ["Tipo"] = "Straordinario", ["Dal"] = giorno.ToString("yyyy-MM-dd"), ["Ore"] = "2,5", ["Note"] = "Chiusura progetto",
        });
        Assert.Contains("in attesa di approvazione", WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
        var s = await _app.ConDb(db => db.Straordinari.SingleAsync());
        Assert.Equal((2.5m, StatoRichiesta.InAttesa), (s.Ore, s.Stato));

        await _app.CreaUtente("hr@test.it", Ruoli.Azienda, _aziendaId);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@test.it");
        Assert.Contains($"/Assenze/DecidiStraordinario/{s.Id}", WebUtility.HtmlDecode(await hr.GetStringAsync("/Assenze")));
        await hr.InviaForm("/Assenze", $"/Assenze/DecidiStraordinario/{s.Id}", new() { ["approva"] = "true" });
        Assert.Equal(StatoRichiesta.Approvata, (await _app.ConDb(db => db.Straordinari.SingleAsync())).Stato);

        // Nel riepilogo presenze del mese le ore approvate sono già nel campo dello straordinario.
        var riepilogo = WebUtility.HtmlDecode(await hr.GetStringAsync($"/Presenze/Foglio?anno={giorno.Year}&mese={giorno.Month}"));
        Assert.Matches(@"name=""righe\[0\]\.Orestraordinario"" value=""2,5""|name=""righe\[0\]\.OreStraordinario"" value=""2,5""", riepilogo);
        Assert.Contains("2,5 h approvate nel portale", riepilogo);
    }

    [Fact]
    public async Task L_azienda_sceglie_i_tipi_di_richiesta_e_gli_altri_spariscono()
    {
        var dip = await PreparaDipendente(SedeLavoro.SedeLegale);
        await _app.ConDb(async db =>
        {
            var p = new CL.Domain.Aziende.ProfiloAzienda(_aziendaId);
            db.Add(p);
            return await db.SaveChangesAsync();
        });
        await _app.CreaUtente("hr@test.it", Ruoli.Azienda, _aziendaId);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@test.it");

        // Tutto abilitato tranne straordinari e smart working (ferie e malattia restano comunque).
        var abilitate = CL.Domain.Aziende.TipiRichiesta.Tutti.Select(t => t.Chiave)
            .Where(k => k is not ("Straordinario" or "SmartWorking")).ToList();
        var token = await hr.TokenDa("/Assenze/Nuova");
        var campi = abilitate.Select(k => new KeyValuePair<string, string>("abilitate", k))
            .Append(new("__RequestVerificationToken", token));
        await hr.PostAsync("/Onboarding/RichiesteAbilitate", new FormUrlEncodedContent(campi));
        var azienda = await _app.ConDb(db => db.Aziende.SingleAsync());
        Assert.False(azienda.Abilitata("Straordinario"));
        Assert.True(azienda.Abilitata("Ferie"));

        var modulo = WebUtility.HtmlDecode(await dip.GetStringAsync("/Assenze/Nuova"));
        Assert.DoesNotContain(">Straordinario</option>", modulo);
        Assert.DoesNotContain(">Smart working</option>", modulo);
        Assert.Contains(">Ferie</option>", modulo);
        var r = await dip.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new()
        {
            ["Tipo"] = "Straordinario", ["Dal"] = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"), ["Ore"] = "2",
        });
        Assert.Contains("non è previsto dall'azienda", WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
        Assert.Equal(0, await _app.ConDb(db => db.Straordinari.CountAsync()));
        Assert.DoesNotContain("data-mostra-filtro=\"straordinari\"", await dip.GetStringAsync("/Ufficio"));
    }

    [Fact]
    public async Task Il_referente_vede_il_dettaglio_e_revoca_anche_una_richiesta_passata()
    {
        await PreparaDipendente(SedeLavoro.SedeLegale);
        var giorno = new DateOnly(DateTime.Today.Year, 1, 12);
        while (!CalendarioLavorativo.IsLavorativo(giorno, 5)) giorno = giorno.AddDays(1);
        if (giorno >= DateOnly.FromDateTime(DateTime.Today)) return;
        var id = await _app.ConDb(async db =>
        {
            var d = await db.Dipendenti.SingleAsync();
            var a = await db.Aziende.SingleAsync();
            var ferie = PianificatoreAssenze.Crea(d, a, TipoAssenza.Ferie, giorno, giorno, [], DateTime.UtcNow);
            ferie.Approva("hr", DateTime.UtcNow);
            db.Add(ferie);
            await db.SaveChangesAsync();
            return ferie.Id;
        });
        await _app.CreaUtente("hr@test.it", Ruoli.Azienda, _aziendaId);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@test.it");
        var html = WebUtility.HtmlDecode(await hr.GetStringAsync("/Assenze"));
        Assert.Contains("modaleDettaglioRichiesta", html);
        Assert.Contains("Cerca per nome o cognome", html);                                     // ricerca nelle richieste
        Assert.Contains("Rossi Mario", WebUtility.HtmlDecode(await hr.GetStringAsync("/Assenze?q=mario rossi")));
        Assert.DoesNotContain("Rossi Mario</span>", WebUtility.HtmlDecode(await hr.GetStringAsync("/Assenze?q=bianchi")));
        Assert.Contains($"/Assenze/Revoca/{id}?genere=assenza", html);
        await hr.InviaForm("/Assenze", $"/Assenze/Revoca/{id}?genere=assenza", new());
        Assert.Equal(StatoRichiesta.Annullata, (await _app.ConDb(db => db.RichiesteAssenza.SingleAsync())).Stato);

        // Contratti: stato del contratto e modifica che apre proprio quel contratto.
        var contratti = WebUtility.HtmlDecode(await hr.GetStringAsync("/Documenti/Contratti"));
        Assert.Contains(">Attivo</span>", contratti);
        Assert.Matches(@"sezione=contratto&modifica=[Tt]rue&contratto=", contratti);
    }

    [Fact]
    public async Task Dai_documenti_si_crea_un_contratto_anche_per_una_persona_nuova_e_si_caricano_cu_e_buste()
    {
        await PreparaDipendente(SedeLavoro.SedeLegale);
        await _app.CreaUtente("hr@test.it", Ruoli.Azienda, _aziendaId);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@test.it");

        // Pulsanti nella barra in alto.
        Assert.Contains("Nuovo contratto", WebUtility.HtmlDecode(await hr.GetStringAsync("/Documenti/Contratti")));
        Assert.Contains("/Documenti/NuovaCu", WebUtility.HtmlDecode(await hr.GetStringAsync("/Documenti/Cu")));
        Assert.Contains("Carica busta paga", WebUtility.HtmlDecode(await hr.GetStringAsync("/BustePaga")));
        Assert.Contains("Carica busta paga", WebUtility.HtmlDecode(await hr.GetStringAsync("/BustePaga/Carica")));
        Assert.Contains("Rossi Mario", WebUtility.HtmlDecode(await hr.GetStringAsync("/Documenti/NuovaCu")));

        // Persona già presente: si va al modulo del contratto.
        var r = await hr.InviaForm("/Documenti/NuovoContratto", "/Documenti/NuovoContratto", new() { ["NuovaPersona"] = "false", ["DipendenteId"] = _dipId.ToString() });
        Assert.EndsWith($"/Dipendenti/Assumi/{_dipId}", r.RequestMessage!.RequestUri!.AbsolutePath);

        // Persona nuova: la si crea dalla stessa schermata e poi si compila il contratto.
        r = await hr.InviaForm("/Documenti/NuovoContratto", "/Documenti/NuovoContratto", new()
        {
            ["NuovaPersona"] = "true", ["Nome"] = "Anna", ["Cognome"] = "Verdi", ["CodiceFiscale"] = "VRDNNA88C41F839V", ["DataNascita"] = "1988-03-01",
            ["Email"] = "anna.verdi@test.it",
        });
        var nuova = await _app.ConDb(db => db.Dipendenti.SingleAsync(d => d.Cognome == "Verdi"));
        Assert.Equal(_aziendaId, nuova.AziendaId);
        Assert.EndsWith($"/Dipendenti/Assumi/{nuova.Id}", r.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("ora inserisci il contratto", WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
        Assert.Equal("anna.verdi@test.it", nuova.Email);
        Assert.Contains("Verdi Anna", WebUtility.HtmlDecode(await hr.GetStringAsync("/Dipendenti"))); // subito nella tabella del personale
    }

    [Fact]
    public async Task Dalla_scheda_si_sceglie_il_referente_del_team_e_chi_gestisce_il_portale()
    {
        await PreparaDipendente(SedeLavoro.SedeLegale);
        var teamId = await _app.ConDb(async db =>
        {
            var t = new CL.Domain.Organizzazione.Team(_aziendaId, "Vendite");
            db.Add(t);
            await db.SaveChangesAsync();
            return t.Id;
        });
        await _app.CreaUtente("hr@test.it", Ruoli.Azienda, _aziendaId);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@test.it");
        Assert.DoesNotContain(">Referenti</a>", await hr.GetStringAsync("/Dipendenti")); // niente più vista separata

        var pagina = $"/Dipendenti/Modifica/{_dipId}";
        Assert.Contains("Referente dei team", WebUtility.HtmlDecode(await hr.GetStringAsync(pagina)));
        var campi = new Dictionary<string, string>
        {
            ["Id"] = _dipId.ToString(), ["AziendaId"] = _aziendaId.ToString(), ["Nome"] = "Mario", ["Cognome"] = "Rossi",
            ["CodiceFiscale"] = "RSSMRA85T10A562S", ["DataNascita"] = "1985-12-10", ["TeamReferente"] = teamId.ToString(),
        };
        Assert.Contains("Modifiche salvate", WebUtility.HtmlDecode(await (await hr.InviaForm(pagina, "/Dipendenti/Salva", campi)).Content.ReadAsStringAsync()));
        Assert.Equal(_dipId, (await _app.ConDb(db => db.Team.SingleAsync())).ResponsabileId);

        // Accesso come azienda: Mario (dipendente con accesso) passa a gestire il portale.
        await hr.InviaForm(pagina, "/Utenti/AccessoAzienda", new() { ["dipendenteId"] = _dipId.ToString(), ["attivo"] = "true" });
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var u = await users.FindByEmailAsync("dip@test.it");
            Assert.True(await users.IsInRoleAsync(u!, Ruoli.Azienda));
            Assert.False(await users.IsInRoleAsync(u!, Ruoli.Dipendente));
            Assert.Equal(_aziendaId, u!.AziendaId);
        }
        Assert.Contains("Azienda: gestisce il portale", WebUtility.HtmlDecode(await hr.GetStringAsync($"/Dipendenti/Dettaglio/{_dipId}")));
    }

    [Fact]
    public async Task Il_consulente_cambia_da_se_nome_ed_email()
    {
        await _app.CreaUtente("studio@test.it", Ruoli.Consulente);
        var c = _app.CreateClient();
        await c.Accedi("studio@test.it");
        var profilo = WebUtility.HtmlDecode(await c.GetStringAsync("/Account/Profilo"));
        Assert.DoesNotContain("rivolgiti al consulente", profilo);
        await c.InviaForm("/Account/Profilo", "/Account/DatiProfilo", new() { ["nome"] = "Giulia", ["cognome"] = "Neri", ["email"] = "giulia@test.it" });
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var u = await users.FindByEmailAsync("giulia@test.it");
        Assert.Equal(("Giulia", "Neri"), (u!.Nome, u.Cognome));
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
