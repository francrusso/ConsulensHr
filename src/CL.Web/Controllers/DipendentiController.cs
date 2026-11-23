using System.ComponentModel.DataAnnotations;
using CL.Domain.Dipendenti;
using CL.Domain.Common;
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
    [RegularExpression(@"^\d{5}$", ErrorMessage = "Il CAP ha 5 cifre."), Display(Name = "CAP")]
    public string? Cap { get; set; }
    [StringLength(100), Display(Name = "Comune")]
    public string? Comune { get; set; }
    [RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Usa la sigla (es. MI)."), Display(Name = "Provincia")]
    public string? Provincia { get; set; }

    [Display(Name = "IBAN per l'accredito dello stipendio")]
    public string? Iban { get; set; }

    [StringLength(30), Display(Name = "Matricola")]
    public string? Matricola { get; set; }

    [Display(Name = "Team")]
    public Guid? TeamId { get; set; }
    [StringLength(100), Display(Name = "Posizione")]
    public string? Posizione { get; set; }
    [Display(Name = "Responsabile diretto")]
    public Guid? ResponsabileId { get; set; }
    [Display(Name = "Sede di lavoro")]
    public SedeLavoro? Sede { get; set; }

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
        Cap = d.Cap,
        Comune = d.Comune,
        Provincia = d.Provincia,
        Iban = d.Iban?.Valore,
        Matricola = d.Matricola,
        HaFoto = d.FotoChiave is not null,
        Genere = d.Genere,
        Pronomi = d.Pronomi,
        TeamId = d.TeamId,
        Posizione = d.Posizione,
        ResponsabileId = d.ResponsabileId,
        Sede = d.Sede,
    };
}

/// <summary>Giorni del mese: in ufficio, da remoto, assente; con i giorni di smart working e quelli segnati in ufficio.</summary>
public sealed record PresenzeMese(int Mese, int InUfficio, int Remoto, int Assente, List<DateOnly> SmartWorking, List<DateOnly> SegnatiUfficio);

public sealed class ContrattoForm
{
    public Guid DipendenteId { get; set; }
    /// <summary>Contratto da correggere (vuoto per una nuova assunzione).</summary>
    public Guid? ContrattoId { get; set; }

    public static ContrattoForm Da(Contratto c) => new()
    {
        DipendenteId = c.DipendenteId, ContrattoId = c.Id, Tipo = c.Tipo, Ccnl = c.Ccnl, Livello = c.Livello, Mansione = c.Mansione,
        DataInizio = c.DataInizio, DataFine = c.DataFine, RetribuzioneLordaAnnua = c.RetribuzioneLordaAnnua, MensilitaAnnue = c.MensilitaAnnue,
        OreSettimanaliFullTime = c.OreSettimanaliFullTime, PercentualePartTime = c.PercentualePartTime, GiorniLavorativiSettimana = c.GiorniLavorativiSettimana,
        OreFerieAnnue = c.OreFerieAnnue, OreRolAnnue = c.OreRolAnnue,
        Modalita = c.Modalita, GiorniInUfficio = c.GiorniInUfficio,
        GiorniSmartWorking = c.GiorniSmartWorking, PeriodoSmartWorking = c.PeriodoSmartWorking ?? PeriodoSmartWorking.Mese,
    };
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

    [Display(Name = "Modalità di lavoro")]
    public ModalitaLavoro Modalita { get; set; } = ModalitaLavoro.InPresenza;
    [Range(1, 5, ErrorMessage = "Tra 1 e 5."), Display(Name = "Giorni in ufficio a settimana")]
    public int? GiorniInUfficio { get; set; }
    /// <summary>Per chi lavora in presenza: giorni di smart working che può chiedere.</summary>
    [Range(0, 23, ErrorMessage = "Tra 0 e 23."), Display(Name = "Smart working concesso")]
    public int? GiorniSmartWorking { get; set; }
    public PeriodoSmartWorking PeriodoSmartWorking { get; set; } = PeriodoSmartWorking.Mese;

    public static IEnumerable<SelectListItem> ElencoPeriodi =>
        [new("al mese", nameof(PeriodoSmartWorking.Mese)), new("a settimana", nameof(PeriodoSmartWorking.Settimana))];

    public static IEnumerable<SelectListItem> ElencoModalita =>
        Enum.GetValues<ModalitaLavoro>().Select(m => new SelectListItem(m.Descrizione(), m.ToString()));

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
/// <summary>
/// Stato del rapporto, dal contratto più recente: in corso (attivo), firmato ma non ancora iniziato,
/// arrivato alla scadenza naturale, chiuso con una cessazione. Chi non ha contratti è "senza contratto" (solo in "Tutti").
/// </summary>
public enum StatoPersonale { Attivo, Firmato, Scaduto, Cessato, SenzaContratto }

public static class StatoPersonaleExtensions
{
    public static string Descrizione(this StatoPersonale s, bool plurale = false) => s switch
    {
        StatoPersonale.Attivo => plurale ? "Attivi" : "Attivo",
        StatoPersonale.Firmato => plurale ? "Firmati" : "Firmato",
        StatoPersonale.Scaduto => plurale ? "Scaduti" : "Scaduto",
        StatoPersonale.Cessato => plurale ? "Cessati" : "Cessato",
        _ => "Senza contratto",
    };

