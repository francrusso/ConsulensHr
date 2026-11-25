using System.ComponentModel.DataAnnotations;
using CL.Domain.Presenze;
using CL.Domain.Assenze;
using CL.Domain.Common;
using CL.Domain.Organizzazione;
using CL.Domain.Dipendenti;
using CL.Domain.Notifiche;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

public sealed class TurnoForm
{
    public Guid? Id { get; set; }
    public List<Guid> DipendentiIds { get; set; } = [];
    public bool Personalizzato { get; set; }
    public TimeOnly? OraInizio { get; set; }
    public TimeOnly? OraFine { get; set; }
    public TipoGiornataTurno Tipo { get; set; }
    [Required] public Guid DipendenteId { get; set; }
    public DateTime? Inizio { get; set; }
    public DateTime? Fine { get; set; }
    [StringLength(500)] public string? Note { get; set; }
    public bool Pianifica { get; set; }
    public DateOnly Dal { get; set; }
    public DateOnly Al { get; set; }
    public List<DayOfWeek> Giorni { get; set; } = [];
    public Guid? TurnoSalvatoId { get; set; }
    public Guid? SecondoTurnoId { get; set; }
    public Guid? TerzoTurnoId { get; set; }
    public Dictionary<Guid, Guid> AziendePersone { get; set; } = [];
    public List<TurnoSalvato> Salvati { get; set; } = [];
    public List<SelectListItem> Persone { get; set; } = [];
}
public sealed record TurnoRiga(TurnoLavoro Turno, string Persona, string Azienda);
public sealed record PersonaTurni(Guid Id, string Nome, string Azienda, string? Posizione, bool HaFoto, string Segnaposto, Guid? TeamId)
{
    public string? Team { get; init; }
    public string? EmojiTeam { get; init; }
    public string ColoreTeam { get; init; } = "#D3D7DD";
    public string TestoTeam => CL.Domain.Organizzazione.Team.TestoSu(ColoreTeam);
}
public sealed record TurniViewModel(DateOnly Settimana, Guid? DipendenteId, List<SelectListItem> Persone, List<PersonaTurni> Righe, List<TurnoRiga> Turni, bool Gestione)
{
    public string Vista { get; init; } = "settimana";
    public int GiorniVista { get; init; } = 7;
    public Guid? TeamId { get; init; }
    public SedeLavoro? Sede { get; init; }
    public List<SelectListItem> Team { get; init; } = [];
    public List<RichiestaAssenza> Assenze { get; init; } = [];
    public double OreEffettive(TurnoLavoro turno)
    {
        if (turno.Tipo != TipoGiornataTurno.Lavoro) return 0;
        var dal = Settimana.ToDateTime(TimeOnly.MinValue); var al = dal.AddDays(GiorniVista);
        var inizio = turno.Inizio > dal ? turno.Inizio : dal; var fine = turno.Fine < al ? turno.Fine : al;
        double ore = 0;
        for (var data = inizio.Date; data < fine; data = data.AddDays(1))
        {
            var giorno = DateOnly.FromDateTime(data);
            if (Assenze.Any(a => a.DipendenteId == turno.DipendenteId && a.Stato == StatoRichiesta.Approvata && !a.AOre && a.Dal <= giorno && a.Al >= giorno)) continue;
            ore += ((fine < data.AddDays(1) ? fine : data.AddDays(1)) - (inizio > data ? inizio : data)).TotalHours;
        }
        return ore;
    }
}
public sealed class TurnoSalvatoForm
{
    public Guid? Id { get; set; }
    public Guid AziendaId { get; set; }
    [Required, StringLength(80)] public string Nome { get; set; } = "";
    public TimeOnly? Inizio { get; set; }
    public TimeOnly? Fine { get; set; }
    public List<DayOfWeek> Giorni { get; set; } = [];
    public Guid? TeamId { get; set; }
    public List<Guid> TeamIds { get; set; } = [];
    public bool Ciclico { get; set; }
    public List<FaseTurnoForm> Fasi { get; set; } = [];
    public List<SelectListItem> Team { get; set; } = [];
    public List<SelectListItem> Aziende { get; set; } = [];
}
public sealed class FaseTurnoForm
{
    public string? Nome { get; set; }
    public TipoGiornataTurno Tipo { get; set; }
    public TimeOnly? Inizio { get; set; }
    public TimeOnly? Fine { get; set; }
}
public sealed record TurniSalvatiViewModel(List<TurnoSalvato> Turni, Dictionary<Guid, string> Aziende);

