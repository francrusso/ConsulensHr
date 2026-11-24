using CL.Domain.Assenze;
using CL.Domain.Aziende;
using CL.Domain.Dipendenti;
using CL.Domain.Presenze;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CL.Tests.Web;

public sealed class CambiTurnoWebTests : IDisposable
{
    private readonly AppFactory _app = new();
    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Pubblicazione_accordo_scambio_e_protezione_da_richieste_obsolete()
    {
        var (azienda, dip, collega, mioId, altroId) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Scambi Srl", PartitaIva.Parse("01234567897"));
            var d = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 12, 10));
            var e = new Dipendente(a.Id, "Anna", "Verdi", CodiceFiscale.Parse("VRDNNA88C41F839V"), new DateOnly(1988, 3, 1));
            foreach (var p in new[] { d, e }) p.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(2026, 1, 1), null, 28000);
            var mio = new TurnoLavoro(d.Id, new DateTime(2027, 1, 4, 6, 0, 0), new DateTime(2027, 1, 4, 14, 0, 0), "Mattina privata");
            var altro = new TurnoLavoro(e.Id, new DateTime(2027, 1, 4, 14, 0, 0), new DateTime(2027, 1, 4, 22, 0, 0), "Pomeriggio");
            db.AddRange(a, d, e, mio, altro); await db.SaveChangesAsync(); return (a.Id, d.Id, e.Id, mio.Id, altro.Id);
        });
        await _app.CreaUtente("hrscambi@test.it", Ruoli.Azienda, azienda);
        var hr = _app.CreateClient(); await hr.Accedi("hrscambi@test.it");
        var account = await _app.CreaUtente("scambi@test.it", Ruoli.Dipendente);
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var u = (await users.FindByIdAsync(account.Id))!; u.DipendenteId = dip; await users.UpdateAsync(u);
        }
        await _app.CompletaDatiPersonali(dip);
        var client = _app.CreateClient(); await client.Accedi("scambi@test.it");
        Assert.DoesNotContain("Mattina privata", await client.GetStringAsync("/Turni?data=2027-01-04"));
        Assert.DoesNotContain(mioId.ToString(), await client.GetStringAsync("/Assenze/CambioTurno"));
        await hr.InviaForm("/Turni?data=2027-01-04", "/Turni/Pubblica", new() { ["data"] = "2027-01-04" });
        Assert.Contains("Mattina privata", await client.GetStringAsync("/Turni?data=2027-01-04"));
        var campi = new Dictionary<string, string> { ["TurnoId"] = mioId.ToString(), ["Scambio"] = "true", ["CollegaId"] = collega.ToString(), ["TurnoCollegaId"] = altroId.ToString(), ["Note"] = "Scambio concordato" };
        await client.InviaForm("/Assenze/CambioTurno", "/Assenze/CambioTurno", campi);
        Assert.Equal(0, await _app.ConDb(db => db.RichiesteCambioTurno.CountAsync()));
        campi["AccordoCollega"] = "true";
        await client.InviaForm("/Assenze/CambioTurno", "/Assenze/CambioTurno", campi);
        var richiesta = await _app.ConDb(db => db.RichiesteCambioTurno.SingleAsync());
        Assert.Equal(dip, await _app.ConDb(async db => (await db.TurniLavoro.SingleAsync(t => t.Id == mioId)).DipendenteId));
        Assert.Contains("Modifica / scambio turno", await hr.GetStringAsync("/Assenze?anno=2027"));
        await hr.InviaForm("/Assenze?anno=2027", $"/Assenze/DecidiCambioTurno/{richiesta.Id}", new() { ["approva"] = "true" });
        Assert.Equal(StatoRichiesta.Approvata, await _app.ConDb(async db => (await db.RichiesteCambioTurno.SingleAsync()).Stato));
        Assert.Equal(collega, await _app.ConDb(async db => (await db.TurniLavoro.SingleAsync(t => t.Id == mioId)).DipendenteId));
        Assert.Equal(dip, await _app.ConDb(async db => (await db.TurniLavoro.SingleAsync(t => t.Id == altroId)).DipendenteId));
        var modifica = new Dictionary<string, string> { ["TurnoId"] = altroId.ToString(), ["NuovoInizio"] = "2027-01-05T09:00", ["NuovaFine"] = "2027-01-05T17:00", ["Note"] = "Cambio orario" };
        await client.InviaForm("/Assenze/CambioTurno", "/Assenze/CambioTurno", modifica);
        var nuova = await _app.ConDb(db => db.RichiesteCambioTurno.SingleAsync(r => r.Stato == StatoRichiesta.InAttesa));
        await _app.ConDb(async db => { var t = await db.TurniLavoro.SingleAsync(t => t.Id == altroId); t.Modifica(t.Inizio.AddHours(1), t.Fine.AddHours(1), null); t.Pubblica(DateTime.UtcNow); return await db.SaveChangesAsync(); });
        await hr.InviaForm("/Assenze?anno=2027", $"/Assenze/DecidiCambioTurno/{nuova.Id}", new() { ["approva"] = "true" });
        Assert.Equal(StatoRichiesta.InAttesa, await _app.ConDb(async db => (await db.RichiesteCambioTurno.SingleAsync(r => r.Id == nuova.Id)).Stato));
        Assert.Equal(new DateTime(2027, 1, 4, 15, 0, 0), await _app.ConDb(async db => (await db.TurniLavoro.SingleAsync(t => t.Id == altroId)).Inizio));
        await hr.InviaForm("/Assenze?anno=2027", $"/Assenze/DecidiCambioTurno/{nuova.Id}", new() { ["approva"] = "false", ["motivo"] = "Turno aggiornato" });
        Assert.Equal(StatoRichiesta.Rifiutata, await _app.ConDb(async db => (await db.RichiesteCambioTurno.SingleAsync(r => r.Id == nuova.Id)).Stato));
        Assert.Empty(_app.ErroriServer);
    }
}
