using System.ComponentModel.DataAnnotations;
using CL.Domain.Assenze;
using CL.Domain.Aziende;
using CL.Domain.Common;
using CL.Domain.Dipendenti;
using CL.Domain.Organizzazione;
using CL.Domain.Presenze;
using CL.Infrastructure.Identity;
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

    [Display(Name = "Tipo di richiesta")]
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
}

public sealed record RichiestaRiga(
    Guid Id, Guid DipendenteId, string Dipendente, string Azienda, TipoAssenza Tipo, DateOnly Dal, DateOnly Al,
    bool AOre, decimal Ore, StatoRichiesta Stato, DateTime RichiestaIl, string? Note, string? DecisaDa, string? MotivoRifiuto)
{
    /// <summary>Giorno di smart working chiesto da chi lavora in presenza (stessa tabella delle ferie, decisione a parte).</summary>
    public bool SmartWorking { get; init; }
    /// <summary>Ore di straordinario di un giorno (in <see cref="Ore"/>), da approvare come le ferie.</summary>
    public bool Straordinario { get; init; }
    /// <summary>Il dipendente ha chiesto di cancellare questa richiesta approvata: l'azienda conferma o respinge.</summary>
    public bool AnnullamentoInAttesa { get; init; }
    public string? MotivoAnnullamento { get; init; }
}

/// <summary>Persona nel calendario; <paramref name="ColoreTeam"/> fa da bordo alla foto (null senza team).</summary>
public sealed record PersonaCalendario(Guid Id, string Nome, bool HaFoto, string Segnaposto, string? ColoreTeam);
public sealed record AssenzaCalendario(PersonaCalendario Persona, TipoAssenza Tipo, StatoRichiesta Stato, bool AOre, decimal Ore);
public sealed record GiornoCalendario(DateOnly Data, bool DelMese, bool Lavorativo, List<PersonaCalendario> Presenti, List<AssenzaCalendario> Assenti)
{
    /// <summary>Chiusure aziendali che comprendono il giorno.</summary>
    public List<string> Chiusure { get; init; } = [];
    /// <summary>Chi lavora a distanza e ha segnato di essere in ufficio.</summary>
    public List<PersonaCalendario> InUfficio { get; init; } = [];
    /// <summary>Festività del giorno (nazionale, o santo patrono se si guarda una sola azienda).</summary>
    public string? Festa { get; init; }
    /// <summary>Chi lavora in presenza e ha lo smart working approvato quel giorno.</summary>
    public List<PersonaCalendario> InSmartWorking { get; init; } = [];
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
    /// <summary>Filtro sul luogo di lavoro dei presenti: null (tutti), "ufficio" o "remoto".</summary>
    public string? Luogo { get; init; }
    /// <summary>Team dell'azienda per il filtro (vuoto se il consulente non ha scelto un'azienda).</summary>
    public List<TeamFiltro> Team { get; init; } = [];
}

public sealed class AssenzeViewModel
{
    public required List<RichiestaRiga> Richieste { get; init; }
    public StatoRichiesta? Stato { get; init; }
    public int Anno { get; init; }
    /// <summary>Anni con almeno una richiesta, più quello corrente e quello scelto, dal più recente.</summary>
    public required List<int> Anni { get; init; }
    /// <summary>Richieste per stato, prima del filtro sullo stato (numeri delle pillole).</summary>
    public required Dictionary<StatoRichiesta, int> Conteggi { get; init; }
    public string? Ricerca { get; init; }
    public Guid? AziendaId { get; init; }
    public List<SelectListItem>? Aziende { get; init; }
    /// <summary>Solo per il dipendente: i propri saldi.</summary>
    public IReadOnlyList<SaldoAssenza>? Saldi { get; init; }
    /// <summary>Solo per un dipendente responsabile: richieste dei collaboratori in attesa della sua decisione.</summary>
    public List<RichiestaRiga>? DaApprovareTeam { get; init; }
    public DateOnly Oggi { get; init; }
}