    /// <summary>Colore del pallino: verde attivo, blu firmato, giallo scaduto, grigio cessato, beige senza contratto.</summary>
    public static string Colore(this StatoPersonale s) => s switch
    {
        StatoPersonale.Attivo => "#2FBA6C",
        StatoPersonale.Firmato => "#6096FF",
        StatoPersonale.Scaduto => "#F2B544",
        StatoPersonale.Cessato => "#9AA3AE",
        _ => "#D2BFAE",
    };

    /// <summary>Gli stati che hanno un filtro sotto la ricerca.</summary>
    public static readonly StatoPersonale[] Filtri = [StatoPersonale.Attivo, StatoPersonale.Firmato, StatoPersonale.Scaduto, StatoPersonale.Cessato];

    public static string Chiave(this StatoPersonale s) => s switch
    {
        StatoPersonale.Attivo => "attivi",
        StatoPersonale.Firmato => "firmati",
        StatoPersonale.Scaduto => "scaduti",
        StatoPersonale.Cessato => "cessati",
        _ => "senza-contratto",
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
    /// <summary>Contratto di riferimento e se il suo PDF è stato caricato.</summary>
    public Guid? ContrattoId { get; init; }
    public bool HaDocumentoContratto { get; init; }
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
            [StatoPersonale.Firmato] = d => !d.Contratti.AsQueryable().Any(attivo) && d.Contratti.Any(c => c.DataInizio > oggi),
            [StatoPersonale.Scaduto] = d => d.Contratti.Any() && !d.Contratti.AsQueryable().Any(attivo) && !d.Contratti.Any(c => c.DataInizio > oggi)
                                            && d.Contratti.OrderByDescending(c => c.DataInizio).Select(c => c.DataCessazione).FirstOrDefault() == null,
            [StatoPersonale.Cessato] = d => d.Contratti.Any() && !d.Contratti.AsQueryable().Any(attivo) && !d.Contratti.Any(c => c.DataInizio > oggi)
                                            && d.Contratti.OrderByDescending(c => c.DataInizio).Select(c => c.DataCessazione).FirstOrDefault() != null,
            [StatoPersonale.SenzaContratto] = d => !d.Contratti.Any(),
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
                    Cessato = d.Contratti.OrderByDescending(c => c.DataInizio).Select(c => c.DataCessazione).FirstOrDefault() != null,
                    HaFoto = d.FotoChiave != null, d.Genere, d.Pronomi, d.Posizione, d.TeamId,
                    // Contratto di riferimento: quello in corso, altrimenti il più recente.
                    MansioneA = d.Contratti.AsQueryable().Where(attivo).OrderByDescending(c => c.DataInizio).Select(c => c.Mansione).FirstOrDefault(),
                    RalA = d.Contratti.AsQueryable().Where(attivo).OrderByDescending(c => c.DataInizio).Select(c => (decimal?)c.RetribuzioneLordaAnnua).FirstOrDefault(),
                    MensA = d.Contratti.AsQueryable().Where(attivo).OrderByDescending(c => c.DataInizio).Select(c => (int?)c.MensilitaAnnue).FirstOrDefault(),
                    MensU = d.Contratti.OrderByDescending(c => c.DataInizio).Select(c => (int?)c.MensilitaAnnue).FirstOrDefault(),
                    FineA = d.Contratti.AsQueryable().Where(attivo).OrderByDescending(c => c.DataInizio).Select(c => c.DataFine).FirstOrDefault(),
                    IdA = d.Contratti.AsQueryable().Where(attivo).OrderByDescending(c => c.DataInizio).Select(c => (Guid?)c.Id).FirstOrDefault(),
                    DocA = d.Contratti.AsQueryable().Where(attivo).OrderByDescending(c => c.DataInizio).Select(c => c.DocumentoChiave != null).FirstOrDefault(),
                    IdU = d.Contratti.OrderByDescending(c => c.DataInizio).Select(c => (Guid?)c.Id).FirstOrDefault(),
                    DocU = d.Contratti.OrderByDescending(c => c.DataInizio).Select(c => c.DocumentoChiave != null).FirstOrDefault(),
                    MansioneU = d.Contratti.OrderByDescending(c => c.DataInizio).Select(c => c.Mansione).FirstOrDefault(),
                    RalU = d.Contratti.OrderByDescending(c => c.DataInizio).Select(c => (decimal?)c.RetribuzioneLordaAnnua).FirstOrDefault(),
                    FineU = d.Contratti.OrderByDescending(c => c.DataInizio).Select(c => c.DataFine).FirstOrDefault(),
                })
                .ToListAsync());
        var idTeam = righeDb.Where(d => d.TeamId != null).Select(d => d.TeamId!.Value).Distinct().ToList();
        // Team con il colore mostrato (i sotto-team ereditano quello del team madre, più chiaro).
        var team = new Dictionary<Guid, TeamRiga>();
        if (idTeam.Count > 0)
        {
            var aziendeTeam = await db.Team.AsNoTracking().Where(t => idTeam.Contains(t.Id)).Select(t => t.AziendaId).Distinct().ToListAsync();
            var tuttiTeam = await db.Team.AsNoTracking().Where(t => aziendeTeam.Contains(t.AziendaId)).ToListAsync();
            foreach (var t in tuttiTeam.Where(t => idTeam.Contains(t.Id)))
                team[t.Id] = new TeamRiga(t.Nome, CL.Domain.Organizzazione.Organigramma.ColoreDi(t, tuttiTeam), t.Emoji);
        }
        var righe = righeDb
            .Select(d => new DipendenteRiga(d.Id, $"{d.Cognome} {d.Nome}", d.CodiceFiscale.Valore, d.Azienda ?? "—",
                d.Tipo is { } tipo && !tipo.IsSubordinato() ? tipo.Descrizione() : string.IsNullOrEmpty(d.Livello) ? null : d.Livello,
                d.InForza ? StatoPersonale.Attivo : d.InArrivo ? StatoPersonale.Firmato : !d.HaContratti ? StatoPersonale.SenzaContratto
                    : d.Cessato ? StatoPersonale.Cessato : StatoPersonale.Scaduto,
                d.HaFoto, Dipendente.SegnapostoPer(d.Genere, d.Pronomi))
            {
                Profilo = !string.IsNullOrWhiteSpace(d.Posizione) ? d.Posizione : d.InForza ? d.MansioneA : d.MansioneU,
                Team = d.TeamId is { } t ? team.GetValueOrDefault(t) : null,
                Ral = d.InForza ? d.RalA : d.RalU,
                Mensilita = d.InForza ? d.MensA : d.MensU,
                Scadenza = d.InForza ? d.FineA : d.FineU,
                ContrattoId = d.InForza ? d.IdA : d.IdU,
                HaDocumentoContratto = d.InForza ? d.DocA : d.DocU,
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

    /// <summary>La modifica è la stessa scheda della visualizzazione, con i campi aperti.</summary>
    public IActionResult Modifica(Guid id, string? sezione = null) =>
        RedirectToAction(nameof(Dettaglio), new { id, sezione, modifica = true });

    public const long DimensioneMassimaContratto = 10 * 1024 * 1024;

    /// <summary>Carica (o sostituisce) il PDF firmato di un contratto.</summary>
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(DimensioneMassimaContratto + 256 * 1024)]
    public async Task<IActionResult> CaricaContratto(Guid id, Guid contrattoId, IFormFile? documento)
    {
        var d = await Trova(id);
        var c = d?.Contratti.FirstOrDefault(x => x.Id == contrattoId);
        if (c is null) return NotFound();
        string? errore = null;
        if (documento is not { Length: > 0 }) errore = "Scegli il file del contratto.";
        else if (documento.Length > DimensioneMassimaContratto) errore = "Il file supera 10 MB.";
        else
        {
            var testa = new byte[5];
            await using (var st = documento.OpenReadStream()) _ = await st.ReadAtLeastAsync(testa, testa.Length, throwOnEndOfStream: false);
            if (!testa.AsSpan().SequenceEqual("%PDF-"u8)) errore = "Il contratto deve essere un file PDF.";
        }
        if (errore is not null)
        {
            Errore(errore);
            return SchedaContratto(id);
        }
        var chiave = $"contratti/{d!.Id}-{Guid.NewGuid():N}.pdf";
        await using (var st = documento!.OpenReadStream()) await archivio.SalvaAsync(chiave, st);
        var precedente = c.DocumentoChiave;
        c.ImpostaDocumento(chiave, documento.FileName, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync();
        if (precedente is not null) await archivio.EliminaAsync(precedente);
        Conferma("Contratto caricato.");
        return SchedaContratto(id);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Mansione(Guid id, Guid contrattoId, string? mansione)
    {
        var d = await Trova(id);
        var c = d?.Contratti.FirstOrDefault(x => x.Id == contrattoId);
        if (c is null) return NotFound();
        if (Prova(() => c.ImpostaMansione(mansione)))
        {
            await db.SaveChangesAsync();
            Conferma("Mansione aggiornata.");
        }
        else Errore(ModelState[""]!.Errors[0].ErrorMessage);
        return SchedaContratto(id);
    }

/// <summary>Foto salvata subito (dalla scheda Contratto, dove non c'è "Salva").</summary>
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(Immagini.DimensioneMassima + 256 * 1024)]
    public async Task<IActionResult> CaricaFoto(Guid id, IFormFile? immagine, string? sezione)
    {
        var d = await Trova(id);
        if (d is null) return NotFound();
        if (immagine is not { Length: > 0 }) Errore("Scegli un'immagine.");
        else if (await foto.SalvaAsync(d, immagine) is { } errore) Errore(errore);
        else
        {
            await db.SaveChangesAsync();
            Conferma("Foto aggiornata.");
        }
        return RedirectToAction(nameof(Dettaglio), new { id, sezione, modifica = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TogliFoto(Guid id, string? sezione)
    {
        var d = await Trova(id);
        if (d is null) return NotFound();
        await foto.RimuoviAsync(d);
        await db.SaveChangesAsync();
        Conferma("Foto rimossa.");
        return RedirectToAction(nameof(Dettaglio), new { id, sezione, modifica = true });
    }

    /// <summary>
    /// Dati per le paghe salvati insieme all'anagrafica (stesse regole del percorso guidato). Residenza, IBAN e cellulare
    /// sono quelli della card "Anagrafica e contatti": qui non si ripetono. Il completamento resta al percorso guidato.
    /// </summary>
    private async Task<bool> ApplicaDatiPaghe(Dipendente dip, DipendenteForm form, DatiPagheForm paghe)
    {
        var profilo = await db.ProfiliDipendente.FirstOrDefaultAsync(p => p.DipendenteId == dip.Id) ?? new ProfiloDipendente(dip.Id);
        var (chi, adesso) = (utente.Email, clock.GetUtcNow().UtcDateTime);
        var (a, r, pg, f, al) = (paghe.Anagrafica, paghe.Residenza, paghe.Pagamento, paghe.Famiglia, paghe.Altri);
        (a.Telefono, r.Via, r.Cap, r.Comune, r.Provincia, pg.Iban) = (form.Telefono, form.Indirizzo, form.Cap, form.Comune, form.Provincia, form.Iban);
        foreach (var k in new[] { "paghe.Residenza.Via", "paghe.Residenza.Cap", "paghe.Residenza.Comune", "paghe.Residenza.Provincia", "paghe.Pagamento.Iban" })
            ModelState.Remove(k);
        foreach (var (campo, valore, msg) in new[] { (nameof(form.Indirizzo), form.Indirizzo, "Per le paghe serve l'indirizzo di residenza."),
                     (nameof(form.Iban), form.Iban, "Per le paghe serve l'IBAN.") })
            if (string.IsNullOrWhiteSpace(valore)) ModelState.AddModelError(campo, msg);

        if (r.DomicilioDiverso)
            foreach (var (campo, valore, msg) in new[] { ("DomicilioVia", r.DomicilioVia, "Inserisci l'indirizzo."), ("DomicilioCap", r.DomicilioCap, "Inserisci il CAP."),
                         ("DomicilioComune", r.DomicilioComune, "Inserisci il comune."), ("DomicilioProvincia", r.DomicilioProvincia, "Inserisci la provincia.") })
                if (string.IsNullOrWhiteSpace(valore)) ModelState.AddModelError("paghe.Residenza." + campo, msg);
        if (al.TipoDocumento is not null && !AltriDatiForm.Documenti.Contains(al.TipoDocumento))
            ModelState.AddModelError("paghe.Altri.TipoDocumento", "Scegli una voce dell'elenco.");
        if (al.ScadenzaDocumento is { } s && s < Oggi) ModelState.AddModelError("paghe.Altri.ScadenzaDocumento", "Il documento è scaduto: indica quello valido.");
        var familiari = new List<DatiFamiliare>();
        for (var i = 0; i < f.Familiari.Count; i++)
        {
            var x = f.Familiari[i];
            if (x.Vuoto) continue;
            var pre = $"paghe.Famiglia.Familiari[{i}].";
            if (string.IsNullOrWhiteSpace(x.Nome) || string.IsNullOrWhiteSpace(x.Cognome)) ModelState.AddModelError(pre + "Nome", "Nome e cognome del familiare.");
            if (x.DataNascita is null) ModelState.AddModelError(pre + "DataNascita", "Data di nascita del familiare.");
            CodiceFiscale? cfFam = null;
            try { cfFam = CodiceFiscale.Parse(x.CodiceFiscale ?? ""); }
            catch (DomainException ex) { ModelState.AddModelError(pre + "CodiceFiscale", ex.Message); }
            if (cfFam is not null && x.DataNascita is { } nascita && !string.IsNullOrWhiteSpace(x.Nome) && !string.IsNullOrWhiteSpace(x.Cognome))
                familiari.Add(new DatiFamiliare(x.Relazione, x.Nome, x.Cognome, cfFam, nascita, x.PercentualeCarico, x.Disabile));
        }
        Iban? iban = null;
        var ok = ModelState.IsValid
                 && Prova(() => profilo.ImpostaAnagrafica(new DatiAnagrafici(a.ComuneNascita!, a.ProvinciaNascita!, a.Cittadinanza!, a.Telefono, a.EmailPersonale), chi, adesso), "paghe.Anagrafica.ComuneNascita")
                 && Prova(() => profilo.ImpostaResidenza(new CL.Domain.Aziende.Indirizzo(r.Via!, r.Cap ?? "", r.Comune ?? "", r.Provincia ?? ""),
                     r.DomicilioDiverso ? new CL.Domain.Aziende.Indirizzo(r.DomicilioVia!, r.DomicilioCap!, r.DomicilioComune!, r.DomicilioProvincia!) : null, chi, adesso), "paghe.Residenza.DomicilioVia")
                 && Prova(() => iban = Iban.Parse(pg.Iban!), nameof(form.Iban))
                 && Prova(() => profilo.ImpostaPagamento(iban!, pg.IntestatarioConto, pg.Tfr, pg.FondoPensione, chi, adesso), "paghe.Pagamento.FondoPensione")
                 && Prova(() => profilo.ImpostaFamiglia(f.RichiedeDetrazioni, familiari, chi, adesso), "paghe.Famiglia.RichiedeDetrazioni")
                 && Prova(() => profilo.ImpostaAltriDati(new AltriDati(al.TitoloStudio, al.TipoDocumento!, al.NumeroDocumento!, al.ScadenzaDocumento!.Value,
                     al.ScadenzaPermessoSoggiorno), chi, adesso), "paghe.Altri.NumeroDocumento");
        if (ok && db.Entry(profilo).State == EntityState.Detached) db.ProfiliDipendente.Add(profilo);
        return ok;
    }

    /// <summary>Correzione delle condizioni del contratto dalla scheda (azienda o consulente).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ModificaContratto(Guid id, [Bind(Prefix = "contratto")] ContrattoForm form)
    {
        var d = await Trova(id);
        if (d is null || form.ContrattoId is not { } cid || d.Contratti.All(c => c.Id != cid)) return NotFound();
        if (form.Tipo.IsSubordinato())
        {
            if (string.IsNullOrWhiteSpace(form.Ccnl)) ModelState.AddModelError("", "Inserisci il CCNL.");
            if (string.IsNullOrWhiteSpace(form.Livello)) ModelState.AddModelError("", "Inserisci il livello.");
        }
        if (ModelState.IsValid)
            Prova(() =>
            {
                d.ModificaContratto(cid, form.Tipo, form.Ccnl ?? "", form.Livello ?? "", form.DataInizio!.Value, form.DataFine,
                    form.RetribuzioneLordaAnnua!.Value, form.MensilitaAnnue, form.OreSettimanaliFullTime, form.PercentualePartTime,
                    form.GiorniLavorativiSettimana, form.OreFerieAnnue, form.OreRolAnnue, form.Mansione,
                    form.Modalita, form.GiorniInUfficio);
                d.Contratti.First(c => c.Id == cid).ImpostaSmartWorking(form.GiorniSmartWorking, form.PeriodoSmartWorking);
            });
        if (!ModelState.IsValid)
        {
            Errore(string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Distinct()));
            return SchedaContratto(id);
        }
        await db.SaveChangesAsync();
        Conferma("Contratto aggiornato.");
        return RedirectToAction(nameof(Dettaglio), new { id, sezione = "contratto" });
    }

    public async Task<IActionResult> DocumentoContratto(Guid id, Guid contrattoId)
    {
        var d = await Trova(id);
        var c = d?.Contratti.FirstOrDefault(x => x.Id == contrattoId);
        if (c?.DocumentoChiave is null || await archivio.ApriAsync(c.DocumentoChiave) is not { } file) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file, "application/pdf", c.DocumentoNome ?? "contratto.pdf");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RimuoviContratto(Guid id, Guid contrattoId)
    {
        var d = await Trova(id);
        var c = d?.Contratti.FirstOrDefault(x => x.Id == contrattoId);
        if (c is null) return NotFound();
        if (c.DocumentoChiave is { } chiave)
        {
            c.RimuoviDocumento();
            await db.SaveChangesAsync();
            await archivio.EliminaAsync(chiave);
            Conferma("Documento del contratto rimosso.");
        }
        return SchedaContratto(id);
    }

    private RedirectToActionResult SchedaContratto(Guid id) => RedirectToAction(nameof(Dettaglio), new { id, sezione = "contratto", modifica = true });

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(Immagini.DimensioneMassima + 256 * 1024)]
    public async Task<IActionResult> Salva(DipendenteForm form, [Bind(Prefix = "paghe")] DatiPagheForm paghe, bool salvaPaghe = false)
    {
        if (utente.IsAzienda) form.AziendaId = utente.AziendaId; // il referente non può scegliere un'altra azienda
        // I dati per le paghe si salvano solo se la loro parte è stata aperta o toccata; i loro controlli si fanno dopo.
        salvaPaghe &= form.Id is not null;
        if (salvaPaghe) ViewBag.DatiPagheForm = paghe;
        if (ModelState.Any(e => !e.Key.StartsWith("paghe.", StringComparison.Ordinal) && e.Value!.Errors.Count > 0)) return await MostraForm(form);
        var erroriPaghe = ModelState.Where(e => e.Key.StartsWith("paghe.", StringComparison.Ordinal)).ToList();
        foreach (var e in erroriPaghe) ModelState.Remove(e.Key);
        if (salvaPaghe) foreach (var e in erroriPaghe) foreach (var err in e.Value!.Errors) ModelState.AddModelError(e.Key, err.ErrorMessage);
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
        dip.AggiornaRecapiti(form.Email, form.Telefono);
        var residenza = new[] { form.Indirizzo, form.Cap, form.Comune, form.Provincia }.All(string.IsNullOrWhiteSpace) ? null
            : new CL.Domain.Aziende.Indirizzo(form.Indirizzo ?? "", form.Cap ?? "", form.Comune ?? "", form.Provincia ?? "");
        if (!Prova(() => dip.ImpostaResidenza(residenza), nameof(form.Indirizzo))) return await MostraForm(form);
        dip.ImpostaIban(iban);
        dip.ImpostaMatricola(form.Matricola);
        dip.ImpostaIdentita(form.Genere, form.Pronomi);
        var team = form.TeamId is { } tid ? await db.Team.FirstOrDefaultAsync(t => t.Id == tid && t.AziendaId == dip.AziendaId) : null;
        var resp = form.ResponsabileId is { } rid ? await db.Dipendenti.FirstOrDefaultAsync(x => x.Id == rid && x.AziendaId == dip.AziendaId) : null;
        if (!Prova(() => dip.ImpostaOrganizzazione(team, form.Posizione, resp), nameof(form.ResponsabileId))) return await MostraForm(form);
        if (form.Sede is SedeLavoro.SedeLegale or SedeLavoro.SedeOperativa && !(await SediAzienda(dip.AziendaId)).ContainsKey(form.Sede.Value))
        {
            ModelState.AddModelError(nameof(form.Sede), "Questa sede non è tra quelle dell'azienda.");
            return await MostraForm(form);
        }
        dip.ImpostaSede(form.Sede);
        if (form.Foto is { Length: > 0 } file && await foto.SalvaAsync(dip, file) is { } erroreFoto)
        {
            ModelState.AddModelError(nameof(form.Foto), erroreFoto);
            return await MostraForm(form);
        }
        if (form.RimuoviFoto && form.Foto is null) await foto.RimuoviAsync(dip);
        if (salvaPaghe && !await ApplicaDatiPaghe(dip, form, paghe)) return await MostraForm(form);
        await db.SaveChangesAsync();

        if (form.Id is null)
        {
            Conferma($"{dip.NomeCompleto} aggiunto. Ora registra il contratto di assunzione.");
            return RedirectToAction(nameof(Assumi), new { id = dip.Id });
        }
        Conferma(salvaPaghe ? "Modifiche salvate, anche nei dati per le paghe." : "Modifiche salvate.");
        return RedirectToAction(nameof(Dettaglio), new { id = dip.Id });
    }

/// <summary>
    /// Scheda della persona: una sola pagina per vedere e modificare. Con <paramref name="modifica"/> i campi sono aperti
    /// (matita nell'elenco o "Modifica dati" nella scheda); le schede sono anagrafica, contratto, ferie e permessi, buste paga.
    /// </summary>
    public async Task<IActionResult> Dettaglio(Guid id, string? sezione = null, bool modifica = false)
    {
        var d = await utente.Filtra(db.Dipendenti.AsNoTracking()).FirstOrDefaultAsync(x => x.Id == id);
        if (d is null) return NotFound();
        return await MostraScheda(d, DipendenteForm.Da(d), modifica, sezione);
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
                form.GiorniLavorativiSettimana, form.OreFerieAnnue, form.OreRolAnnue, form.Mansione,
                form.Modalita, form.GiorniInUfficio).ImpostaSmartWorking(form.GiorniSmartWorking, form.PeriodoSmartWorking)))
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
            return RedirectToAction(nameof(Dettaglio), new { id, sezione = "contratto" });
        }
        if (Prova(() => d.Cessa(data)))
        {
            await db.SaveChangesAsync();
            Conferma($"Cessazione registrata al {data:dd/MM/yyyy}.");
        }
        else Errore(string.Join(" ", ModelState[""]!.Errors.Select(e => e.ErrorMessage)));
        return RedirectToAction(nameof(Dettaglio), new { id, sezione = "contratto" });
    }

    /// <summary>
    /// Foto del dipendente: la vedono il consulente, i referenti della sua azienda e il dipendente stesso.
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

