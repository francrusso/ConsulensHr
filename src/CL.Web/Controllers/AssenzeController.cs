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
        // Chi approva vede per default ciò che è da decidere; il dipendente vede tutto il proprio anno.
        var filtroStato = tutte || utente.IsDipendente ? stato : stato ?? StatoRichiesta.InAttesa;

        var dipendenti = utente.Filtra(db.Dipendenti.AsNoTracking());
        if (aziendaId is { } az) dipendenti = dipendenti.Where(d => d.AziendaId == az);

        var query = from r in db.RichiesteAssenza.AsNoTracking()
                    join d in dipendenti on r.DipendenteId equals d.Id
                    join a in db.Aziende.AsNoTracking() on d.AziendaId equals a.Id
                    select new { r, d.Cognome, d.Nome, a.RagioneSociale };
        if (filtroStato is { } s) query = query.Where(x => x.r.Stato == s);
        // Le richieste da approvare si vedono tutte, anche se riguardano l'anno prossimo.
        if (!(Gestore && filtroStato == StatoRichiesta.InAttesa))
        {
            var (inizio, fine) = Filtri.Anno(annoScelto);
            query = query.Where(x => x.r.Dal >= inizio && x.r.Dal <= fine);
        }

        var risultati = await query.OrderBy(x => x.r.Dal).ThenBy(x => x.Cognome).ToListAsync();
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
            var richiesteAnno = filtroStato is null ? risultati.Select(x => x.r).ToList() : null;
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