[Authorize]
public sealed class TurniController(AppDbContext db, UtenteCorrente utente, ServizioNotifiche notifiche, TimeProvider clock) : AppController
{
    public async Task<IActionResult> Index(int? anno, int? mese, Guid? dipendenteId, DateOnly? data, Guid? teamId, SedeLavoro? sede, string? vista)
    {
        var oggi = clock.GetLocalNow();
        var primo = new DateOnly(anno is >= 2 and <= 9998 ? anno.Value : oggi.Year, mese is >= 1 and <= 12 ? mese.Value : oggi.Month, 1);
        var riferimento = data is { Year: >= 2 and <= 9998 } ? data.Value : (anno.HasValue || mese.HasValue ? primo : DateOnly.FromDateTime(oggi.DateTime));
        var vistaScelta = vista is "giorno" or "mese" ? vista : "settimana";
        var settimana = vistaScelta == "giorno" ? riferimento : vistaScelta == "mese" ? new DateOnly(riferimento.Year, riferimento.Month, 1) : riferimento.AddDays(-(((int)riferimento.DayOfWeek + 6) % 7));
        var giorniVista = vistaScelta == "giorno" ? 1 : vistaScelta == "mese" ? DateTime.DaysInMonth(riferimento.Year, riferimento.Month) : 7;
        var dal = settimana.ToDateTime(TimeOnly.MinValue);
        var al = dal.AddDays(giorniVista);
        var persone = utente.Filtra(db.Dipendenti.AsNoTracking());
        if (teamId is { } filtroTeam) persone = persone.Where(d => d.TeamId == filtroTeam);
        if (sede is { } filtroSede) persone = persone.Where(d => d.Sede == filtroSede);
        var turni = await (from t in db.TurniLavoro.AsNoTracking()
                           join d in persone on t.DipendenteId equals d.Id
                           join a in db.Aziende on d.AziendaId equals a.Id
                           where (t.PubblicatoIl != null || utente.IsAzienda || utente.IsConsulente) && t.Inizio < al && t.Fine > dal && (dipendenteId == null || d.Id == dipendenteId)
                           orderby t.Inizio, d.Cognome
                           select new TurnoRiga(t, d.Cognome + " " + d.Nome, a.RagioneSociale)).ToListAsync();
        var righe = await (from d in persone join a in db.Aziende on d.AziendaId equals a.Id
            where dipendenteId == null || d.Id == dipendenteId orderby a.RagioneSociale, d.Cognome, d.Nome
            select new PersonaTurni(d.Id, d.Cognome + " " + d.Nome, a.RagioneSociale, d.Posizione, d.FotoChiave != null, Dipendente.SegnapostoPer(d.Genere, d.Pronomi), d.TeamId)).ToListAsync();
        var team = await db.Team.AsNoTracking().ToListAsync();
        righe = righe.Select(r => team.FirstOrDefault(t => t.Id == r.TeamId) is { } t
            ? r with { Team = t.Nome, EmojiTeam = t.Emoji, ColoreTeam = Organigramma.ColoreDi(t, team) } : r).ToList();
        var idsRighe = righe.Select(r => r.Id).ToList();
        var inizioAssenze = DateOnly.FromDateTime(dal); var fineAssenze = DateOnly.FromDateTime(al.AddDays(-1));
        var assenze = await db.RichiesteAssenza.AsNoTracking().Where(a => idsRighe.Contains(a.DipendenteId) && a.Dal <= fineAssenze && a.Al >= inizioAssenze && (a.Stato == StatoRichiesta.InAttesa || a.Stato == StatoRichiesta.Approvata)).ToListAsync();
        var aziende = await utente.Filtra(db.Dipendenti).Select(d => d.AziendaId).Distinct().ToListAsync();
        return View(new TurniViewModel(settimana, dipendenteId, await Persone(), righe, turni, utente.IsAzienda || utente.IsConsulente)
        {
            Vista = vistaScelta, GiorniVista = giorniVista, TeamId = teamId, Sede = sede, Assenze = assenze,
            Team = team.Where(t => aziende.Contains(t.AziendaId)).Select(t => new SelectListItem(t.Nome, t.Id.ToString())).ToList()
        });
    }

