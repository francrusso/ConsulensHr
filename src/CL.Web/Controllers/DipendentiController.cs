using System.ComponentModel.DataAnnotations;
using CL.Domain.Dipendenti;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Archivio;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

public sealed class DipendenteForm
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Seleziona l'azienda."), Display(Name = "Azienda")]
    public Guid? AziendaId { get; set; }

    [Required(ErrorMessage = "Inserisci il nome."), StringLength(100), Display(Name = "Nome")]
    public string Nome { get; set; } = "";

    [Required(ErrorMessage = "Inserisci il cognome."), StringLength(100), Display(Name = "Cognome")]
    public string Cognome { get; set; } = "";

    [Required(ErrorMessage = "Inserisci il codice fiscale."), Display(Name = "Codice fiscale")]
    public string CodiceFiscale { get; set; } = "";

    [Required(ErrorMessage = "Inserisci la data di nascita."), Display(Name = "Data di nascita")]
    public DateOnly? DataNascita { get; set; }

    [EmailAddress(ErrorMessage = "Email non valida."), StringLength(256), Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(30), Display(Name = "Telefono")]
    public string? Telefono { get; set; }

    [StringLength(300), Display(Name = "Indirizzo di residenza")]
    public string? Indirizzo { get; set; }

    [Display(Name = "IBAN per l'accredito dello stipendio")]
    public string? Iban { get; set; }

    [StringLength(30), Display(Name = "Matricola")]
    public string? Matricola { get; set; }

    [Display(Name = "Genere")]
    public Genere? Genere { get; set; }
    [Display(Name = "Pronomi")]
    public Pronomi? Pronomi { get; set; }

    [Display(Name = "Foto (PNG, JPEG o WebP, massimo 2 MB)")]
    public IFormFile? Foto { get; set; }
    [Display(Name = "Togli la foto attuale")]
    public bool RimuoviFoto { get; set; }
    public bool HaFoto { get; set; }

    public static DipendenteForm Da(Dipendente d) => new()
    {
        Id = d.Id,
        AziendaId = d.AziendaId,
        Nome = d.Nome,
        Cognome = d.Cognome,
        CodiceFiscale = d.CodiceFiscale.Valore,
        DataNascita = d.DataNascita,
        Email = d.Email,
        Telefono = d.Telefono,
        Indirizzo = d.Indirizzo,
        Iban = d.Iban?.Valore,
        Matricola = d.Matricola,
        HaFoto = d.FotoChiave is not null,
        Genere = d.Genere,
        Pronomi = d.Pronomi,
    };
}

public sealed class ContrattoForm
{
    public Guid DipendenteId { get; set; }
    public string NomeDipendente { get; set; } = "";

    [Display(Name = "Tipo di contratto")]
    public TipoContratto Tipo { get; set; } = TipoContratto.TempoIndeterminato;

    // Obbligatori solo per il lavoro dipendente (controllo nell'azione).
    [StringLength(100), Display(Name = "CCNL applicato")]
    public string? Ccnl { get; set; }

    [StringLength(20), Display(Name = "Livello")]
    public string? Livello { get; set; }

    [StringLength(150), Display(Name = "Mansione")]
    public string? Mansione { get; set; }

    [Required(ErrorMessage = "Inserisci la data di inizio."), Display(Name = "Data di inizio")]
    public DateOnly? DataInizio { get; set; }

    [Display(Name = "Data di fine")]
    public DateOnly? DataFine { get; set; }

    [Required(ErrorMessage = "Inserisci la retribuzione."), Display(Name = "Retribuzione lorda annua (€)")]
    public decimal? RetribuzioneLordaAnnua { get; set; }

    [Range(12, 16, ErrorMessage = "Tra 12 e 16."), Display(Name = "Mensilità")]
    public int MensilitaAnnue { get; set; } = 14;

    [Range(1, 48, ErrorMessage = "Tra 1 e 48."), Display(Name = "Ore settimanali full-time (CCNL)")]
    public decimal OreSettimanaliFullTime { get; set; } = 40;

    [Range(1, 100, ErrorMessage = "Tra 1 e 100."), Display(Name = "Percentuale part-time")]
    public decimal PercentualePartTime { get; set; } = 100;

    [Range(5, 6, ErrorMessage = "5 o 6."), Display(Name = "Giorni lavorativi a settimana")]
    public int GiorniLavorativiSettimana { get; set; } = 5;

