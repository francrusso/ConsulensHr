using System.Net;
using CL.Domain.Aziende;
using CL.Domain.Dipendenti;
using CL.Domain.Presenze;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace CL.Tests.Web;

public sealed class TurniWebTests : IDisposable
{
    private readonly AppFactory _app = new();
    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Azienda_gestisce_solo_i_propri_turni_e_blocca_sovrapposizioni()
    {
        var (azienda, dip, esterno) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Turni Srl", PartitaIva.Parse("01234567897"));
            var b = new Azienda("Altra Srl", PartitaIva.Parse("12345678903"));
            var d = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 12, 10));
            var e = new Dipendente(b.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 12, 10));
            d.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(2026, 1, 1), null, 28000);
            db.AddRange(a, b, d, e);
            var t = new TurnoLavoro(e.Id, new DateTime(2026, 11, 24, 8, 0, 0), new DateTime(2026, 11, 24, 16, 0, 0), "Riservato");
            db.Add(t);
            await db.SaveChangesAsync();
            return (a.Id, d.Id, t.Id);
        });
        await _app.CreaUtente("turni@test.it", Ruoli.Azienda, azienda);
        var client = _app.CreateClient();
        await client.Accedi("turni@test.it");
        var campi = new Dictionary<string, string> { ["DipendenteId"] = dip.ToString(), ["Inizio"] = "2026-11-24T22:00", ["Fine"] = "2026-11-25T06:00", ["Note"] = "Turno notte" };
        var risposta = await client.InviaForm("/Turni/Modifica", "/Turni/Modifica", campi);
        Assert.Equal(HttpStatusCode.OK, risposta.StatusCode);
        Assert.Contains("Turno notte", WebUtility.HtmlDecode(await risposta.Content.ReadAsStringAsync()));
        await client.InviaForm("/Turni/Modifica", "/Turni/Modifica", campi);
        Assert.Equal(1, await _app.ConDb(db => db.TurniLavoro.CountAsync(t => t.DipendenteId == dip)));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Turni/Modifica/{esterno}")).StatusCode);
        Assert.DoesNotContain("Riservato", await client.GetStringAsync("/Turni?data=2026-11-24"));
        await client.InviaForm("/Turni?data=2026-11-24", "/Turni/Pubblica", new() { ["data"] = "2026-11-23" });
        var account = await _app.CreaUtente("dipturni@test.it", Ruoli.Dipendente);
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var persona = (await users.FindByIdAsync(account.Id))!;
            persona.DipendenteId = dip;
            await users.UpdateAsync(persona);
        }
        await _app.CompletaDatiPersonali(dip);
        var dipendente = _app.CreateClient();
        await dipendente.Accedi("dipturni@test.it");
        var miei = await dipendente.GetStringAsync("/Turni?data=2026-11-24");
        Assert.Contains("Turno notte", miei);
        Assert.DoesNotContain("Riservato", miei);
        Assert.Contains("Turno", await dipendente.GetStringAsync("/Ufficio?anno=2026&mese=11"));
        Assert.DoesNotContain("Nuovo turno", miei);
        Assert.Contains("/Account/AccessoNegato", (await dipendente.GetAsync("/Turni/Modifica")).RequestMessage!.RequestUri!.AbsolutePath);
        var t = await _app.ConDb(db => db.TurniLavoro.SingleAsync(t => t.DipendenteId == dip));
        campi["Id"] = t.Id.ToString(); campi["Note"] = "Aggiornato";
        await client.InviaForm($"/Turni/Modifica/{t.Id}", "/Turni/Modifica", campi);
        Assert.Equal("Aggiornato", await _app.ConDb(async db => (await db.TurniLavoro.SingleAsync(x => x.Id == t.Id)).Note));
        await client.InviaForm("/Turni?data=2026-11-24", "/Turni/Elimina", new() { ["id"] = t.Id.ToString() });
        Assert.False(await _app.ConDb(db => db.TurniLavoro.AnyAsync(x => x.Id == t.Id)));
        var modello = new Dictionary<string, string> { ["AziendaId"] = azienda.ToString(), ["Nome"] = "Mattina", ["Inizio"] = "06:00", ["Fine"] = "14:00", ["Giorni[0]"] = "Monday" };
        await client.InviaForm("/Turni/Modello", "/Turni/Modello", modello);
        modello["Nome"] = "Notte"; modello["Inizio"] = "22:00"; modello["Fine"] = "06:00";
        await client.InviaForm("/Turni/Modello", "/Turni/Modello", modello);
        var salvati = await _app.ConDb(db => db.TurniSalvati.OrderBy(x => x.Nome).ToListAsync());
        Assert.Equal(2, salvati.Count);
        var pianifica = new Dictionary<string, string> { ["DipendenteId"] = dip.ToString(), ["Pianifica"] = "true", ["Dal"] = "2026-11-02", ["Al"] = "2026-11-23", ["Giorni[0]"] = "Monday", ["TurnoSalvatoId"] = salvati[0].Id.ToString(), ["SecondoTurnoId"] = salvati[1].Id.ToString() };
        await client.InviaForm("/Turni/Modifica", "/Turni/Modifica", pianifica);
        var assegnati = await _app.ConDb(db => db.TurniLavoro.Where(x => x.DipendenteId == dip).OrderBy(x => x.Inizio).ToListAsync());
        Assert.Equal(new[] { "Mattina", "Notte", "Mattina", "Notte" }, assegnati.Select(x => x.Nome));
        Assert.Equal(new DateTime(2026, 11, 10, 6, 0, 0), assegnati[1].Fine);
        pianifica["Al"] = "2026-11-30";
        var conflitto = await client.InviaForm("/Turni/Modifica", "/Turni/Modifica", pianifica);
        Assert.Contains("Nessun turno", await conflitto.Content.ReadAsStringAsync());
        Assert.Equal(4, await _app.ConDb(db => db.TurniLavoro.CountAsync(x => x.DipendenteId == dip)));
        var griglia = await client.GetStringAsync("/Turni?data=2026-11-09");
        Assert.Contains("turni-griglia", griglia);
        Assert.Contains("dipendenteId=" + dip, griglia);
        Assert.Contains("Notte", griglia);
        Assert.DoesNotContain("Notte", await dipendente.GetStringAsync("/Turni?data=2026-11-09"));
        await client.InviaForm("/Turni?data=2026-11-09", "/Turni/Pubblica", new() { ["data"] = "2026-11-09" });
        Assert.Contains("Notte", await dipendente.GetStringAsync("/Turni?data=2026-11-09"));
        await client.InviaForm("/Turni/Salvati", "/Turni/EliminaModello", new() { ["id"] = salvati[1].Id.ToString() });
        Assert.Equal(4, await _app.ConDb(db => db.TurniLavoro.CountAsync(x => x.DipendenteId == dip)));

    }

    [Fact]
    public async Task Ciclo_giornaliero_multiselezione_team_e_turno_personalizzato()
    {
        var (azienda, primo, secondo, teamId, secondoTeamId) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Cicli Srl", PartitaIva.Parse("01234567897"));
            var team = new CL.Domain.Organizzazione.Team(a.Id, "Produzione");
            var secondoTeam = new CL.Domain.Organizzazione.Team(a.Id, "Ufficio");
            var d = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 12, 10));
            var e = new Dipendente(a.Id, "Luigi", "Verdi", CodiceFiscale.Parse("VRDNNA88C41F839V"), new DateOnly(1990, 1, 1));
            foreach (var persona in new[] { d, e }) persona.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(2026, 1, 1), null, 28000);
            d.ImpostaOrganizzazione(team, null, null); e.ImpostaOrganizzazione(secondoTeam, null, null);
            db.AddRange(a, team, secondoTeam, d, e); await db.SaveChangesAsync(); return (a.Id, d.Id, e.Id, team.Id, secondoTeam.Id);
        });
        await _app.CreaUtente("cicli@test.it", Ruoli.Azienda, azienda);
        var client = _app.CreateClient(); await client.Accedi("cicli@test.it");
        var campi = new Dictionary<string, string> { ["AziendaId"] = azienda.ToString(), ["Nome"] = "Rotazione", ["Ciclico"] = "true", ["TeamId"] = teamId.ToString() };
        for (var i = 0; i < 7; i++) campi[$"Giorni[{i}]"] = ((DayOfWeek)i).ToString();
        var nomi = new[] { "Mattina", "Pomeriggio", "Notte", "Smonto", "Riposo" };
        for (var i = 0; i < 5; i++)
        {
            campi[$"Fasi[{i}].Nome"] = nomi[i];
            campi[$"Fasi[{i}].Tipo"] = i < 3 ? "Lavoro" : nomi[i];
            if (i < 3) { campi[$"Fasi[{i}].Inizio"] = new[] { "06:00", "14:00", "22:00" }[i]; campi[$"Fasi[{i}].Fine"] = new[] { "14:00", "22:00", "06:00" }[i]; }
        }
        await client.InviaForm("/Turni/Modello", "/Turni/Modello", campi);
        var modello = await _app.ConDb(db => db.TurniSalvati.SingleAsync());
        Assert.Equal(5, modello.Fasi.Count);
        Assert.Equal(teamId, modello.TeamId);
        var assegnazione = new Dictionary<string, string> { ["DipendentiIds[0]"] = primo.ToString(), ["DipendentiIds[1]"] = secondo.ToString(), ["Dal"] = "2026-12-01", ["Al"] = "2026-12-10", ["Pianifica"] = "true", ["TurnoSalvatoId"] = modello.Id.ToString() };
        var negato = await client.InviaForm("/Turni/Modifica", "/Turni/Modifica", assegnazione);
        Assert.Contains("team", await negato.Content.ReadAsStringAsync());
        Assert.Equal(0, await _app.ConDb(db => db.TurniLavoro.CountAsync()));
        campi["Id"] = modello.Id.ToString(); campi["TeamId"] = ""; campi["TeamIds[0]"] = teamId.ToString(); campi["TeamIds[1]"] = secondoTeamId.ToString();
        await client.InviaForm($"/Turni/Modello/{modello.Id}", "/Turni/Modello", campi);
        await client.InviaForm("/Turni/Modifica", "/Turni/Modifica", assegnazione);
        var turni = await _app.ConDb(db => db.TurniLavoro.OrderBy(t => t.Inizio).ToListAsync());
        Assert.Equal(20, turni.Count);
        Assert.All(turni, t => Assert.Equal(2, t.TeamConsentiti.Count));
        Assert.Equal(96, turni.Sum(t => t.OreLavorate));
        Assert.Equal(4, turni.Count(t => t.Tipo == TipoGiornataTurno.Riposo));
        Assert.Contains("Smonto", await client.GetStringAsync("/Turni?data=2026-12-01"));
        assegnazione["Personalizzato"] = "true"; assegnazione["Dal"] = "2026-12-14"; assegnazione["Al"] = "2026-12-15";
        assegnazione["Giorni[0]"] = "Monday"; assegnazione["OraInizio"] = "09:00"; assegnazione["OraFine"] = "17:00";
        await client.InviaForm("/Turni/Modifica", "/Turni/Modifica", assegnazione);
        Assert.Equal(22, await _app.ConDb(db => db.TurniLavoro.CountAsync()));
        // Un conflitto per un solo dipendente annulla l'intero invio multiplo.
        await client.InviaForm("/Turni/Modifica", "/Turni/Modifica", assegnazione);
        Assert.Equal(22, await _app.ConDb(db => db.TurniLavoro.CountAsync()));
        Assert.Empty(_app.ErroriServer);
    }

    [Fact]
    public async Task Viste_filtri_e_assenze_bloccano_pubblicazione_e_pianificazione()
    {
        var (azienda, dip, team, altroTeam) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Assenze turni", PartitaIva.Parse("01234567897"));
            var t = new CL.Domain.Organizzazione.Team(a.Id, "Produzione"); var altro = new CL.Domain.Organizzazione.Team(a.Id, "Ufficio");
            var d = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 12, 10));
            d.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(2026, 1, 1), null, 28000);
            d.ImpostaOrganizzazione(t, null, null); d.ImpostaSede(SedeLavoro.SedeOperativa);
            var ferie = CL.Domain.Assenze.PianificatoreAssenze.Crea(d, a, CL.Domain.Assenze.TipoAssenza.Ferie, new DateOnly(2026, 12, 14), new DateOnly(2026, 12, 14), [], DateTime.UtcNow);
            ferie.Approva("hr", DateTime.UtcNow);
            db.AddRange(a, t, altro, d, ferie, new TurnoLavoro(d.Id, new DateTime(2026, 12, 14, 9, 0, 0), new DateTime(2026, 12, 14, 17, 0, 0), null));
            await db.SaveChangesAsync(); return (a.Id, d.Id, t.Id, altro.Id);
        });
        await _app.CreaUtente("filtriturni@test.it", Ruoli.Azienda, azienda);
        var client = _app.CreateClient(); await client.Accedi("filtriturni@test.it");
        Assert.Contains("Ferie", await client.GetStringAsync("/Turni?data=2026-12-14&vista=giorno"));
        Assert.Contains("31 DIC", (await client.GetStringAsync("/Turni?data=2026-12-14&vista=mese")).ToUpperInvariant());
        Assert.Contains("Foto di Rossi Mario", await client.GetStringAsync($"/Turni?data=2026-12-14&teamId={team}&sede=SedeOperativa"));
        Assert.DoesNotContain("Foto di Rossi Mario", await client.GetStringAsync($"/Turni?data=2026-12-14&teamId={altroTeam}"));
        await client.InviaForm("/Turni?data=2026-12-14", "/Turni/Pubblica", new() { ["data"] = "2026-12-14" });
        Assert.Null(await _app.ConDb(async db => (await db.TurniLavoro.SingleAsync()).PubblicatoIl));
        var form = new Dictionary<string, string> { ["DipendentiIds[0]"] = dip.ToString(), ["Dal"] = "2026-12-14", ["Al"] = "2026-12-14", ["Pianifica"] = "true", ["Personalizzato"] = "true", ["Giorni[0]"] = "Monday", ["OraInizio"] = "18:00", ["OraFine"] = "20:00" };
        await client.InviaForm("/Turni/Modifica", "/Turni/Modifica", form);
        Assert.Equal(1, await _app.ConDb(db => db.TurniLavoro.CountAsync()));
        Assert.Empty(_app.ErroriServer);
    }
}