    [Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> Modifica(Guid? id, Guid? dipendenteId, DateOnly? giorno)
    {
        var data = giorno is { Year: >= 2 and <= 9998 } ? giorno.Value : DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var form = new TurnoForm { OraInizio = new TimeOnly(9, 0), OraFine = new TimeOnly(17, 0), DipendentiIds = dipendenteId is { } persona ? [persona] : [], DipendenteId = dipendenteId ?? Guid.Empty, Dal = data, Al = data.AddDays(6 - (((int)data.DayOfWeek + 6) % 7)),
            Inizio = data.ToDateTime(new TimeOnly(9, 0)), Fine = data.ToDateTime(new TimeOnly(17, 0)),
            Giorni = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday] };
        if (id is { } valore)
        {
            var turno = await Visibili().FirstOrDefaultAsync(t => t.Id == valore);
            if (turno is null) return NotFound();
            form.Id = turno.Id; form.DipendenteId = turno.DipendenteId;
            form.Inizio = turno.Inizio; form.Fine = turno.Fine; form.Note = turno.Note; form.Tipo = turno.Tipo;
        }
        await CaricaForm(form);
        if (id is null) { form.Pianifica = true; form.Personalizzato = form.Salvati.Count == 0; }
        return View(form);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> Modifica(TurnoForm form)
    {
        var idsPersone = (form.Id is not null || form.DipendentiIds.Count == 0
            ? (form.DipendenteId != Guid.Empty ? new List<Guid> { form.DipendenteId } : []) : form.DipendentiIds).Distinct().ToList();
        if (idsPersone.Count == 0) ModelState.AddModelError("", "Seleziona almeno un dipendente.");
        if (idsPersone.Count > 200) ModelState.AddModelError("", "Seleziona al massimo 200 dipendenti alla volta.");
        var persone = await utente.Filtra(db.Dipendenti).Where(d => idsPersone.Contains(d.Id)).ToListAsync();
        if (persone.Count != idsPersone.Count) return NotFound();
        var esistente = form.Id is { } id ? await Visibili().FirstOrDefaultAsync(t => t.Id == id) : null;
        if (form.Id is not null && (esistente is null || esistente.DipendenteId != form.DipendenteId)) return NotFound();
        if (ModelState.IsValid)
        {
            var nuovi = new List<TurnoLavoro>();
            var idsModelli = new[] { form.TurnoSalvatoId, form.SecondoTurnoId, form.TerzoTurnoId }.Where(x => x.HasValue).Select(x => x!.Value).ToList();
            var modelli = await ModelliVisibili().Where(t => idsModelli.Contains(t.Id)).ToListAsync();
            Prova(() =>
            {
                foreach (var d in persone)
                {
                    if (form.Pianifica && form.Id is null)
                    {
                        if (form.Personalizzato)
                        {
                            if (form.OraInizio is null || form.OraFine is null) throw new DomainException("Indica gli orari del turno personalizzato.");
                            var modello = new TurnoSalvato(d.AziendaId, "Turno", form.OraInizio.Value, form.OraFine.Value);
                            var maschera = MascheraGiorni(form.Giorni);
                            modello.Configura(maschera, null, []);
                            nuovi.AddRange(modello.Pianifica(d.Id, d.TeamId, form.Dal, form.Al, form.Note));
                        }
                        else
                        {
                            if (form.TurnoSalvatoId is null || idsModelli.Any(id => modelli.All(t => t.Id != id || t.AziendaId != d.AziendaId)))
                                throw new DomainException("Scegli un turno salvato dell'azienda di tutti i dipendenti selezionati.");
                            if (modelli.Any(t => t.TeamConsentiti.Count > 0 && (d.TeamId is null || !t.TeamConsentiti.Contains(d.TeamId.Value))))
                                throw new DomainException($"Il turno selezionato non è disponibile per il team di {d.NomeCompleto}.");
                            if (idsModelli.Count == 1)
                                nuovi.AddRange(modelli.Single(t => t.Id == form.TurnoSalvatoId).Pianifica(d.Id, d.TeamId, form.Dal, form.Al, form.Note));
                            else
                            {
                                if (modelli.Any(t => t.Ciclico)) throw new DomainException("Un ciclo giornaliero va assegnato da solo.");
                                nuovi.AddRange(PianificazioneTurni.Crea(d.Id, form.Dal, form.Al, form.Giorni, idsModelli.Select(id => modelli.Single(t => t.Id == id)).ToList(), form.Note));
                            }
                        }
                    }
                    else
                    {
                        if (form.Inizio is null || form.Fine is null) throw new DomainException("Indica inizio e fine del turno.");
                        nuovi.Add(esistente is { Tipo: not TipoGiornataTurno.Lavoro }
                            ? TurnoLavoro.NonLavorato(d.Id, DateOnly.FromDateTime(form.Inizio.Value), esistente.Tipo, esistente.Nome ?? esistente.Tipo.ToString(), form.Note)
                            : new TurnoLavoro(d.Id, form.Inizio.Value, form.Fine.Value, form.Note));
                    }
                }
            });
            if (ModelState.IsValid)
            {
                var perId = persone.ToDictionary(d => d.Id);
                if (nuovi.Any(t => !perId[t.DipendenteId].InForzaIl(DateOnly.FromDateTime(t.Inizio)) || !perId[t.DipendenteId].InForzaIl(DateOnly.FromDateTime(t.Fine.AddTicks(-1)))))
                    ModelState.AddModelError("", "Tutte le giornate devono ricadere in un rapporto di lavoro attivo.");
                // Include anche lo smonto successivo a un notturno e i riposi della stessa giornata.
                var dal = nuovi.Min(t => t.Inizio).Date; var al = nuovi.Max(t => t.Fine).Date.AddDays(1);
                if (await ConAssenzeApprovate(nuovi)) ModelState.AddModelError("", "La pianificazione coincide con assenze approvate. Nessun turno è stato salvato.");
                var esistenti = await db.TurniLavoro.Where(t => idsPersone.Contains(t.DipendenteId) && t.Id != form.Id && t.Inizio < al && t.Fine > dal).ToListAsync();
                if (nuovi.Any(t => esistenti.Any(t.Sovrapposto)) || nuovi.GroupBy(t => t.DipendenteId).Any(g => g.Any(t => g.Any(x => x != t && t.Sovrapposto(x)))))
                    ModelState.AddModelError("", "La pianificazione contiene turni sovrapposti. Nessun turno è stato salvato.");
                if (ModelState.IsValid)
                {
                    if (esistente is null) db.TurniLavoro.AddRange(nuovi);
                    else esistente.Modifica(nuovi[0].Inizio, nuovi[0].Fine, nuovi[0].Note);
                    await db.SaveChangesAsync();
                    Conferma($"Pianificazione salvata: {nuovi.Count} giornate per {persone.Count} dipendenti.");
                    return RedirectToAction(nameof(Index), new { data = DateOnly.FromDateTime(nuovi[0].Inizio).ToString("yyyy-MM-dd") });
                }
            }
        }
        await CaricaForm(form);
        return View(form);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> Pubblica(DateOnly data, Guid? dipendenteId, Guid? teamId, SedeLavoro? sede, string? vista)
    {
        if (data.Year is < 2 or > 9998) return BadRequest();
        var dal = data.ToDateTime(TimeOnly.MinValue);
        var giorni = vista == "giorno" ? 1 : vista == "mese" ? DateTime.DaysInMonth(data.Year, data.Month) : 7;
        var al = dal.AddDays(giorni);
        var persone = utente.Filtra(db.Dipendenti).Where(d => (teamId == null || d.TeamId == teamId) && (sede == null || d.Sede == sede)).Select(d => d.Id);
        var turni = await Visibili().Where(t => persone.Contains(t.DipendenteId) && t.Inizio < al && t.Fine > dal && t.PubblicatoIl == null && (dipendenteId == null || t.DipendenteId == dipendenteId)).ToListAsync();
        if (await ConAssenzeApprovate(turni))
        {
            Errore("Sono presenti turni in conflitto con assenze approvate. Correggi la pianificazione prima di pubblicare.");
            return RedirectToAction(nameof(Index), new { data = data.ToString("yyyy-MM-dd"), dipendenteId, teamId, sede, vista });
        }
        foreach (var turno in turni) turno.Pubblica(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync();
        // Ogni persona riceve una notifica con il periodo dei suoi turni appena pubblicati.
        var accessi = (await db.PersoneAsync(turni.Select(t => t.DipendenteId))).ToLookup(p => p.DipendenteId);
        await notifiche.InviaAsync(TipoNotifica.TurniPubblicati, turni.GroupBy(t => t.DipendenteId).SelectMany(g => accessi[g.Key].Select(p =>
        {
            var (primo, ultimo) = (g.Min(t => t.Inizio), g.Max(t => t.Inizio));
            var quando = primo.Date == ultimo.Date ? $"per il {primo:dd/MM/yyyy}" : $"dal {primo:dd/MM/yyyy} al {ultimo:dd/MM/yyyy}";
            return new MessaggioNotifica(p, "Nuovi turni pubblicati",
                $"L'azienda ha pubblicato {(g.Count() == 1 ? "un tuo turno" : $"{g.Count()} tuoi turni")} {quando}.", "/Turni");
        })));
        Conferma($"Pubblicati {turni.Count} turni del periodo selezionato. I dipendenti possono consultarli.");
        return RedirectToAction(nameof(Index), new { data = data.ToString("yyyy-MM-dd"), dipendenteId, teamId, sede, vista });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> Elimina(Guid id)
    {
        var turno = await Visibili().FirstOrDefaultAsync(t => t.Id == id);
        if (turno is null) return NotFound();
        db.TurniLavoro.Remove(turno);
        await db.SaveChangesAsync();
        Conferma("Turno eliminato.");
        return RedirectToAction(nameof(Index), new { data = DateOnly.FromDateTime(turno.Inizio).ToString("yyyy-MM-dd") });
    }

    [Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> Salvati()
    {
        var aziende = await AziendeVisibili().ToDictionaryAsync(a => a.Id, a => a.RagioneSociale);
        return View(new TurniSalvatiViewModel(await db.TurniSalvati.AsNoTracking().Where(t => aziende.Keys.Contains(t.AziendaId)).OrderBy(t => t.Nome).ToListAsync(), aziende));
    }

    [Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> Modello(Guid? id)
    {
        var form = new TurnoSalvatoForm { AziendaId = utente.AziendaId ?? Guid.Empty,
            Giorni = Enum.GetValues<DayOfWeek>().ToList(), Fasi = [
                new() { Nome = "Mattina", Inizio = new TimeOnly(6, 0), Fine = new TimeOnly(14, 0) },
                new() { Nome = "Pomeriggio", Inizio = new TimeOnly(14, 0), Fine = new TimeOnly(22, 0) },
                new() { Nome = "Notte", Inizio = new TimeOnly(22, 0), Fine = new TimeOnly(6, 0) },
                new() { Nome = "Smonto", Tipo = TipoGiornataTurno.Smonto },
                new() { Nome = "Riposo", Tipo = TipoGiornataTurno.Riposo }] };
        if (id is { } valore)
        {
            var t = await ModelliVisibili().FirstOrDefaultAsync(t => t.Id == valore);
            if (t is null) return NotFound();
            form.Id = t.Id; form.AziendaId = t.AziendaId; form.Nome = t.Nome; form.Inizio = t.Inizio; form.Fine = t.Fine;
            form.TeamId = t.TeamId; form.TeamIds = t.TeamConsentiti.ToList(); form.Ciclico = t.Ciclico;
            form.Giorni = Enum.GetValues<DayOfWeek>().Where(t.Previsto).ToList();
            if (t.Ciclico) form.Fasi = t.Fasi.OrderBy(f => f.Ordine).Select(f => new FaseTurnoForm { Nome = f.Nome, Tipo = f.Tipo, Inizio = f.Inizio, Fine = f.Fine }).ToList();
        }
        await CaricaModello(form);
        return View(form);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> Modello(TurnoSalvatoForm form)
    {
        if (!await AziendeVisibili().AnyAsync(a => a.Id == form.AziendaId)) return NotFound();
        var esistente = form.Id is { } id ? await ModelliVisibili().FirstOrDefaultAsync(t => t.Id == id) : null;
        if (form.Id is not null && (esistente is null || esistente.AziendaId != form.AziendaId)) return NotFound();
        if (await db.TurniSalvati.AnyAsync(t => t.AziendaId == form.AziendaId && t.Id != form.Id && t.Nome.ToLower() == (form.Nome ?? "").Trim().ToLower()))
            ModelState.AddModelError(nameof(form.Nome), "Esiste già un turno con questo nome.");
        var teamIds = form.TeamIds.Count > 0 ? form.TeamIds.Distinct().ToList() : form.TeamId is { } vecchioTeam ? [vecchioTeam] : new List<Guid>();
        if (await db.Team.CountAsync(t => teamIds.Contains(t.Id) && t.AziendaId == form.AziendaId) != teamIds.Count)
            ModelState.AddModelError(nameof(form.TeamId), "Seleziona un team dell'azienda.");
        if (ModelState.IsValid && Prova(() =>
        {
            var fasi = form.Ciclico ? form.Fasi.Select(f => new FaseTurno(f.Nome ?? "", f.Tipo, f.Inizio, f.Fine)).ToList() : new List<FaseTurno>();
            if (form.Ciclico && fasi.Count == 0) throw new DomainException("Aggiungi almeno una giornata al ciclo.");
            if (!form.Ciclico && (form.Inizio is null || form.Fine is null)) throw new DomainException("Indica gli orari del turno.");
            var inizio = form.Ciclico ? new TimeOnly(0, 0) : form.Inizio!.Value;
            var fine = form.Ciclico ? new TimeOnly(1, 0) : form.Fine!.Value;
            var maschera = MascheraGiorni(form.Giorni);
            var modello = new TurnoSalvato(form.AziendaId, form.Nome, inizio, fine);
            modello.ConfiguraPerTeam(maschera, teamIds, fasi);
            if (esistente is null) db.TurniSalvati.Add(modello);
            else { esistente.Modifica(form.Nome, inizio, fine); esistente.ConfiguraPerTeam(maschera, teamIds, fasi); }
        }))
        {
            await db.SaveChangesAsync(); Conferma("Turno salvato nella libreria."); return RedirectToAction(nameof(Salvati));
        }
        await CaricaModello(form); return View(form);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> EliminaModello(Guid id)
    {
        var t = await ModelliVisibili().FirstOrDefaultAsync(t => t.Id == id);
        if (t is null) return NotFound();
        db.TurniSalvati.Remove(t); await db.SaveChangesAsync();
        Conferma("Turno rimosso dalla libreria. Le assegnazioni esistenti restano disponibili.");
        return RedirectToAction(nameof(Salvati));
    }

    private async Task<bool> ConAssenzeApprovate(List<TurnoLavoro> turni)
    {
        var lavoro = turni.Where(t => t.Tipo == TipoGiornataTurno.Lavoro).ToList();
        if (lavoro.Count == 0) return false;
        var ids = lavoro.Select(t => t.DipendenteId).Distinct().ToList();
        var dal = DateOnly.FromDateTime(lavoro.Min(t => t.Inizio)); var al = DateOnly.FromDateTime(lavoro.Max(t => t.Fine));
        var assenze = await db.RichiesteAssenza.AsNoTracking().Where(a => ids.Contains(a.DipendenteId) && a.Stato == StatoRichiesta.Approvata && !a.AOre && a.Dal <= al && a.Al >= dal).ToListAsync();
        return lavoro.Any(t => assenze.Any(a => a.DipendenteId == t.DipendenteId && a.Dal.ToDateTime(TimeOnly.MinValue) < t.Fine && a.Al.AddDays(1).ToDateTime(TimeOnly.MinValue) > t.Inizio));
    }

    private static int MascheraGiorni(List<DayOfWeek> giorni)
    {
        if (giorni.Any(g => !Enum.IsDefined(g))) throw new DomainException("Giorno della settimana non valido.");
        return giorni.Distinct().Aggregate(0, (mask, g) => mask | (1 << (int)g));
    }
    private async Task CaricaModello(TurnoSalvatoForm form)
    {
        form.Aziende = await ElencoAziende();
        var ids = AziendeVisibili().Select(a => a.Id);
        form.Team = await db.Team.Where(t => ids.Contains(t.AziendaId)).OrderBy(t => t.Nome)
            .Select(t => new SelectListItem(t.Nome, t.Id.ToString(), false, false) { Group = new SelectListGroup { Name = t.AziendaId.ToString() } }).ToListAsync();
    }
    private IQueryable<CL.Domain.Aziende.Azienda> AziendeVisibili() => db.Aziende.Where(a => utente.IsConsulente || a.Id == utente.AziendaId);
    private IQueryable<TurnoSalvato> ModelliVisibili()
    {
        var ids = AziendeVisibili().Select(a => a.Id);
        return db.TurniSalvati.Where(t => ids.Contains(t.AziendaId));
    }
    private async Task<List<SelectListItem>> ElencoAziende() => await AziendeVisibili().OrderBy(a => a.RagioneSociale).Select(a => new SelectListItem(a.RagioneSociale, a.Id.ToString())).ToListAsync();
    private async Task CaricaForm(TurnoForm form)
    {
        form.Persone = await Persone();
        form.AziendePersone = await utente.Filtra(db.Dipendenti.AsNoTracking()).ToDictionaryAsync(d => d.Id, d => d.AziendaId);
        var ids = utente.Filtra(db.Dipendenti).Select(d => d.AziendaId);
        form.Salvati = await db.TurniSalvati.AsNoTracking().Where(t => ids.Contains(t.AziendaId)).OrderBy(t => t.Nome).ToListAsync();
    }

    private IQueryable<TurnoLavoro> Visibili()
    {
        var persone = utente.Filtra(db.Dipendenti).Select(d => d.Id);
        return db.TurniLavoro.Where(t => persone.Contains(t.DipendenteId));
    }
    private async Task<List<SelectListItem>> Persone() => await (from d in utente.Filtra(db.Dipendenti.AsNoTracking())
        join a in db.Aziende on d.AziendaId equals a.Id orderby a.RagioneSociale, d.Cognome, d.Nome
        select new SelectListItem(d.Cognome + " " + d.Nome + " · " + a.RagioneSociale, d.Id.ToString())).ToListAsync();
}