/// <summary>
/// Richieste (ferie, permessi, malattia, smart working, presenza in ufficio, straordinari), loro decisione e annullamento,
/// calendario di presenze e assenze, chiusure aziendali e residui di inizio anno.
/// Decide il referente dell'azienda (o il responsabile indicato dall'organigramma); il consulente registra ma non approva.
/// </summary>
[Authorize]
public sealed class AssenzeController(AppDbContext db, UtenteCorrente utente, ServizioSaldi saldi, NotificheAssenze notifiche,
    ServizioOrganigramma organigramma, ServizioUfficio ufficio, TimeProvider clock) : AppController
{
    private DateOnly Oggi => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
    private DateTime AdessoUtc => clock.GetUtcNow().UtcDateTime;
    private bool Gestore => utente.IsConsulente || utente.IsAzienda;
    /// <summary>Id dei dipendenti che l'utente può vedere (sottoquery da usare nei filtri).</summary>
    private IQueryable<Guid> Perimetro => utente.Filtra(db.Dipendenti).Select(d => d.Id);

    public async Task<IActionResult> Index(StatoRichiesta? stato, int? anno, Guid? aziendaId, string? q = null)
    {
        var annoScelto = anno ?? Oggi.Year;
        var (inizioAnno, fineAnno) = Filtri.Anno(annoScelto);

        var dipendenti = utente.Filtra(db.Dipendenti.AsNoTracking());
        if (aziendaId is { } az) dipendenti = dipendenti.Where(d => d.AziendaId == az);
        // Ricerca per nome o cognome della persona, in qualunque ordine (vale per tutti i tipi di richiesta).
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim().ToLower();
            dipendenti = dipendenti.Where(d => (d.Cognome + " " + d.Nome).ToLower().Contains(t) || (d.Nome + " " + d.Cognome).ToLower().Contains(t));
        }

        // Nella stessa lista, con gli stessi stati: ferie e permessi, smart working (di chi lavora in presenza) e straordinari.
        // Si vedono le richieste dell'anno scelto più quelle ancora in attesa di qualunque anno.
        var assenze = await (from r in db.RichiesteAssenza.AsNoTracking()
                             join d in dipendenti on r.DipendenteId equals d.Id
                             join a in db.Aziende.AsNoTracking() on d.AziendaId equals a.Id
                             where r.Stato == StatoRichiesta.InAttesa || (r.Dal >= inizioAnno && r.Dal <= fineAnno)
                             select new { r, d.Cognome, d.Nome, a.RagioneSociale }).ToListAsync();
        // Smart working e straordinari si leggono tutti: servono anche per gli anni da proporre.
        var smart = await (from g in db.GiorniSmartWorking.AsNoTracking()
                           join d in dipendenti on g.DipendenteId equals d.Id
                           join a in db.Aziende.AsNoTracking() on d.AziendaId equals a.Id
                           select new { g, d.Cognome, d.Nome, a.RagioneSociale }).ToListAsync();
        var straordinari = await (from s in db.Straordinari.AsNoTracking()
                                  join d in dipendenti on s.DipendenteId equals d.Id
                                  join a in db.Aziende.AsNoTracking() on d.AziendaId equals a.Id
                                  select new { s, d.Cognome, d.Nome, a.RagioneSociale }).ToListAsync();

        var anni = await (from r in db.RichiesteAssenza.AsNoTracking()
                          join d in dipendenti on r.DipendenteId equals d.Id
                          select r.Dal.Year).Distinct().ToListAsync();
        anni.AddRange(smart.Select(x => x.g.Giorno.Year));
        anni.AddRange(straordinari.Select(x => x.s.Giorno.Year));
        anni.AddRange([Oggi.Year, annoScelto]);

        bool DellAnno(DateOnly giorno) => giorno >= inizioAnno && giorno <= fineAnno;
        static StatoRichiesta StatoDi(StatoSmartWorking s) => s switch
        {
            StatoSmartWorking.Approvato => StatoRichiesta.Approvata, StatoSmartWorking.Rifiutato => StatoRichiesta.Rifiutata, _ => StatoRichiesta.InAttesa,
        };
        var righe = assenze
            .Select(x => new RichiestaRiga(x.r.Id, x.r.DipendenteId, $"{x.Cognome} {x.Nome}", x.RagioneSociale, x.r.Tipo,
                x.r.Dal, x.r.Al, x.r.AOre, x.r.Ore, x.r.Stato, x.r.RichiestaIl, x.r.Note, x.r.DecisaDa, x.r.MotivoRifiuto)
            {
                AnnullamentoInAttesa = x.r.AnnullamentoInAttesa, MotivoAnnullamento = x.r.MotivoAnnullamento,
            })
            .Concat(smart.Where(x => x.g.Stato == StatoSmartWorking.InAttesa || DellAnno(x.g.Giorno))
                .Select(x => new RichiestaRiga(x.g.Id, x.g.DipendenteId, $"{x.Cognome} {x.Nome}", x.RagioneSociale, TipoAssenza.Ferie,
                    x.g.Giorno, x.g.Giorno, false, 0, StatoDi(x.g.Stato), x.g.RichiestoIl, null, x.g.DecisoDa, x.g.MotivoRifiuto) { SmartWorking = true }))
            .Concat(straordinari.Where(x => x.s.Stato == StatoRichiesta.InAttesa || DellAnno(x.s.Giorno))
                .Select(x => new RichiestaRiga(x.s.Id, x.s.DipendenteId, $"{x.Cognome} {x.Nome}", x.RagioneSociale, TipoAssenza.Ferie,
                    x.s.Giorno, x.s.Giorno, true, x.s.Ore, x.s.Stato, x.s.RichiestoIl, x.s.Note, x.s.DecisoDa, x.s.MotivoRifiuto) { Straordinario = true }))
            .ToList();
        var conteggi = righe.GroupBy(x => x.Stato).ToDictionary(g => g.Key, g => g.Count());
        if (stato is { } filtro) righe = righe.Where(x => x.Stato == filtro).ToList();
        // In cima ciò che c'è da decidere (richieste in attesa e cancellazioni chieste), poi dalla più recente.
        righe = righe.OrderBy(x => x.Stato == StatoRichiesta.InAttesa || x.AnnullamentoInAttesa ? 0 : 1)
            .ThenByDescending(x => x.Dal).ThenBy(x => x.Dipendente)
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

        // Il dipendente vede in cima i propri saldi: maturato fino al mese corrente, o a fine anno se guarda un altro anno.
        IReadOnlyList<SaldoAssenza>? mieiSaldi = null;
        if (utente.IsDipendente && await dipendenti.FirstOrDefaultAsync() is { } io)
            mieiSaldi = await saldi.Calcola(io, annoScelto, annoScelto == Oggi.Year ? Oggi.Month : 12);

        return View(new AssenzeViewModel
        {
            Richieste = righe,
            Stato = stato,
            Anno = annoScelto,
            Anni = anni.Distinct().OrderDescending().ToList(),
            Conteggi = conteggi,
            Ricerca = q,
            AziendaId = aziendaId,
            Aziende = await ElencoAziende(aziendaId),
            Saldi = mieiSaldi,
            DaApprovareTeam = daApprovare,
            Oggi = Oggi,
        });
    }

    /// <summary>
    /// Modulo unico delle richieste. <paramref name="tipo"/> è un <see cref="TipoAssenza"/> oppure una voce di presenza
    /// di <see cref="TipiRichiesta"/> (smart working, presenza in ufficio, straordinario).
    /// </summary>
    public async Task<IActionResult> Nuova(Guid? dipendenteId, DateOnly? dal, string? tipo, bool aOre = false, string? ritorno = null)
    {
        ViewBag.Lavoro = tipo is TipiRichiesta.SmartWorking or TipiRichiesta.InUfficio or TipiRichiesta.Straordinario ? tipo : null;
        ViewBag.Ritorno = Url.IsLocalUrl(ritorno) ? ritorno : null;
        // Dal calendario si arriva con giorno e tipo già scelti (es. permesso a ore di quel giorno).
        return await MostraForm(new RichiestaForm
        {
            DipendenteId = utente.IsDipendente ? utente.DipendenteId : dipendenteId,
            Dal = dal ?? Oggi, Al = dal, Tipo = Enum.TryParse<TipoAssenza>(tipo, out var t) ? t : TipoAssenza.Ferie, AOre = aOre,
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Nuova(RichiestaForm form, string? ritorno)
    {
        ViewBag.Ritorno = Url.IsLocalUrl(ritorno) ? ritorno : null;
        if (utente.IsDipendente)
        {
            form.DipendenteId = utente.DipendenteId; // il dipendente chiede solo per sé: il campo non è nel form
            ModelState.Remove(nameof(form.DipendenteId));
        }
        // Il campo Tipo può valere anche smart working, ufficio o straordinario, che non sono assenze: si legge così com'è.
        var scelta = Request.Form["Tipo"].ToString();
        if (form.DipendenteId is { } chi)
        {
            // Solo i tipi di richiesta che l'azienda della persona offre.
            if (await AziendaDi(chi) is { } aziendaPersona && !aziendaPersona.Abilitata(scelta))
            {
                ModelState.Clear();
                ModelState.AddModelError("", "Questo tipo di richiesta non è previsto dall'azienda.");
                return await MostraForm(form);
            }
            if (scelta is TipiRichiesta.SmartWorking or TipiRichiesta.InUfficio) return await NuovaGiorniLavoro(form, scelta, chi, ritorno);
            if (scelta == TipiRichiesta.Straordinario) return await NuovoStraordinario(form, chi, ritorno);
        }
        if (form.AOre && form.Ore is null)
            ModelState.AddModelError(nameof(form.Ore), "Indica quante ore di permesso.");
        if (form.Tipo == TipoAssenza.Malattia && string.IsNullOrWhiteSpace(form.ProtocolloCertificato))
            ModelState.AddModelError(nameof(form.ProtocolloCertificato), "Inserisci il numero di protocollo del certificato medico.");
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

        // La malattia nasce già approvata; il resto resta in attesa. Solo l'azienda decide: se registra il consulente,
        // ferie e permessi restano in attesa del referente.
        if (utente.IsAzienda && form.ApprovaSubito && richiesta!.Stato == StatoRichiesta.InAttesa)
            richiesta.Approva(utente.Email, AdessoUtc);

        db.RichiesteAssenza.Add(richiesta!);
        await db.SaveChangesAsync();
        await notifiche.NuovaRichiestaAsync(richiesta!);

        Conferma(richiesta!.Stato == StatoRichiesta.Approvata
            ? $"{richiesta.Tipo.Descrizione()} registrata: {richiesta.Ore:0.##} ore."
            : $"Richiesta di {richiesta.Tipo.Descrizione().ToLower()} inviata ({richiesta.Ore:0.##} ore): in attesa di approvazione dell'azienda.");
        return DopoNuova(dip.Id, ritorno);
    }

    /// <summary>
    /// Ore di straordinario di un giorno: le segnala il dipendente (anche a posteriori, dal mese scorso) o le registra l'azienda.
    /// Vanno approvate dall'azienda; il referente può approvarle subito. Approvate, finiscono nel riepilogo presenze.
    /// </summary>
    private async Task<IActionResult> NuovoStraordinario(RichiestaForm form, Guid id, string? ritorno)
    {
        ViewBag.Lavoro = TipiRichiesta.Straordinario;
        ModelState.Clear(); // il tipo non è un'assenza: valgono solo giorno, ore e note
        var dip = await utente.Filtra(db.Dipendenti.AsNoTracking()).FirstOrDefaultAsync(d => d.Id == id);
        if (dip is null) return Forbid();
        if (form.Dal is null) ModelState.AddModelError(nameof(form.Dal), "Indica il giorno dello straordinario.");
        if (form.Ore is null) ModelState.AddModelError(nameof(form.Ore), "Indica quante ore di straordinario.");
        if (!ModelState.IsValid) return await MostraForm(form);

        var g = form.Dal!.Value;
        var primoMeseScorso = new DateOnly(Oggi.Year, Oggi.Month, 1).AddMonths(-1);
        if (g < primoMeseScorso || g > Oggi.AddDays(31))
            ModelState.AddModelError(nameof(form.Dal), "Lo straordinario si indica dal mese scorso fino a un mese da oggi.");
        else if (dip.ContrattoAttivo(g) is null)
            ModelState.AddModelError(nameof(form.Dal), "In quel giorno non c'è un contratto in corso.");
        Straordinario? s = null;
        if (!ModelState.IsValid || !Prova(() => s = new Straordinario(id, g, form.Ore!.Value, form.Note, AdessoUtc)))
            return await MostraForm(form);

        if (utente.IsAzienda && form.ApprovaSubito) s!.Approva(utente.Email, AdessoUtc);
        db.Straordinari.Add(s!);
        await db.SaveChangesAsync();
        Conferma(s!.Stato == StatoRichiesta.Approvata
            ? $"Straordinario registrato: {s.Ore:0.##} ore il {g:dd/MM/yyyy}."
            : $"Straordinario di {s.Ore:0.##} ore del {g:dd/MM/yyyy} inviato: in attesa di approvazione dell'azienda.");
        return DopoNuova(id, ritorno);
    }

    /// <summary>
    /// Smart working (chi lavora in presenza) o presenza in ufficio (chi lavora a distanza) dallo stesso modulo, anche per più
    /// giorni: valgono le regole del calendario di <see cref="ServizioUfficio.AggiungiAsync"/>.
    /// </summary>
    private async Task<IActionResult> NuovaGiorniLavoro(RichiestaForm form, string scelta, Guid id, string? ritorno)
    {
        ViewBag.Lavoro = scelta;
        ModelState.Clear(); // il tipo non è un'assenza: valgono solo le date
        // Il dipendente per sé; azienda e consulente per una persona del proprio perimetro. Valgono per tutti,
        // qualunque sia la modalità di lavoro del contratto.
        var dip = await utente.Filtra(db.Dipendenti.AsNoTracking()).FirstOrDefaultAsync(d => d.Id == id);
        if (dip is null) return Forbid();
        if (form.Dal is not { } dal)
        {
            ModelState.AddModelError(nameof(form.Dal), "Indica la data di inizio.");
            return await MostraForm(form);
        }
        var modo = scelta == TipiRichiesta.SmartWorking ? ModoLavoro.Presenza : ModoLavoro.Distanza;
        var (aggiunti, errore) = await ufficio.AggiungiAsync(id, modo, dal, form.Al ?? dal, AdessoUtc);
        if (aggiunti == 0)
        {
            ModelState.AddModelError("", errore ?? "Nessun giorno da aggiungere.");
            return await MostraForm(form);
        }
        // La presenza in ufficio non si approva mai. Lo smart working va approvato solo per chi lavora in presenza:
        // per chi lavora già a distanza è approvato subito; per gli altri lo approva subito il referente, se lo sceglie.
        var approvaSubito = ServizioUfficio.LavoraADistanza(dip, dal) || (utente.IsAzienda && form.ApprovaSubito);
        if (approvaSubito)
            foreach (var g in db.ChangeTracker.Entries<GiornoSmartWorking>().Where(e => e.State == EntityState.Added))
                g.Entity.Approva(utente.Email, AdessoUtc);
        await db.SaveChangesAsync();

        var giorni = aggiunti == 1 ? "1 giorno" : $"{aggiunti} giorni";
        var messaggio = modo == ModoLavoro.Distanza ? $"{giorni} in ufficio segnat{(aggiunti == 1 ? "o" : "i")}."
            : approvaSubito ? $"Smart working registrato per {giorni}."
            : $"Smart working chiesto per {giorni}: in attesa di approvazione dell'azienda.";
        Conferma(errore is null ? messaggio : $"{messaggio} {errore}");
        if (utente.IsDipendente && !Url.IsLocalUrl(ritorno))
            return RedirectToAction("Index", "Ufficio", new { anno = dal.Year, mese = dal.Month });
        return DopoNuova(id, ritorno);
    }

    /// <summary>Approva o rifiuta uno straordinario: decide il referente dell'azienda.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Ruoli.Azienda)]
    public async Task<IActionResult> DecidiStraordinario(Guid id, bool approva, string? motivo, string? ritorno)
    {
        var s = await db.Straordinari.FirstOrDefaultAsync(x => x.Id == id && Perimetro.Contains(x.DipendenteId));
        if (s is null) return NotFound();
        if (Prova(() => { if (approva) s.Approva(utente.Email, AdessoUtc); else s.Rifiuta(utente.Email, motivo, AdessoUtc); }))
        {
            await db.SaveChangesAsync();
            Conferma(approva ? "Straordinario approvato." : "Straordinario rifiutato.");
        }
        else Errore(PrimoErrore());
        return Torna(ritorno);
    }

    /// <summary>
    /// Il referente revoca una richiesta attiva (anche approvata e già passata): assenze e straordinari passano ad annullati,
    /// lo smart working si toglie. Non conta più in saldi, calendario e riepilogo presenze.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Ruoli.Azienda)]
    public async Task<IActionResult> Revoca(Guid id, string? genere, string? ritorno)
    {
        switch (genere)
        {
            case "smart":
                var g = await db.GiorniSmartWorking.FirstOrDefaultAsync(x => x.Id == id && Perimetro.Contains(x.DipendenteId));
                if (g is null) return NotFound();
                db.GiorniSmartWorking.Remove(g);
                break;
            case "straordinario":
                var s = await db.Straordinari.FirstOrDefaultAsync(x => x.Id == id && Perimetro.Contains(x.DipendenteId));
                if (s is null) return NotFound();
                if (!Prova(s.Revoca)) { Errore(PrimoErrore()); return Torna(ritorno); }
                break;
            default:
                var r = await Trova(id);
                if (r is null) return NotFound();
                if (!Prova(r.Revoca)) { Errore(PrimoErrore()); return Torna(ritorno); }
                break;
        }
        await db.SaveChangesAsync();
        Conferma("Richiesta revocata.");
        return Torna(ritorno);
    }

    /// <summary>Il dipendente chiede di cancellare una richiesta già approvata (anche iniziata): decide l'azienda.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChiediAnnullamento(Guid id, string? motivo, string? ritorno)
    {
        var r = await db.RichiesteAssenza.FirstOrDefaultAsync(x => x.Id == id && x.DipendenteId == utente.DipendenteId);
        if (r is null) return NotFound();
        if (Prova(() => r.ChiediAnnullamento(motivo, AdessoUtc)))
        {
            await db.SaveChangesAsync();
            Conferma("Cancellazione chiesta: l'azienda deve confermarla.");
        }
        else Errore(PrimoErrore());
        return Torna(ritorno);
    }

    /// <summary>L'azienda (o il responsabile che decide) conferma la cancellazione chiesta, che annulla la richiesta, o la respinge.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> DecidiAnnullamento(Guid id, bool conferma, string? ritorno) =>
        Decidi(id, ritorno, r =>
        {
            if (!r.AnnullamentoInAttesa) throw new DomainException("Non c'è una cancellazione da decidere.");
            if (conferma) r.Revoca(); else r.RespingiAnnullamento();
        }, conferma ? "Cancellazione confermata: la richiesta è annullata." : "Cancellazione respinta: la richiesta resta approvata.");

    /// <summary>Il dipendente ritira uno straordinario ancora in attesa.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RitiraStraordinario(Guid id, string? ritorno)
    {
        var s = await db.Straordinari.FirstOrDefaultAsync(x => x.Id == id && x.DipendenteId == utente.DipendenteId && x.Stato == StatoRichiesta.InAttesa);
        if (s is null) return NotFound();
        db.Straordinari.Remove(s);
        await db.SaveChangesAsync();
        Conferma("Richiesta di straordinario ritirata.");
        return Torna(ritorno);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Approva(Guid id, string? ritorno) =>
        Decidi(id, ritorno, r => r.Approva(utente.Email, AdessoUtc), "Richiesta approvata.", avvisaDipendente: true);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Rifiuta(Guid id, string? motivo, string? ritorno) =>
        Decidi(id, ritorno, r => r.Rifiuta(utente.Email, motivo ?? "", AdessoUtc), "Richiesta rifiutata.", avvisaDipendente: true);

    /// <summary>Annulla una richiesta in attesa, o approvata ma non ancora iniziata (vedi <see cref="RichiestaAssenza.Annulla"/>).</summary>
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

    /// <summary>Approva o rifiuta un giorno di smart working: decide il referente dell'azienda, come per le ferie.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Ruoli.Azienda)]
    public async Task<IActionResult> DecidiSmartWorking(Guid id, bool approva, string? motivo, string? ritorno)
    {
        var g = await db.GiorniSmartWorking.FirstOrDefaultAsync(x => x.Id == id && Perimetro.Contains(x.DipendenteId));
        if (g is null) return NotFound();
        if (Prova(() => { if (approva) g.Approva(utente.Email, AdessoUtc); else g.Rifiuta(utente.Email, motivo, AdessoUtc); }))
        {
            await db.SaveChangesAsync();
            Conferma(approva ? "Smart working approvato." : "Smart working rifiutato.");
        }
        else Errore(PrimoErrore());
        return Torna(ritorno);
    }

    /// <summary>
    /// Calendario del mese, giorno per giorno: presenti (in ufficio o da remoto), assenti (approvati e in attesa),
    /// chiusure aziendali e festività. Filtri per azienda (consulente), team e luogo di lavoro.
    /// </summary>
    [Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> Calendario(int? anno, int? mese, Guid? aziendaId, Guid? teamId, string? luogo)
    {
        var primo = new DateOnly(anno ?? Oggi.Year, mese is >= 1 and <= 12 ? mese.Value : Oggi.Month, 1);
        var ultimo = primo.AddMonths(1).AddDays(-1);
        if (luogo is not ("ufficio" or "remoto")) luogo = null;
        // La griglia mostra settimane intere, da lunedì a domenica.
        var inizio = primo.AddDays(-(((int)primo.DayOfWeek + 6) % 7));
        var fine = ultimo.AddDays(6 - (((int)ultimo.DayOfWeek + 6) % 7));

        var query = utente.Filtra(db.Dipendenti.AsNoTracking());
        if (utente.IsConsulente && aziendaId is { } az) query = query.Where(d => d.AziendaId == az);
        // Team del filtro: quelli dell'azienda guardata; scelto un team valgono anche i suoi sotto-team, a ogni livello.
        var aziendaTeam = utente.IsAzienda ? utente.AziendaId : aziendaId;
        var teamAzienda = aziendaTeam is { } at ? await organigramma.TeamAsync(at) : [];
        if (teamId is { } tid && teamAzienda.Any(t => t.Id == tid))
        {
            var inclusi = new HashSet<Guid> { tid };
            for (var aggiunti = true; aggiunti;)
            {
                aggiunti = false;
                foreach (var t in teamAzienda)
                    if (t.PadreId is { } p && inclusi.Contains(p) && inclusi.Add(t.Id)) aggiunti = true;
            }
            query = query.Where(d => d.TeamId != null && inclusi.Contains(d.TeamId.Value));
        }
        else teamId = null;
        // Chi ha un contratto in qualche giorno del mese, in ordine alfabetico (l'ordine resta nel dizionario).
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
        var coloriTeam = tuttiTeam.ToDictionary(t => t.Id, t => Organigramma.ColoreDi(t, tuttiTeam));
        PersonaCalendario Persona(Dipendente d) => new(d.Id, d.NomeCompleto, d.FotoChiave is not null, d.Segnaposto,
            d.TeamId is { } tid ? coloriTeam.GetValueOrDefault(tid) : null);

        var aziendeVisibili = utente.IsAzienda ? [utente.AziendaId!.Value]
            : aziendaId is { } azc ? [azc] : await db.Aziende.AsNoTracking().Select(a => a.Id).ToListAsync();
        var chiusure = await (from c in db.ChiusureAziendali.AsNoTracking()
                              join a in db.Aziende.AsNoTracking() on c.AziendaId equals a.Id
                              where aziendeVisibili.Contains(c.AziendaId) && c.Dal <= fine && c.Al >= inizio
                              orderby c.Dal
                              select new ChiusuraRiga(c.Id, a.RagioneSociale, c.Dal, c.Al, c.Descrizione)).ToListAsync();
        // Con più aziende nel calendario, ogni chiusura dice di quale azienda è.
        var piuAziende = chiusure.Select(c => c.Azienda).Distinct().Count() > 1 || (utente.IsConsulente && aziendaId is null);

        var inUfficio = (await db.GiorniInUfficio.AsNoTracking().Where(u => ids.Contains(u.DipendenteId) && u.Giorno >= primo && u.Giorno <= ultimo)
                .Select(u => new { u.DipendenteId, u.Giorno }).ToListAsync())
            .ToLookup(u => u.Giorno, u => u.DipendenteId);
        var smartWorking = (await db.GiorniSmartWorking.AsNoTracking()
                .Where(u => ids.Contains(u.DipendenteId) && u.Stato == StatoSmartWorking.Approvato && u.Giorno >= primo && u.Giorno <= ultimo)
                .Select(u => new { u.DipendenteId, u.Giorno }).ToListAsync())
            .ToLookup(u => u.Giorno, u => u.DipendenteId);

        // Santo patrono solo se si guarda una sola azienda; il calendario dell'azienda è sempre su 5 giorni.
        var unica = aziendeVisibili.Count == 1 ? await db.Aziende.AsNoTracking().FirstOrDefaultAsync(a => a.Id == aziendeVisibili[0]) : null;
        var giorni = new List<GiornoCalendario>();
        for (var giorno = inizio; giorno <= fine; giorno = giorno.AddDays(1))
        {
            var festa = CalendarioLavorativo.NomeFestivita(giorno, unica?.SantoPatronoMese, unica?.SantoPatronoGiorno);
            var delMese = giorno.Month == primo.Month;
            var lavorativo = CalendarioLavorativo.IsLavorativo(giorno, 5, unica?.SantoPatronoMese, unica?.SantoPatronoGiorno);
            var chiuso = delMese
                ? chiusure.Where(c => c.Dal <= giorno && c.Al >= giorno).Select(c => piuAziende ? $"{c.Descrizione} · {c.Azienda}" : c.Descrizione).ToList()
                : [];
            if (!delMese || !lavorativo) { giorni.Add(new GiornoCalendario(giorno, delMese, lavorativo, [], []) { Chiusure = chiuso, Festa = festa }); continue; }
            var inForza = persone.Values.Where(d => d.InForzaIl(giorno)).ToList();
            var assenti = richieste.Where(r => r.Dal <= giorno && r.Al >= giorno && persone[r.DipendenteId].InForzaIl(giorno))
                .OrderBy(r => r.Stato).ThenBy(r => persone[r.DipendenteId].Cognome)
                .Select(r => new AssenzaCalendario(Persona(persone[r.DipendenteId]), r.Tipo, r.Stato, r.AOre, r.Ore)).ToList();
            // Chi è assente tutto il giorno (approvato) non è tra i presenti; le assenze a ore e quelle in attesa sì.
            var fuori = assenti.Where(a => !a.AOre && a.Stato == StatoRichiesta.Approvata).Select(a => a.Persona.Id).ToHashSet();
            // In sede: chi ha segnato il giorno in ufficio, o chi lavora in presenza e quel giorno non è in smart working.
            var segnati = inUfficio[giorno].ToHashSet();
            var aCasa = smartWorking[giorno].ToHashSet();
            bool InSede(Dipendente d) => segnati.Contains(d.Id) || (!ServizioUfficio.LavoraADistanza(d, giorno) && !aCasa.Contains(d.Id));
            var presenti = inForza.Where(d => !fuori.Contains(d.Id) && luogo switch { "ufficio" => InSede(d), "remoto" => !InSede(d), _ => true }).ToList();
            giorni.Add(new GiornoCalendario(giorno, true, true, presenti.Select(Persona).ToList(), assenti)
            {
                Chiusure = chiuso,
                Festa = festa,
                InUfficio = luogo == "remoto" ? [] : presenti.Where(d => segnati.Contains(d.Id)).Select(Persona).OrderBy(p => p.Nome).ToList(),
                InSmartWorking = luogo == "ufficio" ? [] : presenti.Where(d => aCasa.Contains(d.Id)).Select(Persona).OrderBy(p => p.Nome).ToList(),
            });
        }
        return View(new CalendarioAssenzeViewModel
        {
            Anno = primo.Year, Mese = primo.Month, Giorni = giorni, Oggi = Oggi, AziendaId = aziendaId, TeamId = teamId, Luogo = luogo,
            Team = teamAzienda.Select(t => new TeamFiltro(t.Id, t.Nome, Organigramma.ColoreDi(t, teamAzienda), t.Emoji)).ToList(),
            Chiusure = chiusure.Where(c => c.Dal <= ultimo && c.Al >= primo).ToList(),
            Aziende = await ElencoAziende(aziendaId),
        });
    }

    /// <summary>
    /// Nuova chiusura aziendale: per ogni persona in forza nel periodo si registrano ferie già approvate, collegate alla chiusura.
    /// Chi è già assente in quei giorni o non ha un contratto per tutto il periodo viene saltato (e conteggiato nel messaggio).
    /// I professionisti esterni non hanno ferie: non sono coinvolti.
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
        var adesso = AdessoUtc;
        ChiusuraAziendale chiusura;
        try { chiusura = new ChiusuraAziendale(azienda.Id, inizio, fine, descrizione, utente.Email, adesso); }
        catch (DomainException ex)
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
            catch (DomainException) { saltate++; }
        }
        await db.SaveChangesAsync();
        Conferma($"Chiusura \"{chiusura.Descrizione}\" dal {inizio:dd/MM/yyyy} al {fine:dd/MM/yyyy} salvata: ferie registrate per {registrate} {(registrate == 1 ? "persona" : "persone")}"
                 + (saltate > 0 ? $", {saltate} {(saltate == 1 ? "saltata" : "saltate")} (già assenti o senza contratto per tutto il periodo)." : "."));
        return RedirectToAction(nameof(Calendario), new { anno = inizio.Year, mese = inizio.Month, aziendaId });
    }

    /// <summary>Elimina la chiusura insieme alle ferie che aveva registrato.</summary>
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
    /// Decisione su una richiesta di assenza da parte di chi può prenderla (<see cref="DaDecidere"/>): le regole violate
    /// tornano come messaggio d'errore. Con <paramref name="avvisaDipendente"/> il dipendente riceve l'esito per email.
    /// </summary>
    private async Task<IActionResult> Decidi(Guid id, string? ritorno, Action<RichiestaAssenza> decisione, string messaggio, bool avvisaDipendente = false)
    {
        var r = await DaDecidere(id);
        if (r is null) return utente.IsAzienda ? NotFound() : Forbid();
        if (Prova(() => decisione(r)))
        {
            await db.SaveChangesAsync();
            if (avvisaDipendente) await notifiche.EsitoAsync(r);
            Conferma(messaggio);
        }
        else Errore(PrimoErrore());
        return Torna(ritorno);
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

    private Task<RichiestaAssenza?> Trova(Guid id) =>
        db.RichiesteAssenza.FirstOrDefaultAsync(r => r.Id == id && Perimetro.Contains(r.DipendenteId));

    /// <summary>Il consulente annulla solo ciò che non richiede la decisione dell'azienda (es. malattia registrata per errore).</summary>
    private bool PuoAnnullare(TipoAssenza tipo) => !utente.IsConsulente || !tipo.RichiedeApprovazione();

    private string PrimoErrore() => ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault() ?? "Operazione non riuscita.";

    private IActionResult Torna(string? ritorno) =>
        Url.IsLocalUrl(ritorno) ? LocalRedirect(ritorno) : RedirectToAction(nameof(Index));

    /// <summary>Dopo una nuova richiesta: la pagina di provenienza, altrimenti le richieste (dipendente) o la scheda della persona.</summary>
    private IActionResult DopoNuova(Guid dipendenteId, string? ritorno) =>
        Url.IsLocalUrl(ritorno) ? LocalRedirect(ritorno)
        : utente.IsDipendente ? RedirectToAction(nameof(Index))
        : RedirectToAction("Dettaglio", "Dipendenti", new { id = dipendenteId, sezione = "ferie" });

    /// <summary>Aziende per il filtro del consulente; null per gli altri ruoli, che vedono solo la propria.</summary>
    private async Task<List<SelectListItem>?> ElencoAziende(Guid? scelta) => utente.IsConsulente
        ? await db.Aziende.AsNoTracking().OrderBy(a => a.RagioneSociale)
            .Select(a => new SelectListItem(a.RagioneSociale, a.Id.ToString(), a.Id == scelta)).ToListAsync()
        : null;

    private Task<Azienda?> AziendaDi(Guid dipendenteId) =>
        (from d in utente.Filtra(db.Dipendenti.AsNoTracking().IgnoreAutoIncludes())
         join a in db.Aziende.AsNoTracking() on d.AziendaId equals a.Id
         where d.Id == dipendenteId
         select a).FirstOrDefaultAsync();

    private async Task<IActionResult> MostraForm(RichiestaForm form)
    {
        // Tipi offerti dall'azienda (per il consulente, che sceglie tra più aziende, li controlla il salvataggio).
        var aziendaModulo = utente.IsAzienda ? await db.Aziende.AsNoTracking().FirstOrDefaultAsync(a => a.Id == utente.AziendaId)
            : utente.IsDipendente && utente.DipendenteId is { } me ? await AziendaDi(me) : null;
        ViewBag.Abilitata = aziendaModulo is null ? (Func<string, bool>)(_ => true) : aziendaModulo.Abilitata;
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

        // Il dipendente vede quanto gli resta a fine anno, mentre compila.
        if (utente.IsDipendente && utente.DipendenteId is { } mio
            && await db.Dipendenti.AsNoTracking().FirstOrDefaultAsync(d => d.Id == mio) is { } io)
            ViewBag.Saldi = await saldi.Calcola(io, Oggi.Year, 12);

        return View("Nuova", form);
    }
}