    [Range(0, 400), Display(Name = "Ferie annue (ore, full-time)")]
    public decimal OreFerieAnnue { get; set; } = 160;

    [Range(0, 400), Display(Name = "ROL annui (ore, full-time)")]
    public decimal OreRolAnnue { get; set; } = 72;

    [Range(0, 400), Display(Name = "Ex-festività annue (ore, full-time)")]
    public decimal OreExFestivitaAnnue { get; set; } = 32;

    /// <summary>Tipi raggruppati: lavoro dipendente e professionisti esterni.</summary>
    public static IEnumerable<SelectListItem> Tipi
    {
        get
        {
            var dipendente = new SelectListGroup { Name = "Lavoro dipendente" };
            var esterni = new SelectListGroup { Name = "Professionisti esterni" };
            return Enum.GetValues<TipoContratto>().Select(t => new SelectListItem(t.Descrizione(), t.ToString()) { Group = t.IsSubordinato() ? dipendente : esterni });
        }
    }
}

/// <summary>Stato del rapporto ricavato dai contratti.</summary>
public enum StatoPersonale { Attivo, InArrivo, SenzaContratto, Cessato }

public static class StatoPersonaleExtensions
{
    public static string Descrizione(this StatoPersonale s, bool plurale = false) => s switch
    {
        StatoPersonale.Attivo => plurale ? "Attivi" : "Attivo",
        StatoPersonale.InArrivo => "In arrivo",
        StatoPersonale.SenzaContratto => "Senza contratto",
        _ => plurale ? "Cessati" : "Cessato",
    };

    /// <summary>Colore del pallino (stato: verde attivo, blu in arrivo, giallo senza contratto, grigio cessato).</summary>
    public static string Colore(this StatoPersonale s) => s switch
    {
        StatoPersonale.Attivo => "#2FBA6C",
        StatoPersonale.InArrivo => "#6096FF",
        StatoPersonale.SenzaContratto => "#F2B544",
        _ => "#9AA3AE",
    };

    public static string Chiave(this StatoPersonale s) => s switch
    {
        StatoPersonale.Attivo => "attivi",
        StatoPersonale.InArrivo => "in-arrivo",
        StatoPersonale.SenzaContratto => "senza-contratto",
        _ => "cessati",
    };
}

/// <summary>Riga dell'elenco: <paramref name="Inquadramento"/> è il livello per i dipendenti, il tipo di rapporto per gli esterni.</summary>
public sealed record DipendenteRiga(Guid Id, string NomeCompleto, string CodiceFiscale, string Azienda, string? Inquadramento, StatoPersonale Stato,
    bool HaFoto = false, string Segnaposto = "neutro")
{
    /// <summary>Posizione nell'organigramma o, in mancanza, mansione del contratto.</summary>
    public string? Profilo { get; init; }
    public TeamRiga? Team { get; init; }
    /// <summary>Retribuzione lorda annua (o compenso) del contratto in corso, altrimenti dell'ultimo.</summary>
    public decimal? Ral { get; init; }
    /// <summary>Mensilità annue del contratto di riferimento (12, 13, 14…).</summary>
    public int? Mensilita { get; init; }
    /// <summary>Lordo mensile: RAL divisa per tutte le mensilità.</summary>
    public decimal? MensileLordo => Ral is > 0 && Mensilita is > 0 ? Math.Round(Ral.Value / Mensilita.Value, 2) : null;
    /// <summary>Fine del contratto di riferimento (null = senza scadenza).</summary>
    public DateOnly? Scadenza { get; init; }
}

public sealed record TeamRiga(string Nome, string Colore, string? Emoji);