/// <summary>Nuova persona o salvataggio non riuscito: la scheda con i campi aperti e gli errori.</summary>
    private async Task<IActionResult> MostraForm(DipendenteForm form)
    {
        var d = form.Id is { } id ? await utente.Filtra(db.Dipendenti.AsNoTracking()).FirstOrDefaultAsync(x => x.Id == id) : null;
        return await MostraScheda(d, form, true, "anagrafica");
    }

    private async Task<IActionResult> MostraScheda(Dipendente? d, DipendenteForm form, bool modifica, string? sezione)
    {
        var oggi = Oggi;
        ViewBag.Oggi = oggi;
        ViewBag.Modifica = modifica || d is null;
        ViewBag.Sezione = d is not null && sezione is "contratto" or "ferie" or "buste" ? sezione : "anagrafica";
        ViewBag.Aziende = utente.IsConsulente ? await ElencoAziende(form.AziendaId) : null;
        ViewBag.NomeAzienda = form.AziendaId is { } az
            ? await db.Aziende.Where(a => a.Id == az).Select(a => a.RagioneSociale).FirstOrDefaultAsync()
            : null;
        ViewBag.Dipendente = d;
        ViewBag.Sedi = form.AziendaId is { } azs ? await SediAzienda(azs) : new Dictionary<SedeLavoro, string> { [SedeLavoro.SmartWorking] = "Smart working" };
        ViewBag.TeamAzienda = form.AziendaId is { } azt
            ? await db.Team.AsNoTracking().Where(t => t.AziendaId == azt).OrderBy(t => t.Nome)
                .Select(t => new SelectListItem(t.Nome, t.Id.ToString())).ToListAsync()
            : new List<SelectListItem>();
        ViewBag.Colleghi = form.AziendaId is { } azc
            ? await db.Dipendenti.AsNoTracking().IgnoreAutoIncludes().Where(x => x.AziendaId == azc && x.Id != form.Id)
                .OrderBy(x => x.Cognome).ThenBy(x => x.Nome).Select(x => new SelectListItem(x.Cognome + " " + x.Nome, x.Id.ToString())).ToListAsync()
            : new List<SelectListItem>();
        ViewBag.Contratti = d?.Contratti.OrderByDescending(c => c.DataInizio).ToList() ?? new List<Contratto>();
        if (d is null) return View("Scheda", form);

        // Le richieste dell'anno servono sia alla tabella sia al calcolo dei saldi: si caricano una volta sola.
        var richieste = await saldi.RichiesteAnno(d.Id, oggi.Year);
        ViewBag.Accesso = await accessi.Elenco().FirstOrDefaultAsync(u => u.DipendenteId == d.Id);
        var approvatore = await organigramma.ApprovatoreAsync(d.Id);
        ViewBag.Organizzazione = new
        {
            Team = d.TeamId is { } tid ? await db.Team.Where(t => t.Id == tid).Select(t => t.Nome).FirstOrDefaultAsync() : null,
            Approvatore = approvatore is { } aid
                ? await db.Dipendenti.IgnoreAutoIncludes().Where(x => x.Id == aid).Select(x => x.Cognome + " " + x.Nome).FirstOrDefaultAsync()
                : null,
        };
        ViewBag.ProfiloPersonale = await db.ProfiliDipendente.AsNoTracking().FirstOrDefaultAsync(p => p.DipendenteId == d.Id);
        ViewBag.Buste = await db.BustePaga.AsNoTracking().Where(b => b.DipendenteId == d.Id)
            .OrderByDescending(b => b.Anno).ThenByDescending(b => b.Mese).Take(24)
            .Select(b => new BustaRiga
            {
                Id = b.Id, DipendenteId = b.DipendenteId, Dipendente = "", Azienda = "", Anno = b.Anno, Mese = b.Mese,
                Tipo = b.Tipo, Netto = b.Netto, Stato = b.Stato, PubblicataIl = b.PubblicataIl, VisualizzataIl = b.VisualizzataIl,
            })
            .ToListAsync();
        ViewBag.Saldi = await saldi.Calcola(d, oggi.Year, oggi.Month, richieste);
        // Smart working e giorni in ufficio dell'anno: righe tra le richieste e conteggi per mese.
        var (inizioAnno, fineAnno) = (new DateOnly(oggi.Year, 1, 1), new DateOnly(oggi.Year, 12, 31));
        var smart = await db.GiorniSmartWorking.AsNoTracking().Where(g => g.DipendenteId == d.Id && g.Giorno >= inizioAnno && g.Giorno <= fineAnno).ToListAsync();
        var ufficio = (await db.GiorniInUfficio.AsNoTracking().Where(g => g.DipendenteId == d.Id && g.Giorno >= inizioAnno && g.Giorno <= fineAnno)
            .Select(g => g.Giorno).ToListAsync()).ToHashSet();
        ViewBag.Richieste = richieste
            .Select(r => new RichiestaRiga(r.Id, r.DipendenteId, d.NomeCompleto, "", r.Tipo, r.Dal, r.Al, r.AOre, r.Ore,
                r.Stato, r.RichiestaIl, r.Note, r.DecisaDa, r.MotivoRifiuto))
            .Concat(smart.Select(g => new RichiestaRiga(g.Id, d.Id, d.NomeCompleto, "", CL.Domain.Assenze.TipoAssenza.Ferie, g.Giorno, g.Giorno, false, 0,
                g.Stato switch
                {
                    CL.Domain.Presenze.StatoSmartWorking.Approvato => CL.Domain.Assenze.StatoRichiesta.Approvata,
                    CL.Domain.Presenze.StatoSmartWorking.Rifiutato => CL.Domain.Assenze.StatoRichiesta.Rifiutata,
                    _ => CL.Domain.Assenze.StatoRichiesta.InAttesa,
                }, g.RichiestoIl, null, g.DecisoDa, g.MotivoRifiuto) { SmartWorking = true }))
            .OrderBy(r => r.Stato == CL.Domain.Assenze.StatoRichiesta.InAttesa ? 0 : 1).ThenByDescending(r => r.Dal)
            .ToList();
        ViewBag.Presenze = await PresenzeAnno(d, oggi, richieste, smart.Where(g => g.Stato == CL.Domain.Presenze.StatoSmartWorking.Approvato).Select(g => g.Giorno).ToHashSet(), ufficio);
        return View("Scheda", form);
    }

    /// <summary>Sedi censite nel profilo dell'azienda (legale e operativa) più lo smart working.</summary>
    /// <summary>
    /// Giorni lavorati nell'anno fino a oggi, mese per mese: in ufficio o da remoto (stessa regola del calendario: chi lavora
    /// a distanza è da remoto salvo i giorni segnati in ufficio, chi lavora in presenza è in ufficio salvo lo smart working
    /// approvato), più i giorni di assenza di tutta la giornata. Il dettaglio elenca i giorni "fuori dalla regola".
    /// </summary>
    private async Task<List<PresenzeMese>> PresenzeAnno(Dipendente d, DateOnly oggi, IEnumerable<CL.Domain.Assenze.RichiestaAssenza> richieste,
        HashSet<DateOnly> smart, HashSet<DateOnly> ufficio)
    {
        var azienda = await db.Aziende.AsNoTracking().FirstAsync(a => a.Id == d.AziendaId);
        var assenze = richieste.Where(r => r.Stato == CL.Domain.Assenze.StatoRichiesta.Approvata && !r.AOre).ToList();
        var mesi = new List<PresenzeMese>();
        for (var m = 1; m <= oggi.Month; m++)
        {
            int inUfficio = 0, remoto = 0, assente = 0;
            var smartMese = new List<DateOnly>();
            var ufficioMese = new List<DateOnly>();
            var primo = new DateOnly(oggi.Year, m, 1);
            for (var g = primo; g.Month == m && g <= oggi; g = g.AddDays(1))
            {
                var c = d.ContrattoAttivo(g);
                var giorno = g;
                if (c is null || !CL.Domain.Assenze.CalendarioLavorativo.IsLavorativo(g, c.GiorniLavorativiSettimana, azienda.SantoPatronoMese, azienda.SantoPatronoGiorno)) continue;
                if (assenze.Any(r => r.Dal <= giorno && r.Al >= giorno)) { assente++; continue; }
                var distanza = ServizioUfficio.LavoraADistanza(d, g);
                var inSede = ufficio.Contains(g) || (!distanza && !smart.Contains(g));
                if (inSede) inUfficio++; else remoto++;
                if (distanza && ufficio.Contains(g)) ufficioMese.Add(g);
                if (!distanza && smart.Contains(g)) smartMese.Add(g);
            }
            if (inUfficio + remoto + assente > 0)
                mesi.Add(new PresenzeMese(m, inUfficio, remoto, assente, smartMese, ufficioMese));
        }
        return mesi;
    }

    private async Task<Dictionary<SedeLavoro, string>> SediAzienda(Guid aziendaId)
    {
        var p = await db.ProfiliAzienda.AsNoTracking().FirstOrDefaultAsync(x => x.AziendaId == aziendaId);
        var sedi = new Dictionary<SedeLavoro, string>();
        static string Testo(CL.Domain.Aziende.Indirizzo i) => $"{i.Via}, {i.Comune} ({i.Provincia})";
        if (p?.SedeLegale is { } l) sedi[SedeLavoro.SedeLegale] = "Sede legale · " + Testo(l);
        if (p?.SedeOperativa is { } o) sedi[SedeLavoro.SedeOperativa] = "Sede operativa · " + Testo(o);
        sedi[SedeLavoro.SmartWorking] = "Smart working";
        return sedi;
    }

    private async Task<List<SelectListItem>> ElencoAziende(Guid? selezionata) =>
        await db.Aziende.AsNoTracking().Where(a => a.Attiva || a.Id == selezionata).OrderBy(a => a.RagioneSociale)
            .Select(a => new SelectListItem(a.RagioneSociale, a.Id.ToString(), a.Id == selezionata)).ToListAsync();
}
