using System.ComponentModel.DataAnnotations;
using CL.Domain.Assenze;
using CL.Domain.Dipendenti;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

public sealed class RichiestaForm
{
    [Required(ErrorMessage = "Seleziona il dipendente."), Display(Name = "Dipendente")]
    public Guid? DipendenteId { get; set; }

    [Display(Name = "Tipo di assenza")]
    public TipoAssenza Tipo { get; set; } = TipoAssenza.Ferie;

    [Required(ErrorMessage = "Indica la data di inizio."), Display(Name = "Dal")]
    public DateOnly? Dal { get; set; }

    [Display(Name = "Al")]
    public DateOnly? Al { get; set; }

    [Display(Name = "Permesso a ore (una sola giornata)")]
    public bool AOre { get; set; }

    [Display(Name = "Ore")]
    public decimal? Ore { get; set; }

    [StringLength(1000), Display(Name = "Note")]
    public string? Note { get; set; }

    [StringLength(50), Display(Name = "Protocollo certificato medico")]
    public string? ProtocolloCertificato { get; set; }

    [Display(Name = "Approva subito")]
    public bool ApprovaSubito { get; set; } = true;

    public static IEnumerable<SelectListItem> Tipi =>
        Enum.GetValues<TipoAssenza>().Select(t => new SelectListItem(t.Descrizione(), t.ToString()));
}

public sealed record RichiestaRiga(
    Guid Id, Guid DipendenteId, string Dipendente, string Azienda, TipoAssenza Tipo, DateOnly Dal, DateOnly Al,
    bool AOre, decimal Ore, StatoRichiesta Stato, DateTime RichiestaIl, string? Note, string? DecisaDa, string? MotivoRifiuto);

/// <summary>Persona nel calendario; <paramref name="ColoreTeam"/> fa da bordo alla foto (null senza team).</summary>
public sealed record PersonaCalendario(Guid Id, string Nome, bool HaFoto, string Segnaposto, string? ColoreTeam);
public sealed record AssenzaCalendario(PersonaCalendario Persona, TipoAssenza Tipo, StatoRichiesta Stato, bool AOre, decimal Ore);
public sealed record GiornoCalendario(DateOnly Data, bool DelMese, bool Lavorativo, List<PersonaCalendario> Presenti, List<AssenzaCalendario> Assenti)
{
    /// <summary>Chiusure aziendali che comprendono il giorno.</summary>
    public List<string> Chiusure { get; init; } = [];
}

public sealed record TeamFiltro(Guid Id, string Nome, string Colore, string? Emoji);

public sealed record ChiusuraRiga(Guid Id, string Azienda, DateOnly Dal, DateOnly Al, string Descrizione);

/// <summary>Vista calendario del mese: chi è presente e chi è assente, giorno per giorno.</summary>
public sealed class CalendarioAssenzeViewModel
{
    public required int Anno { get; init; }
    public required int Mese { get; init; }
    public required List<GiornoCalendario> Giorni { get; init; }
    public required DateOnly Oggi { get; init; }
    public Guid? AziendaId { get; init; }
    public List<SelectListItem>? Aziende { get; init; }
    public List<ChiusuraRiga> Chiusure { get; init; } = [];
    public Guid? TeamId { get; init; }
    /// <summary>Team dell'azienda per il filtro (vuoto se il consulente non ha scelto un'azienda).</summary>
    public List<TeamFiltro> Team { get; init; } = [];
}

public sealed record RigaResidui(Guid DipendenteId, string Persona, string Azienda, decimal Ferie, decimal Rol);

public sealed class ResiduoForm
{
    public Guid DipendenteId { get; set; }
    public decimal? Ferie { get; set; }
    public decimal? Rol { get; set; }
}

public sealed class ResiduiViewModel
{
    public required List<RigaResidui> Righe { get; init; }
    public int Anno { get; init; }
    public string? Ricerca { get; init; }
    public Guid? AziendaId { get; init; }
    public List<SelectListItem>? Aziende { get; init; }
}