[Authorize(Policy = Policy.GestionePersonale)]
public sealed class DipendentiController(AppDbContext db, UtenteCorrente utente, ServizioSaldi saldi, ServizioAccessi accessi,
    ServizioOrganigramma organigramma, FotoDipendenti foto, IArchivioDocumenti archivio, TimeProvider clock) : AppController
{
    private DateOnly Oggi => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<IActionResult> Index(string? q, Guid? aziendaId, bool esterni = false, string? stato = null)
    {
        var oggi = Oggi;
        var query = utente.Filtra(db.Dipendenti.AsNoTracking());
        if (aziendaId is { } az) query = query.Where(d => d.AziendaId == az);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim().ToLower();
            query = query.Where(d => d.Cognome.ToLower().Contains(t) || d.Nome.ToLower().Contains(t)
                                     || ((string)(object)d.CodiceFiscale).ToLower().Contains(t));
        }
        var attivo = Filtri.ContrattoAttivoIl(oggi);

        // Personale dipendente / professionisti esterni, in base al contratto più recente (senza contratto: tra i dipendenti).
        var tipiEsterni = TipoContrattoExtensions.Esterni.ToList();
        var esterno = (System.Linq.Expressions.Expression<Func<Dipendente, bool>>)(d =>
            tipiEsterni.Contains(d.Contratti.OrderByDescending(c => c.DataInizio).Select(c => c.Tipo).FirstOrDefault()) && d.Contratti.Any());
        ViewBag.NumeroEsterni = await query.CountAsync(esterno);
        ViewBag.NumeroDipendenti = await query.CountAsync() - (int)ViewBag.NumeroEsterni;
        query = esterni ? query.Where(esterno) : query.Where(System.Linq.Expressions.Expression.Lambda<Func<Dipendente, bool>>(
            System.Linq.Expressions.Expression.Not(esterno.Body), esterno.Parameters));

        // Stati del rapporto, con i conteggi per la barra sotto i filtri.
        var condizioni = new Dictionary<StatoPersonale, System.Linq.Expressions.Expression<Func<Dipendente, bool>>>
        {
            [StatoPersonale.Attivo] = d => d.Contratti.AsQueryable().Any(attivo),
            [StatoPersonale.InArrivo] = d => !d.Contratti.AsQueryable().Any(attivo) && d.Contratti.Any(c => c.DataInizio > oggi),
            [StatoPersonale.SenzaContratto] = d => !d.Contratti.Any(),
            [StatoPersonale.Cessato] = d => d.Contratti.Any() && !d.Contratti.AsQueryable().Any(attivo) && !d.Contratti.Any(c => c.DataInizio > oggi),
        };
        var conteggi = new Dictionary<StatoPersonale, int>();
        foreach (var (st, cond) in condizioni) conteggi[st] = await query.CountAsync(cond);
        ViewBag.Conteggi = conteggi;
        ViewBag.Totale = conteggi.Values.Sum();
        StatoPersonale? filtroStato = Enum.GetValues<StatoPersonale>().Cast<StatoPersonale?>().FirstOrDefault(x => x!.Value.Chiave() == stato);
        if (filtroStato is { } fs) query = query.Where(condizioni[fs]);
        ViewBag.Stato = filtroStato;

        // Solo le colonne mostrate, con azienda, livello e contratto di riferimento calcolati nel database (niente contratti né elenco aziende in memoria).
        var righeDb = (await query.OrderBy(d => d.Cognome).ThenBy(d => d.Nome)
                .Select(d => new
                {
                    d.Id, d.Cognome, d.Nome, d.CodiceFiscale,
                    Azienda = db.Aziende.Where(a => a.Id == d.AziendaId).Select(a => a.RagioneSociale).FirstOrDefault(),
                    Livello = d.Contratti.AsQueryable().Where(attivo).Select(c => c.Livello).FirstOrDefault(),
                    Tipo = d.Contratti.OrderByDescending(c => c.DataInizio).Select(c => (TipoContratto?)c.Tipo).FirstOrDefault(),
                    InForza = d.Contratti.AsQueryable().Any(attivo),
                    InArrivo = d.Contratti.Any(c => c.DataInizio > oggi),
                    HaContratti = d.Contratti.Any(),
                    HaFoto = d.FotoChiave != null, d.Genere, d.Pronomi, d.Posizione, d.TeamId,
                    // Contratto di riferimento: quello in corso, altrimenti il più recente.
                    MansioneA = d.Contratti.AsQueryable().Where(attivo).OrderByDescending(c => c.DataInizio).Select(c => c.Mansione).FirstOrDefault(),
                    RalA = d.Contratti.AsQueryable().Where(attivo).OrderByDescending(c => c.DataInizio).Select(c => (decimal?)c.RetribuzioneLordaAnnua).FirstOrDefault(),
                    MensA = d.Contratti.AsQueryable().Where(attivo).OrderByDescending(c => c.DataInizio).Select(c => (int?)c.MensilitaAnnue).FirstOrDefault(),
                    MensU = d.Contratti.OrderByDescending(c => c.DataInizio).Select(c => (int?)c.MensilitaAnnue).FirstOrDefault(),
                    FineA = d.Contratti.AsQueryable().Where(attivo).OrderByDescending(c => c.DataInizio).Select(c => c.DataFine).FirstOrDefault(),
                    MansioneU = d.Contratti.OrderByDescending(c => c.DataInizio).Select(c => c.Mansione).FirstOrDefault(),
                    RalU = d.Contratti.OrderByDescending(c => c.DataInizio).Select(c => (decimal?)c.RetribuzioneLordaAnnua).FirstOrDefault(),
                    FineU = d.Contratti.OrderByDescending(c => c.DataInizio).Select(c => c.DataFine).FirstOrDefault(),
                })
                .ToListAsync());
        var idTeam = righeDb.Where(d => d.TeamId != null).Select(d => d.TeamId!.Value).Distinct().ToList();
        var team = idTeam.Count == 0 ? [] : await db.Team.AsNoTracking().Where(t => idTeam.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => new TeamRiga(t.Nome, t.Colore, t.Emoji));
        var righe = righeDb
            .Select(d => new DipendenteRiga(d.Id, $"{d.Cognome} {d.Nome}", d.CodiceFiscale.Valore, d.Azienda ?? "—",
                d.Tipo is { } tipo && !tipo.IsSubordinato() ? tipo.Descrizione() : string.IsNullOrEmpty(d.Livello) ? null : d.Livello,
                d.InForza ? StatoPersonale.Attivo : d.InArrivo ? StatoPersonale.InArrivo : !d.HaContratti ? StatoPersonale.SenzaContratto : StatoPersonale.Cessato,
                d.HaFoto, Dipendente.SegnapostoPer(d.Genere, d.Pronomi))
            {
                Profilo = !string.IsNullOrWhiteSpace(d.Posizione) ? d.Posizione : d.InForza ? d.MansioneA : d.MansioneU,
                Team = d.TeamId is { } t ? team.GetValueOrDefault(t) : null,
                Ral = d.InForza ? d.RalA : d.RalU,
                Mensilita = d.InForza ? d.MensA : d.MensU,
                Scadenza = d.InForza ? d.FineA : d.FineU,
            })
            .ToList();

        ViewBag.Oggi = oggi;
        ViewBag.Ricerca = q;
        ViewBag.Esterni = esterni;
        ViewBag.AziendaId = aziendaId;
        ViewBag.Aziende = utente.IsConsulente ? await ElencoAziende(aziendaId) : null;
        return View(righe);
    }

    public async Task<IActionResult> Nuovo(Guid? aziendaId)
    {
        var form = new DipendenteForm { AziendaId = utente.IsAzienda ? utente.AziendaId : aziendaId };
        return await MostraForm(form);
    }

    public async Task<IActionResult> Modifica(Guid id)
    {
        var d = await Trova(id);
        return d is null ? NotFound() : await MostraForm(DipendenteForm.Da(d));
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(Immagini.DimensioneMassima + 256 * 1024)]
    public async Task<IActionResult> Salva(DipendenteForm form)
    {
        if (utente.IsAzienda) form.AziendaId = utente.AziendaId; // il referente non può scegliere un'altra azienda
        if (!ModelState.IsValid) return await MostraForm(form);
        if (form.AziendaId is not { } aziendaId || !utente.PuoGestireAzienda(aziendaId) || !await db.Aziende.AnyAsync(a => a.Id == aziendaId))
            return Forbid();

        CodiceFiscale? cf = null;
        Iban? iban = null;
        var ok = Prova(() => cf = CodiceFiscale.Parse(form.CodiceFiscale), nameof(form.CodiceFiscale))
                 & (string.IsNullOrWhiteSpace(form.Iban) || Prova(() => iban = Iban.Parse(form.Iban), nameof(form.Iban)));
        if (!ok) return await MostraForm(form);

        if (await db.Dipendenti.AnyAsync(d => d.AziendaId == aziendaId && d.CodiceFiscale == cf && d.Id != form.Id))
        {
            ModelState.AddModelError(nameof(form.CodiceFiscale), "Esiste già un dipendente con questo codice fiscale in questa azienda.");
            return await MostraForm(form);
        }

        Dipendente? dip;
        if (form.Id is { } id)
        {
            dip = await Trova(id);
            if (dip is null) return NotFound();
            if (!Prova(() => dip.CorreggiAnagrafica(form.Nome, form.Cognome, cf!, form.DataNascita!.Value), nameof(form.DataNascita)))
                return await MostraForm(form);
        }
        else
        {
            dip = new Dipendente(aziendaId, form.Nome, form.Cognome, cf!, form.DataNascita!.Value);
            db.Dipendenti.Add(dip);
        }
        dip.AggiornaRecapiti(form.Email, form.Telefono, form.Indirizzo);
        dip.ImpostaIban(iban);
        dip.ImpostaMatricola(form.Matricola);
        dip.ImpostaIdentita(form.Genere, form.Pronomi);
        if (form.Foto is { Length: > 0 } file && await foto.SalvaAsync(dip, file) is { } erroreFoto)
        {
            ModelState.AddModelError(nameof(form.Foto), erroreFoto);
            return await MostraForm(form);
        }
        if (form.RimuoviFoto && form.Foto is null) await foto.RimuoviAsync(dip);
        await db.SaveChangesAsync();

        if (form.Id is null)
        {
            Conferma($"{dip.NomeCompleto} aggiunto. Ora registra il contratto di assunzione.");
            return RedirectToAction(nameof(Assumi), new { id = dip.Id });
        }
        Conferma("Modifiche salvate.");
        return RedirectToAction(nameof(Dettaglio), new { id = dip.Id });
    }

    public async Task<IActionResult> Dettaglio(Guid id)
    {
        // Dipendente (con contratti) e ragione sociale in una sola query; le richieste dell'anno servono sia
        // alla tabella sia al calcolo dei saldi, quindi si caricano una volta sola.
        var trovato = await utente.Filtra(db.Dipendenti.AsNoTracking()).Where(x => x.Id == id)
            .Select(x => new { Dipendente = x, Azienda = db.Aziende.Where(a => a.Id == x.AziendaId).Select(a => a.RagioneSociale).FirstOrDefault() })
            .FirstOrDefaultAsync();
        if (trovato is null) return NotFound();
        var d = trovato.Dipendente;
        var oggi = Oggi;
        var richieste = await saldi.RichiesteAnno(d.Id, oggi.Year);
        ViewBag.Azienda = trovato.Azienda;
        ViewBag.Accesso = await accessi.Elenco().FirstOrDefaultAsync(u => u.DipendenteId == d.Id);
        var approvatore = await organigramma.ApprovatoreAsync(d.Id);
        ViewBag.Organizzazione = new
        {
            Team = d.TeamId is { } tid ? await db.Team.Where(t => t.Id == tid).Select(t => t.Nome).FirstOrDefaultAsync() : null,
            Approvatore = approvatore is { } aid
                ? await db.Dipendenti.IgnoreAutoIncludes().Where(x => x.Id == aid).Select(x => x.Cognome + " " + x.Nome).FirstOrDefaultAsync()
                : null,
        };
        ViewBag.ProfiloPersonale = await db.ProfiliDipendente.AsNoTracking().IgnoreAutoIncludes()
            .Where(p => p.DipendenteId == d.Id).Select(p => new { p.Passo, p.CompletatoIl, p.AggiornatoIl }).FirstOrDefaultAsync();
        ViewBag.Buste = await db.BustePaga.AsNoTracking().Where(b => b.DipendenteId == d.Id)
            .OrderByDescending(b => b.Anno).ThenByDescending(b => b.Mese).Take(24)
            .Select(b => new BustaRiga
            {
                Id = b.Id, DipendenteId = b.DipendenteId, Dipendente = "", Azienda = "", Anno = b.Anno, Mese = b.Mese,
                Tipo = b.Tipo, Netto = b.Netto, Stato = b.Stato, PubblicataIl = b.PubblicataIl, VisualizzataIl = b.VisualizzataIl,
            })
            .ToListAsync();
        ViewBag.Oggi = oggi;
        ViewBag.Saldi = await saldi.Calcola(d, oggi.Year, oggi.Month, richieste);
        ViewBag.Richieste = richieste
            .OrderByDescending(r => r.Dal)
            .Select(r => new RichiestaRiga(r.Id, r.DipendenteId, d.NomeCompleto, "", r.Tipo, r.Dal, r.Al, r.AOre, r.Ore,
                r.Stato, r.RichiestaIl, r.Note, r.DecisaDa, r.MotivoRifiuto))
            .ToList();
        return View(d);
    }

    public async Task<IActionResult> Assumi(Guid id)
    {
        var d = await Trova(id);
        if (d is null) return NotFound();
        return View(new ContrattoForm { DipendenteId = d.Id, NomeDipendente = d.NomeCompleto, DataInizio = Oggi });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Assumi(ContrattoForm form)
    {
        var d = await Trova(form.DipendenteId);
        if (d is null) return NotFound();
        form.NomeDipendente = d.NomeCompleto;
        if (form.Tipo.IsSubordinato())
        {
            if (string.IsNullOrWhiteSpace(form.Ccnl)) ModelState.AddModelError(nameof(form.Ccnl), "Inserisci il CCNL.");
            if (string.IsNullOrWhiteSpace(form.Livello)) ModelState.AddModelError(nameof(form.Livello), "Inserisci il livello.");
        }
        if (!ModelState.IsValid) return View(form);

        if (!Prova(() => d.Assumi(form.Tipo, form.Ccnl ?? "", form.Livello ?? "", form.DataInizio!.Value, form.DataFine,
                form.RetribuzioneLordaAnnua!.Value, form.MensilitaAnnue, form.OreSettimanaliFullTime, form.PercentualePartTime,
                form.GiorniLavorativiSettimana, form.OreFerieAnnue, form.OreRolAnnue, form.OreExFestivitaAnnue, form.Mansione)))
            return View(form);

        await db.SaveChangesAsync();
        Conferma(form.Tipo.IsSubordinato() ? "Contratto registrato." : "Incarico registrato.");
        return RedirectToAction(nameof(Dettaglio), new { id = d.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cessa(Guid id, DateOnly? dataCessazione)
    {
        var d = await Trova(id);
        if (d is null) return NotFound();
        if (dataCessazione is not { } data)
        {
            Errore("Indica la data di cessazione.");
            return RedirectToAction(nameof(Dettaglio), new { id });
        }
        if (Prova(() => d.Cessa(data)))
        {
            await db.SaveChangesAsync();
            Conferma($"Cessazione registrata al {data:dd/MM/yyyy}.");
        }
        else Errore(string.Join(" ", ModelState[""]!.Errors.Select(e => e.ErrorMessage)));
        return RedirectToAction(nameof(Dettaglio), new { id });
    }

    /// <summary>
    /// Foto del dipendente: la vedono lo studio, i referenti della sua azienda e il dipendente stesso.
    /// (Supera il "gestione personale" del controller: il login e il perimetro sono verificati qui.)
    /// </summary>
    [AllowAnonymous]
    public async Task<IActionResult> Foto(Guid id)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        var d = await db.Dipendenti.AsNoTracking().IgnoreAutoIncludes().Where(x => x.Id == id)
            .Select(x => new { x.AziendaId, x.FotoChiave }).FirstOrDefaultAsync();
        var ammesso = d is not null && (utente.IsConsulente || (utente.IsAzienda && utente.AziendaId == d.AziendaId) || utente.DipendenteId == id);
        if (!ammesso || d!.FotoChiave is null || await archivio.ApriAsync(d.FotoChiave) is not { } file) return NotFound();
        Response.Headers.CacheControl = "private, max-age=300";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file, Immagini.TipoMime(d.FotoChiave));
    }

    private Task<Dipendente?> Trova(Guid id) => utente.Filtra(db.Dipendenti).FirstOrDefaultAsync(d => d.Id == id);

    private async Task<IActionResult> MostraForm(DipendenteForm form)
    {
        ViewBag.Aziende = utente.IsConsulente ? await ElencoAziende(form.AziendaId) : null;
        if (utente.IsAzienda)
            ViewBag.NomeAzienda = await db.Aziende.Where(a => a.Id == utente.AziendaId).Select(a => a.RagioneSociale).FirstOrDefaultAsync();
        return View("Form", form);
    }

    private async Task<List<SelectListItem>> ElencoAziende(Guid? selezionata) =>
        await db.Aziende.AsNoTracking().Where(a => a.Attiva || a.Id == selezionata).OrderBy(a => a.RagioneSociale)
            .Select(a => new SelectListItem(a.RagioneSociale, a.Id.ToString(), a.Id == selezionata)).ToListAsync();
}
