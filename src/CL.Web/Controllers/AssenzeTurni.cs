using System.ComponentModel.DataAnnotations;
using CL.Domain.Assenze;
using CL.Domain.Common;
using CL.Domain.Presenze;
using CL.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

public sealed class CambioTurnoForm
{
    public Guid TurnoId { get; set; }
    public bool Scambio { get; set; }
    public Guid? CollegaId { get; set; }
    public Guid? TurnoCollegaId { get; set; }
    public bool AccordoCollega { get; set; }
    public DateTime? NuovoInizio { get; set; }
    public DateTime? NuovaFine { get; set; }
    [Required, StringLength(1000)] public string Note { get; set; } = "";
    public List<SelectListItem> Turni { get; set; } = [];
    public List<SelectListItem> Colleghi { get; set; } = [];
    public List<OpzioneTurnoCollega> TurniColleghi { get; set; } = [];
}
public sealed record OpzioneTurnoCollega(Guid Id, Guid DipendenteId, string Descrizione);

public sealed partial class AssenzeController
{
    [Authorize(Roles = Ruoli.Dipendente)]
    public async Task<IActionResult> CambioTurno(Guid? turnoId)
    {
        var form = new CambioTurnoForm { TurnoId = turnoId ?? Guid.Empty };
        await CaricaCambio(form); return View(form);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Ruoli.Dipendente)]
    public async Task<IActionResult> CambioTurno(CambioTurnoForm form)
    {
        var mio = await db.TurniLavoro.FirstOrDefaultAsync(t => t.Id == form.TurnoId && t.DipendenteId == utente.DipendenteId && t.PubblicatoIl != null);
        if (mio is null) return NotFound();
        var persona = await db.Dipendenti.FirstAsync(d => d.Id == mio.DipendenteId);
        TurnoLavoro? altro = null;
        if (form.Scambio)
        {
            altro = await (from t in db.TurniLavoro join d in db.Dipendenti on t.DipendenteId equals d.Id
                where t.Id == form.TurnoCollegaId && d.Id == form.CollegaId && d.AziendaId == persona.AziendaId && d.Id != persona.Id && t.PubblicatoIl != null
                select t).FirstOrDefaultAsync();
            if (altro is null) ModelState.AddModelError("", "Seleziona un collega e un suo turno pubblicato.");
        }
        else if (form.NuovoInizio is null || form.NuovaFine is null) ModelState.AddModelError("", "Indica i nuovi orari desiderati.");
        if (mio.Inizio < clock.GetLocalNow().DateTime || (altro is not null && altro.Inizio < clock.GetLocalNow().DateTime))
            ModelState.AddModelError("", "Si possono modificare solo turni non ancora iniziati.");
        if (await db.RichiesteCambioTurno.AnyAsync(r => r.Stato == StatoRichiesta.InAttesa &&
            (r.TurnoId == mio.Id || r.TurnoCollegaId == mio.Id || (altro != null && (r.TurnoId == altro.Id || r.TurnoCollegaId == altro.Id)))))
            ModelState.AddModelError("", "Esiste già una richiesta in attesa per uno dei turni selezionati.");
        RichiestaCambioTurno? richiesta = null;
        if (ModelState.IsValid && Prova(() => richiesta = new RichiestaCambioTurno(mio, altro, form.NuovoInizio ?? mio.Inizio, form.NuovaFine ?? mio.Fine, form.AccordoCollega, form.Note, AdessoUtc)))
        {
            if (richiesta!.NuovoInizio < clock.GetLocalNow().DateTime) ModelState.AddModelError("", "Il nuovo turno non può iniziare nel passato.");
            else
            {
                db.RichiesteCambioTurno.Add(richiesta); await db.SaveChangesAsync();
                await notifiche.CambioTurnoChiestoAsync(richiesta);
                Conferma("Richiesta inviata all'azienda. I turni restano invariati fino all'approvazione.");
                return RedirectToAction(nameof(Index));
            }
        }
        await CaricaCambio(form); return View(form);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Ruoli.Azienda)]
    public async Task<IActionResult> DecidiCambioTurno(Guid id, bool approva, string? motivo, string? ritorno)
    {
        var richiesta = await db.RichiesteCambioTurno.FirstOrDefaultAsync(r => r.Id == id && Perimetro.Contains(r.DipendenteId));
        if (richiesta is null) return NotFound();
        if (approva)
        {
            var mio = await db.TurniLavoro.FirstOrDefaultAsync(t => t.Id == richiesta.TurnoId);
            var altro = richiesta.TurnoCollegaId is { } collegaId ? await db.TurniLavoro.FirstOrDefaultAsync(t => t.Id == collegaId) : null;
            if (mio is null || mio.DipendenteId != richiesta.DipendenteId || mio.Versione != richiesta.VersioneTurno || mio.PubblicatoIl is null ||
                (richiesta.TurnoCollegaId is not null && (altro is null || altro.Versione != richiesta.VersioneCollega || altro.DipendenteId != richiesta.CollegaId || altro.PubblicatoIl is null)))
            { Errore("I turni sono cambiati dopo la richiesta. Rifiutala e chiedi un nuovo invio."); return Torna(ritorno); }
            var candidati = new List<TurnoLavoro>();
            if (!Prova(() =>
            {
                if (mio.Inizio < clock.GetLocalNow().DateTime || richiesta.NuovoInizio < clock.GetLocalNow().DateTime || (altro is not null && altro.Inizio < clock.GetLocalNow().DateTime))
                    throw new DomainException("Il turno è già iniziato o la proposta è nel passato.");
                candidati.Add(new TurnoLavoro(mio.DipendenteId, richiesta.NuovoInizio, richiesta.NuovaFine, mio.Note));
                candidati[0].VincolaTeamMultipli(altro is not null ? altro.TeamConsentiti : mio.TeamConsentiti);
                if (altro is not null)
                {
                    candidati.Add(new TurnoLavoro(altro.DipendenteId, mio.Inizio, mio.Fine, altro.Note));
                    candidati[1].VincolaTeamMultipli(mio.TeamConsentiti);
                }
            })) { Errore(PrimoErrore()); return Torna(ritorno); }
            foreach (var candidato in candidati)
            {
                var d = await db.Dipendenti.FirstOrDefaultAsync(d => d.Id == candidato.DipendenteId && d.AziendaId == utente.AziendaId);
                if (d is null) return NotFound();
                if (!d.InForzaIl(DateOnly.FromDateTime(candidato.Inizio)) || !d.InForzaIl(DateOnly.FromDateTime(candidato.Fine.AddTicks(-1))) || (candidato.TeamConsentiti.Count > 0 && (d.TeamId is null || !candidato.TeamConsentiti.Contains(d.TeamId.Value))))
                { Errore("Lo scambio non rispetta il rapporto di lavoro o il team abilitato."); return Torna(ritorno); }
                var giornoInizio = DateOnly.FromDateTime(candidato.Inizio); var giornoFine = DateOnly.FromDateTime(candidato.Fine.AddTicks(-1));
                if (await db.RichiesteAssenza.AnyAsync(a => a.DipendenteId == d.Id && a.Stato == StatoRichiesta.Approvata && !a.AOre && a.Dal <= giornoFine && a.Al >= giornoInizio))
                { Errore("La modifica coincide con un'assenza approvata."); return Torna(ritorno); }
                var esistenti = await db.TurniLavoro.Where(t => t.DipendenteId == d.Id && t.Id != mio.Id && (altro == null || t.Id != altro.Id) && t.Inizio < candidato.Fine.Date.AddDays(1) && t.Fine > candidato.Inizio.Date).ToListAsync();
                if (esistenti.Any(candidato.Sovrapposto)) { Errore("La modifica crea una sovrapposizione con un altro turno o riposo."); return Torna(ritorno); }
            }
            if (altro is not null)
            {
                var primoDipendente = mio.DipendenteId;
                mio.Riassegna(altro.DipendenteId); altro.Riassegna(primoDipendente);
            }
            else { mio.Modifica(richiesta.NuovoInizio, richiesta.NuovaFine, mio.Note); mio.Pubblica(AdessoUtc); }
        }
        if (!Prova(() => richiesta.Decidi(approva, utente.Email, motivo))) { Errore(PrimoErrore()); return Torna(ritorno); }
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            Errore("I turni o la richiesta sono stati modificati nel frattempo. Aggiorna la pagina.");
            return Torna(ritorno);
        }
        await notifiche.CambioTurnoDecisoAsync(richiesta);
        Conferma(approva ? "Richiesta approvata: calendario aggiornato per i dipendenti coinvolti." : "Richiesta rifiutata.");
        return Torna(ritorno);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Ruoli.Dipendente)]
    public async Task<IActionResult> RitiraCambioTurno(Guid id)
    {
        var r = await db.RichiesteCambioTurno.FirstOrDefaultAsync(r => r.Id == id && r.DipendenteId == utente.DipendenteId);
        if (r is null) return NotFound();
        if (Prova(r.Ritira)) { await db.SaveChangesAsync(); Conferma("Richiesta ritirata."); }
        else Errore(PrimoErrore());
        return RedirectToAction(nameof(Index));
    }

    private async Task CaricaCambio(CambioTurnoForm form)
    {
        var d = await db.Dipendenti.AsNoTracking().FirstOrDefaultAsync(d => d.Id == utente.DipendenteId);
        if (d is null) return;
        var adesso = clock.GetLocalNow().DateTime;
        var turni = await db.TurniLavoro.AsNoTracking().Where(t => t.DipendenteId == d.Id && t.PubblicatoIl != null && t.Tipo == TipoGiornataTurno.Lavoro && t.Inizio >= adesso).OrderBy(t => t.Inizio).ToListAsync();
        form.Turni = turni.Select(t => new SelectListItem($"{t.Nome ?? "Turno"} · {t.Inizio:dd/MM/yyyy HH:mm} – {t.Fine:dd/MM HH:mm}", t.Id.ToString())).ToList();
        form.Colleghi = await db.Dipendenti.AsNoTracking().Where(c => c.AziendaId == d.AziendaId && c.Id != d.Id).OrderBy(c => c.Cognome).Select(c => new SelectListItem(c.Cognome + " " + c.Nome, c.Id.ToString())).ToListAsync();
        var colleghi = form.Colleghi.Select(c => Guid.Parse(c.Value)).ToList();
        var altri = await db.TurniLavoro.AsNoTracking().Where(t => colleghi.Contains(t.DipendenteId) && t.PubblicatoIl != null && t.Tipo == TipoGiornataTurno.Lavoro && t.Inizio >= adesso).OrderBy(t => t.Inizio).ToListAsync();
        form.TurniColleghi = altri.Select(t => new OpzioneTurnoCollega(t.Id, t.DipendenteId, $"{t.Nome ?? "Turno"} · {t.Inizio:dd/MM/yyyy HH:mm} – {t.Fine:dd/MM HH:mm}")).ToList();
    }
}
