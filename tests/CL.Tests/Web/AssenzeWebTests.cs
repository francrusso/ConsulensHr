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

public sealed class AssenzeWebTests : IDisposable
{
    private readonly AppFactory _app = new();
    private Guid _aziendaId, _dipId;

    public void Dispose() => _app.Dispose();

    private static async Task<string> Testo(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

    /// <summary>Azienda + dipendente full-time assunto a inizio anno corrente, con utente portale.</summary>
    private async Task<HttpClient> Prepara(string ruolo)
    {
        var anno = DateTime.Today.Year;
        (_aziendaId, _dipId) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var d = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 12, 10));
            d.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(anno, 1, 1), null, 28_000m);
            db.AddRange(a, d);
            await db.SaveChangesAsync();
            return (a.Id, d.Id);
        });

        var email = $"{ruolo.ToLower()}@test.it";
        var utente = await _app.CreaUtente(email, ruolo, ruolo == Ruoli.Azienda ? _aziendaId : null);
        if (ruolo == Ruoli.Dipendente)
        {
            using var scope = _app.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var u = await users.FindByIdAsync(utente.Id);
            u!.DipendenteId = _dipId;
            await users.UpdateAsync(u);
            await _app.CompletaDatiPersonali(_dipId);
        }

        var client = _app.CreateClient();
        await client.Accedi(email);
        return client;
    }

    /// <summary>Primo lunedì lavorativo del mese indicato dell'anno corrente (evita festività note).</summary>
    private static DateOnly Lunedi(int mese, int settimana = 2)
    {
        var g = new DateOnly(DateTime.Today.Year, mese, 1);
        while (g.DayOfWeek != DayOfWeek.Monday) g = g.AddDays(1);
        return g.AddDays(7 * (settimana - 1));
    }

    [Fact]
    public async Task Dipendente_chiede_ferie_e_il_referente_le_approva()
    {
        var dip = await Prepara(Ruoli.Dipendente);
        // Due giorni lavorativi consecutivi tra un paio di settimane (né weekend né festivi).
        var dal = DateOnly.FromDateTime(DateTime.Today).AddDays(14);
        while (!CalendarioLavorativo.IsLavorativo(dal, 5) || !CalendarioLavorativo.IsLavorativo(dal.AddDays(1), 5)) dal = dal.AddDays(1);
        if (dal.AddDays(1).Year != DateTime.Today.Year) return; // ultime settimane dell'anno: scenario non applicabile

        var r = await dip.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new()
        {
            ["Tipo"] = "Ferie", ["Dal"] = dal.ToString("yyyy-MM-dd"), ["Al"] = dal.AddDays(1).ToString("yyyy-MM-dd"),
            ["Note"] = "Visita parenti", // come dal portale: nessun DipendenteId nel form
        });
        var html = await Testo(r);
        Assert.Contains("in attesa di approvazione", html);
        Assert.Contains("Le mie ferie e permessi", html);

        var richiesta = await _app.ConDb(db => db.RichiesteAssenza.SingleAsync());
        Assert.Equal(_dipId, richiesta.DipendenteId);
        Assert.Equal(16m, richiesta.Ore);

        // Il dipendente non può approvarsi da solo
        var tentativo = await dip.InviaForm("/Assenze", $"/Assenze/Approva/{richiesta.Id}", new());
        Assert.Contains("/Account/AccessoNegato", tentativo.RequestMessage!.RequestUri!.AbsolutePath);

        // Il referente dell'azienda la vede tra quelle da approvare e la approva
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, _aziendaId);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");
        html = WebUtility.HtmlDecode(await hr.GetStringAsync("/Assenze"));
        Assert.Contains("Rossi Mario", html);
        Assert.Contains("Da approvare", html);

        r = await hr.InviaForm("/Assenze", $"/Assenze/Approva/{richiesta.Id}", new());
        Assert.Contains("Richiesta approvata", await Testo(r));
        Assert.Equal(StatoRichiesta.Approvata, (await _app.ConDb(db => db.RichiesteAssenza.SingleAsync())).Stato);
    }

    [Fact]
    public async Task Dipendente_non_puo_chiedere_per_un_collega()
    {
        var dip = await Prepara(Ruoli.Dipendente);
        var collega = await _app.ConDb(async db =>
        {
            var c = new Dipendente(_aziendaId, "Anna", "Bianchi", CodiceFiscale.Parse("VRDLGU90A01H501S"), new DateOnly(1990, 1, 1));
            c.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(DateTime.Today.Year, 1, 1), null, 28_000m);
            db.Add(c);
            await db.SaveChangesAsync();
            return c.Id;
        });
        await dip.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new()
        {
            ["DipendenteId"] = collega.ToString(), ["Tipo"] = "Malattia", ["Dal"] = Lunedi(2).ToString("yyyy-MM-dd"),
        });
        var r = await _app.ConDb(db => db.RichiesteAssenza.SingleAsync());
        Assert.Equal(_dipId, r.DipendenteId);
    }

    [Fact]
    public async Task Rifiuto_richiede_motivo_visibile_al_dipendente()
    {
        var hr = await Prepara(Ruoli.Azienda);
        var giorno = DateOnly.FromDateTime(DateTime.Today).AddDays(30);
        while (!CalendarioLavorativo.IsLavorativo(giorno, 5)) giorno = giorno.AddDays(1);
        if (giorno.Year != DateTime.Today.Year) return;

        await hr.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new()
        {
            ["DipendenteId"] = _dipId.ToString(), ["Tipo"] = "Rol", ["AOre"] = "true", ["Ore"] = "2,5",
            ["Dal"] = giorno.ToString("yyyy-MM-dd"), ["ApprovaSubito"] = "false",
        });
        var richiesta = await _app.ConDb(db => db.RichiesteAssenza.SingleAsync());
        Assert.Equal(2.5m, richiesta.Ore);
        Assert.Equal(StatoRichiesta.InAttesa, richiesta.Stato);

        var r = await hr.InviaForm("/Assenze", $"/Assenze/Rifiuta/{richiesta.Id}", new() { ["motivo"] = "" });
        Assert.Contains("Motivo del rifiuto", await Testo(r));

        r = await hr.InviaForm("/Assenze", $"/Assenze/Rifiuta/{richiesta.Id}", new() { ["motivo"] = "Chiusura bilancio" });
        Assert.Contains("Richiesta rifiutata", await Testo(r));
        var html = WebUtility.HtmlDecode(await hr.GetStringAsync("/Assenze?stato=Rifiutata&tutte=true"));
        Assert.Contains("Chiusura bilancio", html);
    }

    [Fact]
    public async Task Ufficio_registra_malattia_e_residui_nel_dettaglio_dipendente()
    {
        var consulente = await Prepara(Ruoli.Consulente);
        var lun = Lunedi(2);

        var r = await consulente.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new()
        {
            ["DipendenteId"] = _dipId.ToString(), ["Tipo"] = "Malattia", ["Dal"] = lun.ToString("yyyy-MM-dd"),
            ["Al"] = lun.AddDays(2).ToString("yyyy-MM-dd"), ["ProtocolloCertificato"] = "123456789",
        });
        var html = await Testo(r);
        Assert.Contains("Malattia registrata: 24 ore", html); // redirect al dettaglio dipendente
        Assert.Contains("Ferie e permessi", html);

        r = await consulente.InviaForm($"/Dipendenti/Dettaglio/{_dipId}", "/Assenze/ImpostaResidui", new()
        {
            ["dipendenteId"] = _dipId.ToString(), ["anno"] = DateTime.Today.Year.ToString(),
            ["ferie"] = "12,5", ["rol"] = "-4", ["exFestivita"] = "0",
        });
        html = await Testo(r);
        Assert.Contains("Residui di inizio", html);
        var residuo = await _app.ConDb(db => db.SaldiIniziali.SingleAsync(s => s.Tipo == TipoAssenza.Ferie));
        Assert.Equal(12.5m, residuo.Ore);
    }

    [Fact]
    public async Task Dipendente_non_supera_il_saldo_maturato()
    {
        var dip = await Prepara(Ruoli.Dipendente);
        // Tre settimane a gennaio: a gennaio sono maturate solo 160/12 ≈ 13,33 ore di ferie.
        var lun = Lunedi(1, 2);
        var r = await dip.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new()
        {
            ["Tipo"] = "Ferie", ["Dal"] = lun.ToString("yyyy-MM-dd"), ["Al"] = lun.AddDays(18).ToString("yyyy-MM-dd"),
        });
        Assert.Contains("Saldo Ferie insufficiente", await Testo(r));
        Assert.Equal(0, await _app.ConDb(db => db.RichiesteAssenza.CountAsync()));
    }

    [Fact]
    public async Task Lo_studio_non_decide_le_ferie_che_restano_in_attesa_dell_azienda()
    {
        var studio = await Prepara(Ruoli.Consulente);
        var giorno = DateOnly.FromDateTime(DateTime.Today).AddDays(30);
        while (!CalendarioLavorativo.IsLavorativo(giorno, 5)) giorno = giorno.AddDays(1);
        if (giorno.Year != DateTime.Today.Year) return;

        // Anche con "approva subito" (campo non mostrato allo studio) la richiesta resta in attesa.
        var r = await studio.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new()
        {
            ["DipendenteId"] = _dipId.ToString(), ["Tipo"] = "Ferie", ["Dal"] = giorno.ToString("yyyy-MM-dd"), ["ApprovaSubito"] = "true",
        });
        Assert.Contains("in attesa di approvazione dell'azienda", await Testo(r));
        var richiesta = await _app.ConDb(db => db.RichiesteAssenza.SingleAsync());
        Assert.Equal(StatoRichiesta.InAttesa, richiesta.Stato);

        var html = WebUtility.HtmlDecode(await studio.GetStringAsync("/Assenze"));
        Assert.Contains("In attesa dell'azienda", html);
        Assert.DoesNotContain("modaleRifiuto", html); // nessun pulsante approva/rifiuta per lo studio

        r = await studio.InviaForm("/Assenze", $"/Assenze/Approva/{richiesta.Id}", new());
        Assert.Contains("/Account/AccessoNegato", r.RequestMessage!.RequestUri!.AbsolutePath);
        r = await studio.InviaForm("/Assenze", $"/Assenze/Annulla/{richiesta.Id}", new());
        Assert.Contains("/Account/AccessoNegato", r.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Equal(StatoRichiesta.InAttesa, (await _app.ConDb(db => db.RichiesteAssenza.SingleAsync())).Stato);

        // Il referente dell'azienda la approva.
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, _aziendaId);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");
        r = await hr.InviaForm("/Assenze", $"/Assenze/Approva/{richiesta.Id}", new());
        Assert.Contains("Richiesta approvata", await Testo(r));
        Assert.Empty(_app.ErroriServer);
    }
}