public sealed class AssenzeViewModel
{
    public required List<RichiestaRiga> Richieste { get; init; }
    public StatoRichiesta? Stato { get; init; }
    public int Anno { get; init; }
    public Guid? AziendaId { get; init; }
    public List<SelectListItem>? Aziende { get; init; }
    /// <summary>Solo per il dipendente: i propri saldi.</summary>
    public IReadOnlyList<SaldoAssenza>? Saldi { get; init; }
    /// <summary>Solo per un dipendente responsabile: richieste dei collaboratori in attesa della sua decisione.</summary>
    public List<RichiestaRiga>? DaApprovareTeam { get; init; }
    public DateOnly Oggi { get; init; }
}

[Authorize]
public sealed class AssenzeController(AppDbContext db, UtenteCorrente utente, ServizioSaldi saldi, NotificheAssenze notifiche,
    ServizioOrganigramma organigramma, TimeProvider clock) : AppController
{
    private DateOnly Oggi => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
    private DateTime AdessoUtc => clock.GetUtcNow().UtcDateTime;
    private bool Gestore => utente.IsConsulente || utente.IsAzienda;

    public async Task<IActionResult> Index(StatoRichiesta? stato, int? anno, Guid? aziendaId, bool tutte = false)
    {
        var annoScelto = anno ?? Oggi.Year;
        // Di default tutte le richieste dell'anno, con quelle da approvare in cima (anche se riguardano un altro anno).
        var filtroStato = stato;

        var dipendenti = utente.Filtra(db.Dipendenti.AsNoTracking());
        if (aziendaId is { } az) dipendenti = dipendenti.Where(d => d.AziendaId == az);

        var query = from r in db.RichiesteAssenza.AsNoTracking()
                    join d in dipendenti on r.DipendenteId equals d.Id
                    join a in db.Aziende.AsNoTracking() on d.AziendaId equals a.Id
                    select new { r, d.Cognome, d.Nome, a.RagioneSociale };
        // Anni per cui ci sono richieste (più quello corrente e quello scelto), dal più recente.
        var anniRichieste = await (from r in db.RichiesteAssenza.AsNoTracking()
                                   join d in dipendenti on r.DipendenteId equals d.Id
                                   select r.Dal.Year).Distinct().ToListAsync();
        ViewBag.Anni = anniRichieste.Append(Oggi.Year).Append(annoScelto).Distinct().OrderDescending().ToList();
        var (inizioAnno, fineAnno) = Filtri.Anno(annoScelto);
        query = query.Where(x => x.r.Stato == StatoRichiesta.InAttesa || (x.r.Dal >= inizioAnno && x.r.Dal <= fineAnno));
        // Conteggi per le pillole di stato (prima del filtro sullo stato).
        ViewBag.Conteggi = (await query.GroupBy(x => x.r.Stato).Select(g => new { g.Key, N = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key, x => x.N);
        if (filtroStato is { } s) query = query.Where(x => x.r.Stato == s);

        var risultati = await query.OrderBy(x => x.r.Stato == StatoRichiesta.InAttesa ? 0 : 1)
            .ThenByDescending(x => x.r.Dal).ThenBy(x => x.Cognome).ToListAsync();
        var righe = risultati
            .Select(x => new RichiestaRiga(x.r.Id, x.r.DipendenteId, $"{x.Cognome} {x.Nome}", x.RagioneSociale, x.r.Tipo,
                x.r.Dal, x.r.Al, x.r.AOre, x.r.Ore, x.r.Stato, x.r.RichiestaIl, x.r.Note, x.r.DecisaDa, x.r.MotivoRifiuto))
            .ToList();

        // Il dipendente responsabile di qualcuno vede anche le richieste del suo team da decidere.
        List<RichiestaRiga>? daApprovare = null;
        if (utente.IsDipendente && utente.DipendenteId is { } me && await organigramma.CollaboratoriAsync(me) is { Count: > 0 } collaboratori)
            daApprovare = (await (from r in db.RichiesteAssenza.AsNoTracking()
                                  join d in db.Dipendenti.AsNoTracking().IgnoreAutoIncludes() on r.DipendenteId equals d.Id
                                  where collaboratori.Contains(r.DipendenteId) && r.Stato == StatoRichiesta.InAttesa
                                  orderby r.Dal
                                  select new { r, d.Cognome, d.Nome }).ToListAsync())
                .Select(x => new RichiestaRiga(x.r.Id, x.r.DipendenteId, $"{x.Cognome} {x.Nome}", "", x.r.Tipo, x.r.Dal, x.r.Al, x.r.AOre, x.r.Ore,
                    x.r.Stato, x.r.RichiestaIl, x.r.Note, x.r.DecisaDa, x.r.MotivoRifiuto))
                .ToList();

        IReadOnlyList<SaldoAssenza>? mieiSaldi = null;
        if (utente.IsDipendente && await dipendenti.FirstOrDefaultAsync() is { } io)
        {
            // Senza filtro di stato le righe sono proprio tutte le richieste dell'anno: si riusano per i saldi.
            List<RichiestaAssenza>? richiesteAnno = null; // si ricalcolano: qui ci sono anche le richieste in attesa di altri anni
            mieiSaldi = await saldi.Calcola(io, annoScelto, annoScelto == Oggi.Year ? Oggi.Month : 12, richiesteAnno);
        }

        return View(new AssenzeViewModel
        {
            Richieste = righe,
            Stato = filtroStato,
            Anno = annoScelto,
            AziendaId = aziendaId,
            Aziende = utente.IsConsulente
                ? await db.Aziende.AsNoTracking().OrderBy(a => a.RagioneSociale)
                    .Select(a => new SelectListItem(a.RagioneSociale, a.Id.ToString(), a.Id == aziendaId)).ToListAsync()
                : null,
            Saldi = mieiSaldi,
            DaApprovareTeam = daApprovare,
            Oggi = Oggi,
        });
    }

    public async Task<IActionResult> Nuova(Guid? dipendenteId)
    {
        var form = new RichiestaForm { DipendenteId = utente.IsDipendente ? utente.DipendenteId : dipendenteId, Dal = Oggi };
        return await MostraForm(form);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Nuova(RichiestaForm form)
    {
        if (utente.IsDipendente)
        {
            form.DipendenteId = utente.DipendenteId; // il dipendente chiede solo per sé: il campo non è nel form
            ModelState.Remove(nameof(form.DipendenteId));
        }
        if (form.AOre && form.Ore is null)
            ModelState.AddModelError(nameof(form.Ore), "Indica quante ore di permesso.");
        if (!ModelState.IsValid) return await MostraForm(form);

        // Dipendente (con contratti) e azienda (calendario, santo patrono) in una sola query.
        var trovato = await utente.Filtra(db.Dipendenti.AsNoTracking()).Where(d => d.Id == form.DipendenteId)
            .Join(db.Aziende.AsNoTracking(), d => d.AziendaId, a => a.Id, (d, a) => new { d, a })
            .FirstOrDefaultAsync();
        if (trovato is null) return Forbid();
        var (dip, azienda) = (trovato.d, trovato.a);

        var dal = form.Dal!.Value;
        var al = form.AOre ? dal : form.Al ?? dal;
        var esistenti = await db.RichiesteAssenza.Where(r => r.DipendenteId == dip.Id && r.Dal <= al && r.Al >= dal).ToListAsync();

        // Il dipendente non può superare il saldo maturato fino al mese dell'assenza; l'ufficio può farlo (es. ferie anticipate).
        decimal? disponibile = null;
        if (utente.IsDipendente && form.Tipo.HaSaldo())
            disponibile = (await saldi.Calcola(dip, form.Tipo, dal.Year, al.Month)).Disponibile;

        RichiestaAssenza? richiesta = null;
        if (!Prova(() => richiesta = PianificatoreAssenze.Crea(dip, azienda, form.Tipo, dal, al, esistenti, AdessoUtc,
                form.AOre ? form.Ore : null, disponibile, form.Note, form.ProtocolloCertificato)))
            return await MostraForm(form);

        // Solo l'azienda decide: se registra il consulente, ferie e permessi restano in attesa del referente.
        if (utente.IsAzienda && form.ApprovaSubito && richiesta!.Stato == StatoRichiesta.InAttesa)
            richiesta.Approva(utente.Email, AdessoUtc);

        db.RichiesteAssenza.Add(richiesta!);
        await db.SaveChangesAsync();
        await notifiche.NuovaRichiestaAsync(richiesta!);

        Conferma(richiesta!.Stato == StatoRichiesta.Approvata
            ? $"{richiesta.Tipo.Descrizione()} registrata: {richiesta.Ore:0.##} ore."
            : $"Richiesta di {richiesta.Tipo.Descrizione().ToLower()} inviata ({richiesta.Ore:0.##} ore): in attesa di approvazione dell'azienda.");
        return utente.IsDipendente
            ? RedirectToAction(nameof(Index))
            : RedirectToAction("Dettaglio", "Dipendenti", new { id = dip.Id, sezione = "ferie" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Approva(Guid id, string? ritorno)
    {
        var r = await DaDecidere(id);
        if (r is null) return utente.IsAzienda ? NotFound() : Forbid();
        if (Prova(() => r.Approva(utente.Email, AdessoUtc)))
        {
            await db.SaveChangesAsync();
            await notifiche.EsitoAsync(r);
            Conferma("Richiesta approvata.");
        }
        else Errore(PrimoErrore());
        return Torna(ritorno);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Rifiuta(Guid id, string? motivo, string? ritorno)
    {
        var r = await DaDecidere(id);
        if (r is null) return utente.IsAzienda ? NotFound() : Forbid();
        if (Prova(() => r.Rifiuta(utente.Email, motivo ?? "", AdessoUtc)))
        {
            await db.SaveChangesAsync();
            await notifiche.EsitoAsync(r);
            Conferma("Richiesta rifiutata.");
        }
        else Errore(PrimoErrore());
        return Torna(ritorno);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Annulla(Guid id, string? ritorno)
    {
        var r = await Trova(id);
        if (r is null) return NotFound();
        if (!PuoAnnullare(r.Tipo)) return Forbid();
        if (Prova(() => r.Annulla(Oggi)))
        {
            await db.SaveChangesAsync();
            Conferma("Richiesta annullata.");
        }
        else Errore(PrimoErrore());
        return Torna(ritorno);
    }

    /// <summary>Calendario del mese con presenti e assenti, giorno per giorno.</summary>
    [Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> Calendario(int? anno, int? mese, Guid? aziendaId, Guid? teamId)
    {
        var a0 = anno ?? Oggi.Year;
        var m0 = mese is >= 1 and <= 12 ? mese.Value : Oggi.Month;
        return await CalendarioMese(a0, m0, aziendaId, teamId);
    }

    private async Task<IActionResult> CalendarioMese(int anno, int mese, Guid? aziendaId, Guid? teamId)
    {
        var primo = new DateOnly(anno, mese, 1);
        var ultimo = primo.AddMonths(1).AddDays(-1);
        // Settimane intere da lunedì a domenica.
        var inizio = primo.AddDays(-(((int)primo.DayOfWeek + 6) % 7));
        var fine = ultimo.AddDays(6 - (((int)ultimo.DayOfWeek + 6) % 7));

        var query = utente.Filtra(db.Dipendenti.AsNoTracking());
        if (utente.IsConsulente && aziendaId is { } az) query = query.Where(d => d.AziendaId == az);
        // Team del filtro: quelli dell'azienda; scelto un team valgono anche i suoi sotto-team.
        var aziendaTeam = utente.IsAzienda ? utente.AziendaId : aziendaId;
        var teamAzienda = aziendaTeam is { } at ? await db.Team.AsNoTracking().Where(t => t.AziendaId == at).OrderBy(t => t.Nome).ToListAsync() : [];
        if (teamId is { } tid && teamAzienda.Any(t => t.Id == tid))
        {
            var inclusi = new HashSet<Guid> { tid };
            for (var aggiunti = true; aggiunti;)
                aggiunti = teamAzienda.Where(t => t.PadreId is { } p && inclusi.Contains(p)).Select(t => inclusi.Add(t.Id)).ToList().Any(x => x);
            query = query.Where(d => d.TeamId != null && inclusi.Contains(d.TeamId.Value));
        }
        else teamId = null;
        var persone = (await query.Where(d => d.Contratti.Any(c => c.DataInizio <= ultimo && (c.DataFine == null || c.DataFine >= primo)))
                .OrderBy(d => d.Cognome).ThenBy(d => d.Nome).ToListAsync())
            .ToDictionary(d => d.Id);
        var ids = persone.Keys.ToList();
        var richieste = await db.RichiesteAssenza.AsNoTracking()
            .Where(r => ids.Contains(r.DipendenteId) && (r.Stato == StatoRichiesta.Approvata || r.Stato == StatoRichiesta.InAttesa)
                        && r.Dal <= ultimo && r.Al >= primo)
            .ToListAsync();
        var aziendePersone = persone.Values.Select(d => d.AziendaId).Distinct().ToList();
        var tuttiTeam = await db.Team.AsNoTracking().Where(t => aziendePersone.Contains(t.AziendaId)).ToListAsync();
        var coloriTeam = tuttiTeam.ToDictionary(t => t.Id, t => CL.Domain.Organizzazione.Organigramma.ColoreDi(t, tuttiTeam));
        PersonaCalendario P(Dipendente d) => new(d.Id, d.NomeCompleto, d.FotoChiave is not null, d.Segnaposto,
            d.TeamId is { } tid ? coloriTeam.GetValueOrDefault(tid) : null);

        var aziendeVisibili = utente.IsAzienda ? [utente.AziendaId!.Value]
            : aziendaId is { } azc ? [azc] : await db.Aziende.AsNoTracking().Select(a => a.Id).ToListAsync();
        var chiusure = await (from c in db.ChiusureAziendali.AsNoTracking()
                              join a in db.Aziende.AsNoTracking() on c.AziendaId equals a.Id
                              where aziendeVisibili.Contains(c.AziendaId) && c.Dal <= fine && c.Al >= inizio
                              orderby c.Dal
                              select new ChiusuraRiga(c.Id, a.RagioneSociale, c.Dal, c.Al, c.Descrizione)).ToListAsync();
        var piuAziende = chiusure.Select(c => c.Azienda).Distinct().Count() > 1 || (utente.IsConsulente && aziendaId is null);

        var giorni = new List<GiornoCalendario>();
        for (var g = inizio; g <= fine; g = g.AddDays(1))
        {
            var delMese = g.Month == mese;
            var lavorativo = CalendarioLavorativo.IsLavorativo(g, 5);
            var giorno = g;
            var chiuso = delMese
                ? chiusure.Where(c => c.Dal <= giorno && c.Al >= giorno).Select(c => piuAziende ? $"{c.Descrizione} · {c.Azienda}" : c.Descrizione).ToList()
                : [];
            if (!delMese || !lavorativo) { giorni.Add(new GiornoCalendario(g, delMese, lavorativo, [], []) { Chiusure = chiuso }); continue; }
            var inForza = persone.Values.Where(d => d.InForzaIl(giorno)).ToList();
            var assenti = richieste.Where(r => r.Dal <= giorno && r.Al >= giorno && persone.ContainsKey(r.DipendenteId) && persone[r.DipendenteId].InForzaIl(giorno))
                .OrderBy(r => r.Stato).ThenBy(r => persone[r.DipendenteId].Cognome)
                .Select(r => new AssenzaCalendario(P(persone[r.DipendenteId]), r.Tipo, r.Stato, r.AOre, r.Ore)).ToList();
            // Chi è assente tutto il giorno (approvato) non è tra i presenti; le assenze a ore e quelle in attesa sì.
            var fuori = assenti.Where(a => !a.AOre && a.Stato == StatoRichiesta.Approvata).Select(a => a.Persona.Id).ToHashSet();
            giorni.Add(new GiornoCalendario(g, true, true, inForza.Where(d => !fuori.Contains(d.Id)).Select(P).ToList(), assenti) { Chiusure = chiuso });
        }
        return View("Calendario", new CalendarioAssenzeViewModel
        {
            Anno = anno, Mese = mese, Giorni = giorni, Oggi = Oggi, AziendaId = aziendaId, TeamId = teamId,
            Team = teamAzienda.Select(t => new TeamFiltro(t.Id, t.Nome, CL.Domain.Organizzazione.Organigramma.ColoreDi(t, teamAzienda), t.Emoji)).ToList(),
            Chiusure = chiusure.Where(c => c.Dal <= new DateOnly(anno, mese, 1).AddMonths(1).AddDays(-1) && c.Al >= new DateOnly(anno, mese, 1)).ToList(),
            Aziende = utente.IsConsulente
                ? await db.Aziende.AsNoTracking().OrderBy(a => a.RagioneSociale).Select(a => new SelectListItem(a.RagioneSociale, a.Id.ToString(), a.Id == aziendaId)).ToListAsync()
                : null,
        });
    }

    /// <summary>
    /// Nuova chiusura aziendale: per ogni persona in forza nel periodo si registrano ferie già approvate, collegate alla chiusura.
    /// Chi è già assente in quei giorni o non ha un contratto per tutto il periodo viene saltato (e conteggiato nel messaggio).
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> Chiusura(Guid? aziendaId, DateOnly? dal, DateOnly? al, string? descrizione)
    {
        var az = utente.IsAzienda ? utente.AziendaId : aziendaId;
        var azienda = az is { } id ? await db.Aziende.FirstOrDefaultAsync(a => a.Id == id) : null;
        if (azienda is null)
        {
            Errore("Scegli l'azienda della chiusura.");
            return RedirectToAction(nameof(Calendario));
        }
        if (dal is not { } inizio || al is not { } fine)
        {
            Errore("Indica le date della chiusura.");
            return RedirectToAction(nameof(Calendario), new { aziendaId });
        }
        ChiusuraAziendale chiusura;
        try { chiusura = new ChiusuraAziendale(azienda.Id, inizio, fine, descrizione, utente.Email, DateTime.UtcNow); }
        catch (CL.Domain.Common.DomainException ex)
        {
            Errore(ex.Message);
            return RedirectToAction(nameof(Calendario), new { anno = inizio.Year, mese = inizio.Month, aziendaId });
        }
        db.ChiusureAziendali.Add(chiusura);

        var esterni = TipoContrattoExtensions.Esterni.ToList();
        var persone = await db.Dipendenti.Where(d => d.AziendaId == azienda.Id
                && d.Contratti.Any(c => !esterni.Contains(c.Tipo) && c.DataInizio <= fine && (c.DataFine == null || c.DataFine >= inizio)))
            .ToListAsync();
        var ids = persone.Select(d => d.Id).ToList();
        var esistenti = await db.RichiesteAssenza.Where(r => ids.Contains(r.DipendenteId) && r.Dal <= fine && r.Al >= inizio).ToListAsync();
        var adesso = DateTime.UtcNow;
        int registrate = 0, saltate = 0;
        foreach (var d in persone)
        {
            try
            {
                var ferie = PianificatoreAssenze.Crea(d, azienda, TipoAssenza.Ferie, inizio, fine, esistenti, adesso, note: chiusura.Descrizione);
                ferie.Approva("Chiusura aziendale", adesso);
                ferie.CollegaChiusura(chiusura.Id);
                db.RichiesteAssenza.Add(ferie);
                registrate++;
            }
            catch (CL.Domain.Common.DomainException) { saltate++; }
        }
        await db.SaveChangesAsync();
        Conferma($"Chiusura \"{chiusura.Descrizione}\" dal {inizio:dd/MM/yyyy} al {fine:dd/MM/yyyy} salvata: ferie registrate per {registrate} {(registrate == 1 ? "persona" : "persone")}"
                 + (saltate > 0 ? $", {saltate} {(saltate == 1 ? "saltata" : "saltate")} (già assenti o senza contratto per tutto il periodo)." : "."));
        return RedirectToAction(nameof(Calendario), new { anno = inizio.Year, mese = inizio.Month, aziendaId });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> EliminaChiusura(Guid id)
    {
        var chiusura = await db.ChiusureAziendali.FirstOrDefaultAsync(c => c.Id == id);
        if (chiusura is null || !utente.PuoGestireAzienda(chiusura.AziendaId)) return NotFound();
        db.RichiesteAssenza.RemoveRange(db.RichiesteAssenza.Where(r => r.ChiusuraId == id));
        db.ChiusureAziendali.Remove(chiusura);
        await db.SaveChangesAsync();
        Conferma($"Chiusura \"{chiusura.Descrizione}\" eliminata, con le ferie che aveva registrato.");
        return RedirectToAction(nameof(Calendario), new { anno = chiusura.Dal.Year, mese = chiusura.Dal.Month });
    }

    /// <summary>Residui di ferie e ROL al 1° gennaio per tutto il personale dipendente (dal cedolino di dicembre o dal gestionale paghe).</summary>
    [Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> Residui(int? anno, Guid? aziendaId, string? q)
    {
        var a = anno ?? Oggi.Year;
        var (inizio, fine) = Filtri.Anno(a);
        var esterni = TipoContrattoExtensions.Esterni.ToList();
        var persone = utente.Filtra(db.Dipendenti.AsNoTracking().IgnoreAutoIncludes())
            .Where(d => db.Contratti.Any(c => c.DipendenteId == d.Id && !esterni.Contains(c.Tipo) && c.DataInizio <= fine && (c.DataFine == null || c.DataFine >= inizio)));
        if (utente.IsConsulente && aziendaId is { } az) persone = persone.Where(d => d.AziendaId == az);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim().ToLower();
            persone = persone.Where(d => d.Cognome.ToLower().Contains(t) || d.Nome.ToLower().Contains(t));
        }
        var righe = await (
            from d in persone
            join az2 in db.Aziende.AsNoTracking() on d.AziendaId equals az2.Id
            orderby d.Cognome, d.Nome
            select new RigaResidui(d.Id, d.Cognome + " " + d.Nome, az2.RagioneSociale,
                db.SaldiIniziali.Where(s => s.DipendenteId == d.Id && s.Anno == a && s.Tipo == TipoAssenza.Ferie).Select(s => s.Ore).FirstOrDefault(),
                db.SaldiIniziali.Where(s => s.DipendenteId == d.Id && s.Anno == a && s.Tipo == TipoAssenza.Rol).Select(s => s.Ore).FirstOrDefault()))
            .ToListAsync();
        return View(new ResiduiViewModel
        {
            Righe = righe, Anno = a, Ricerca = q, AziendaId = aziendaId,
            Aziende = utente.IsConsulente
                ? await db.Aziende.AsNoTracking().OrderBy(x => x.RagioneSociale).Select(x => new SelectListItem(x.RagioneSociale, x.Id.ToString(), x.Id == aziendaId)).ToListAsync()
                : null,
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> Residui(int anno, List<ResiduoForm> righe, string? ritorno)
    {
        var ids = righe.Select(r => r.DipendenteId).ToList();
        var visibili = await utente.Filtra(db.Dipendenti).Where(d => ids.Contains(d.Id)).Select(d => d.Id).ToListAsync();
        var esistenti = await db.SaldiIniziali.Where(s => visibili.Contains(s.DipendenteId) && s.Anno == anno).ToListAsync();
        foreach (var r in righe.Where(r => visibili.Contains(r.DipendenteId)))
            foreach (var (tipo, ore) in new[] { (TipoAssenza.Ferie, r.Ferie), (TipoAssenza.Rol, r.Rol) })
            {
                var s = esistenti.FirstOrDefault(x => x.DipendenteId == r.DipendenteId && x.Tipo == tipo);
                if (s is null) { if (ore is { } o && o != 0) db.SaldiIniziali.Add(new SaldoIniziale(r.DipendenteId, anno, tipo, o)); }
                else s.Rettifica(ore ?? 0);
            }
        await db.SaveChangesAsync();
        Conferma($"Residui al 1° gennaio {anno} salvati.");
        return Url.IsLocalUrl(ritorno) ? LocalRedirect(ritorno) : RedirectToAction(nameof(Residui), new { anno });
    }

    /// <summary>Residuo dell'anno precedente (o saldo di partenza dal gestionale paghe).</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> ImpostaResidui(Guid dipendenteId, int anno, decimal? ferie, decimal? rol)
    {
        if (!await utente.Filtra(db.Dipendenti).AnyAsync(d => d.Id == dipendenteId)) return NotFound();
        var esistenti = await db.SaldiIniziali.Where(s => s.DipendenteId == dipendenteId && s.Anno == anno).ToListAsync();

        foreach (var (tipo, ore) in new[] { (TipoAssenza.Ferie, ferie), (TipoAssenza.Rol, rol) })
        {
            var s = esistenti.FirstOrDefault(x => x.Tipo == tipo);
            if (s is null) db.SaldiIniziali.Add(new SaldoIniziale(dipendenteId, anno, tipo, ore ?? 0));
            else s.Rettifica(ore ?? 0);
        }
        await db.SaveChangesAsync();
        Conferma($"Residui di inizio {anno} salvati.");
        return RedirectToAction("Dettaglio", "Dipendenti", new { id = dipendenteId, sezione = "ferie" });
    }

    /// <summary>
    /// Richiesta che l'utente può approvare o rifiutare: il referente per tutta la propria azienda, un dipendente
    /// solo se l'organigramma lo indica come responsabile del richiedente. Il consulente non decide.
    /// </summary>
    private async Task<RichiestaAssenza?> DaDecidere(Guid id)
    {
        if (utente.IsAzienda) return await Trova(id);
        if (!utente.IsDipendente || utente.DipendenteId is not { } io) return null;
        var r = await db.RichiesteAssenza.FirstOrDefaultAsync(x => x.Id == id);
        if (r is null || r.DipendenteId == io) return null;
        return await organigramma.ApprovatoreAsync(r.DipendenteId) == io ? r : null;
    }

    private Task<RichiestaAssenza?> Trova(Guid id)
    {
        var ids = utente.Filtra(db.Dipendenti).Select(d => d.Id);
        return db.RichiesteAssenza.FirstOrDefaultAsync(r => r.Id == id && ids.Contains(r.DipendenteId));
    }

    /// <summary>Il consulente annulla solo ciò che non richiede la decisione dell'azienda (es. malattia registrata per errore).</summary>
    private bool PuoAnnullare(TipoAssenza tipo) => !utente.IsConsulente || !tipo.RichiedeApprovazione();

    private string PrimoErrore() => ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault() ?? "Operazione non riuscita.";

    private IActionResult Torna(string? ritorno) =>
        Url.IsLocalUrl(ritorno) ? LocalRedirect(ritorno) : RedirectToAction(nameof(Index));

    private async Task<IActionResult> MostraForm(RichiestaForm form)
    {
        ViewBag.Gestore = Gestore;
        ViewBag.PuoApprovare = utente.IsAzienda;
        var scelto = form.DipendenteId;
        var attivo = Filtri.ContrattoAttivoIl(Oggi);
        // Filtro e proiezione nel database: solo id e nome dei dipendenti in forza (più quello già scelto).
        ViewBag.Dipendenti = Gestore
            ? (await utente.Filtra(db.Dipendenti).Where(d => d.Id == scelto || d.Contratti.AsQueryable().Any(attivo))
                    .OrderBy(d => d.Cognome).ThenBy(d => d.Nome)
                    .Select(d => new { d.Id, d.Cognome, d.Nome }).ToListAsync())
                .Select(d => new SelectListItem($"{d.Cognome} {d.Nome}", d.Id.ToString(), d.Id == scelto)).ToList()
            : null;

        if (utente.IsDipendente && utente.DipendenteId is { } mio
            && await db.Dipendenti.AsNoTracking().FirstOrDefaultAsync(d => d.Id == mio) is { } io)
            ViewBag.Saldi = await saldi.Calcola(io, Oggi.Year, 12);

        return View("Nuova", form);
    }
}
