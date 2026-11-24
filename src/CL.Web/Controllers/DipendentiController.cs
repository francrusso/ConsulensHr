using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using CL.Domain.Assenze;
using CL.Domain.Aziende;
using CL.Domain.Dipendenti;
using CL.Domain.Presenze;
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
    /// <summary>Team di cui la persona è referente (responsabile del team).</summary>
    [Display(Name = "Referente dei team")]
    public List<Guid> TeamReferente { get; set; } = [];
    /// <summary>La persona a cui il consulente chiede ogni mese presenze, variazioni e documenti.</summary>
    [Display(Name = "Referente paghe")]
    public bool ReferentePaghe { get; set; }
    /// <summary>Sede di lavoro come chiave del menù: "SedeLegale", "SmartWorking" o "Unita:{id}" (unità locale).</summary>
    [Display(Name = "Sede di lavoro")]
    public string? Sede { get; set; }

    public static string? ChiaveSede(SedeLavoro? sede, Guid? unita) =>
        sede == SedeLavoro.UnitaLocale && unita is { } u ? $"Unita:{u}" : sede?.ToString();

    [Display(Name = "Genere")]
    public Genere? Genere { get; set; }
    [Display(Name = "Pronomi")]
    public Pronomi? Pronomi { get; set; }

    /// <summary>Solo per una persona nuova: per chi esiste già la foto si salva subito (azione <c>CaricaFoto</c>).</summary>
    [Display(Name = "Foto (PNG, JPEG o WebP, massimo 2 MB)")]
    public IFormFile? Foto { get; set; }
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
        Sede = DipendenteForm.ChiaveSede(d.Sede, d.UnitaLocaleId),
        ReferentePaghe = d.ReferentePaghe,
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

    /// <summary>Valore del parametro <c>stato</c> nell'indirizzo dell'elenco.</summary>
    public static string Chiave(this StatoPersonale s) => s switch
    {
        StatoPersonale.Attivo => "attivi",
        StatoPersonale.Firmato => "firmati",
        StatoPersonale.Scaduto => "scaduti",
        StatoPersonale.Cessato => "cessati",
        _ => "senza-contratto",
    };

    /// <summary>
    /// Stato dal contratto di riferimento: quello in corso, altrimenti il più recente (null = nessun contratto).
    /// Se non ce n'è uno in corso, il più recente dice se è firmato (inizia dopo oggi), cessato o arrivato a scadenza.
    /// </summary>
    public static StatoPersonale StatoDi(Contratto? riferimento, DateOnly oggi) =>
        riferimento is null ? StatoPersonale.SenzaContratto
        : riferimento.AttivoIl(oggi) ? StatoPersonale.Attivo
        : riferimento.DataInizio > oggi ? StatoPersonale.Firmato
        : riferimento.DataCessazione is not null ? StatoPersonale.Cessato
        : StatoPersonale.Scaduto;
}

/// <summary>Riga dell'elenco: <paramref name="Rapporto"/> è il tipo di rapporto, solo per i professionisti esterni.</summary>
public sealed record DipendenteRiga(Guid Id, string NomeCompleto, string CodiceFiscale, string Azienda, string? Rapporto, StatoPersonale Stato,
    bool HaFoto = false, string Segnaposto = "neutro")
{
    public Guid AziendaId { get; init; }
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

/// <summary>
/// Personale (dipendenti e professionisti esterni): elenco, scheda unica da vedere e modificare, contratti, foto.
/// Il consulente vede tutte le aziende, il referente solo la propria: ogni persona si cerca con <see cref="Trova"/>,
/// che applica il perimetro dell'utente (chi è fuori riceve 404).
/// </summary>
[Authorize(Policy = Policy.GestionePersonale)]
public sealed class DipendentiController(AppDbContext db, UtenteCorrente utente, ServizioSaldi saldi, ServizioAccessi accessi,
    ServizioConsulentiAziende consulenti, FotoDipendenti foto, IArchivioDocumenti archivio, TimeProvider clock) : AppController
{
    private DateOnly Oggi => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<IActionResult> Index(string? q, Guid? aziendaId, bool esterni = false, string? stato = null, string? consulenteId = null)
    {
        var oggi = Oggi;
        var query = utente.Filtra(db.Dipendenti.AsNoTracking());
        // Super amministratore: filtro per consulente (le persone delle aziende a lui assegnate).
        if (utente.IsAmministratore && !string.IsNullOrEmpty(consulenteId))
        {
            var sue = await consulenti.AziendeDiAsync(consulenteId);
            query = query.Where(d => sue.Contains(d.AziendaId));
        }
        if (aziendaId is { } az) query = query.Where(d => d.AziendaId == az);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim().ToLower();
            // Il codice fiscale è un value object salvato come testo: il doppio cast fa cercare EF direttamente sulla colonna.
            query = query.Where(d => d.Cognome.ToLower().Contains(t) || d.Nome.ToLower().Contains(t)
                                     || ((string)(object)d.CodiceFiscale).ToLower().Contains(t));
        }

        ViewBag.NumeroEsterni = await query.CountAsync(Esterno(true));
        ViewBag.NumeroDipendenti = await query.CountAsync(Esterno(false));
        query = query.Where(Esterno(esterni));

        // Stati del rapporto contati nel database per la barra sotto i filtri: stessa regola di StatoPersonaleExtensions.StatoDi,
        // usata poi per le righe.
        var attivo = Filtri.ContrattoAttivoIl(oggi);
        var condizioni = new Dictionary<StatoPersonale, Expression<Func<Dipendente, bool>>>
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
        StatoPersonale? filtroStato = null;
        foreach (var (st, cond) in condizioni)
        {
            conteggi[st] = await query.CountAsync(cond);
            if (st.Chiave() == stato) filtroStato = st;
        }
        if (filtroStato is { } fs) query = query.Where(condizioni[fs]);
        ViewBag.Conteggi = conteggi;
        ViewBag.Totale = conteggi.Values.Sum();
        ViewBag.Stato = filtroStato;

        // Solo le colonne mostrate; azienda, tipo del contratto più recente e contratto di riferimento (quello in corso,
        // altrimenti il più recente: in corso prima, poi per data di inizio) li calcola il database.
        var righeDb = await query.OrderBy(d => d.Cognome).ThenBy(d => d.Nome)
            .Select(d => new
            {
                d.Id, d.AziendaId, d.Cognome, d.Nome, d.CodiceFiscale, HaFoto = d.FotoChiave != null, d.Genere, d.Pronomi, d.Posizione, d.TeamId,
                Azienda = db.Aziende.Where(a => a.Id == d.AziendaId).Select(a => a.RagioneSociale).FirstOrDefault(),
                Tipo = d.Contratti.OrderByDescending(c => c.DataInizio).Select(c => (TipoContratto?)c.Tipo).FirstOrDefault(),
                Riferimento = d.Contratti.AsQueryable().OrderByDescending(attivo).ThenByDescending(c => c.DataInizio).FirstOrDefault(),
            })
            .ToListAsync();

        // Team con il colore mostrato (i sotto-team ereditano quello del team madre, più chiaro).
        var idTeam = righeDb.Where(d => d.TeamId != null).Select(d => d.TeamId!.Value).Distinct().ToList();
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
                d.Tipo is { } tipo && !tipo.IsSubordinato() ? tipo.Descrizione() : null,
                StatoPersonaleExtensions.StatoDi(d.Riferimento, oggi), d.HaFoto, Dipendente.SegnapostoPer(d.Genere, d.Pronomi))
            {
                AziendaId = d.AziendaId,
                Profilo = string.IsNullOrWhiteSpace(d.Posizione) ? d.Riferimento?.Mansione : d.Posizione,
                Team = d.TeamId is { } t ? team.GetValueOrDefault(t) : null,
                Ral = d.Riferimento?.RetribuzioneLordaAnnua,
                Mensilita = d.Riferimento?.MensilitaAnnue,
                Scadenza = d.Riferimento?.DataFine,
                ContrattoId = d.Riferimento?.Id,
                HaDocumentoContratto = d.Riferimento?.DocumentoChiave is not null,
            })
            .ToList();

        ViewBag.Oggi = oggi;
        ViewBag.Consulenti = await consulenti.ElencoAsync(consulenteId);
        ViewBag.ConsulentiAziende = await consulenti.NomiAsync();
        ViewBag.Ricerca = q;
        ViewBag.Esterni = esterni;
        ViewBag.AziendaId = aziendaId;
        ViewBag.Aziende = utente.IsConsulente ? await ElencoAziende(aziendaId) : null;
        return View(righe);
    }

    /// <summary>
    /// Professionista esterno: il tipo del contratto più recente non è da lavoro dipendente. Con <paramref name="si"/> falso
    /// la condizione opposta (personale dipendente, compreso chi non ha ancora contratti).
    /// </summary>
    private static Expression<Func<Dipendente, bool>> Esterno(bool si)
    {
        var tipi = TipoContrattoExtensions.Esterni.ToList();
        return d => (d.Contratti.Any() && tipi.Contains(d.Contratti.OrderByDescending(c => c.DataInizio).Select(c => c.Tipo).FirstOrDefault())) == si;
    }

    public async Task<IActionResult> Nuovo(Guid? aziendaId)
    {
        var form = new DipendenteForm { AziendaId = utente.IsAzienda ? utente.AziendaId : aziendaId };
        return await MostraForm(form);
    }

    /// <summary>La modifica è la stessa scheda della visualizzazione, con i campi aperti.</summary>
    public IActionResult Modifica(Guid id, string? sezione = null) =>
        RedirectToAction(nameof(Dettaglio), new { id, sezione, modifica = true });

    /// <summary>
    /// Carica (o sostituisce) il PDF firmato di un contratto, riconosciuto dal contenuto. Il file sta nell'archivio privato
    /// (<c>contratti/</c>) e si apre solo passando dai controlli di accesso; quello sostituito si elimina dopo il salvataggio.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(DocumentiController.DimensioneMassima + 256 * 1024)]
    public async Task<IActionResult> CaricaContratto(Guid id, Guid contrattoId, IFormFile? documento)
    {
        if (await TrovaContratto(id, contrattoId) is not { } c) return NotFound();
        var errore = documento is not { Length: > 0 } ? "Scegli il file del contratto."
            : documento.Length > DocumentiController.DimensioneMassima ? "Il file supera 10 MB."
            : !await DocumentiController.EPdfAsync(documento) ? "Il contratto deve essere un file PDF."
            : null;
        if (errore is not null)
        {
            Errore(errore);
            return SchedaAperta(id);
        }
        var chiave = $"contratti/{id}-{Guid.NewGuid():N}.pdf";
        await using (var st = documento!.OpenReadStream()) await archivio.SalvaAsync(chiave, st);
        var precedente = c.DocumentoChiave;
        c.ImpostaDocumento(chiave, documento.FileName, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync();
        if (precedente is not null) await archivio.EliminaAsync(precedente);
        Conferma("Contratto caricato.");
        return SchedaAperta(id);
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
        return SchedaAperta(id, sezione);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TogliFoto(Guid id, string? sezione)
    {
        var d = await Trova(id);
        if (d is null) return NotFound();
        await foto.RimuoviAsync(d);
        await db.SaveChangesAsync();
        Conferma("Foto rimossa.");
        return SchedaAperta(id, sezione);
    }

    /// <summary>
    /// Dati per le paghe salvati insieme all'anagrafica, con le stesse regole del percorso guidato (il completamento resta
    /// a quello). Residenza, IBAN e cellulare non si ripetono: valgono quelli della card "Anagrafica e contatti".
    /// </summary>
    private async Task<bool> ApplicaDatiPaghe(Dipendente dip, DipendenteForm form, DatiPagheForm paghe)
    {
        var profilo = await db.ProfiliDipendente.FirstOrDefaultAsync(p => p.DipendenteId == dip.Id) ?? new ProfiloDipendente(dip.Id);
        var (chi, adesso) = (utente.Email, clock.GetUtcNow().UtcDateTime);
        var (a, r, pg, f, al) = (paghe.Anagrafica, paghe.Residenza, paghe.Pagamento, paghe.Famiglia, paghe.Altri);
        (a.Telefono, r.Via, r.Cap, r.Comune, r.Provincia, pg.Iban) = (form.Telefono, form.Indirizzo, form.Cap, form.Comune, form.Provincia, form.Iban);
        // Questi campi non sono nel modulo delle paghe: al posto dei loro "obbligatorio" si segnalano i campi dell'anagrafica.
        foreach (var k in new[] { "paghe.Residenza.Via", "paghe.Residenza.Cap", "paghe.Residenza.Comune", "paghe.Residenza.Provincia", "paghe.Pagamento.Iban" })
            ModelState.Remove(k);
        if (string.IsNullOrWhiteSpace(form.Indirizzo)) ModelState.AddModelError(nameof(form.Indirizzo), "Per le paghe serve l'indirizzo di residenza.");
        if (string.IsNullOrWhiteSpace(form.Iban)) ModelState.AddModelError(nameof(form.Iban), "Per le paghe serve l'IBAN.");

        r.ControllaDomicilio(ModelState, "paghe.Residenza.");
        al.ControllaDocumento(ModelState, Oggi, "paghe.Altri.");
        var familiari = f.Leggi(ModelState, "paghe.Famiglia.", "Data di nascita del familiare.");
        Iban? iban = null;
        var ok = ModelState.IsValid
                 && Prova(() => profilo.ImpostaAnagrafica(new DatiAnagrafici(a.ComuneNascita!, a.ProvinciaNascita!, a.Cittadinanza!, a.Telefono, a.EmailPersonale), chi, adesso), "paghe.Anagrafica.ComuneNascita")
                 && Prova(() => profilo.ImpostaResidenza(new Indirizzo(r.Via!, r.Cap ?? "", r.Comune ?? "", r.Provincia ?? ""),
                     r.DomicilioDiverso ? new Indirizzo(r.DomicilioVia!, r.DomicilioCap!, r.DomicilioComune!, r.DomicilioProvincia!) : null, chi, adesso), "paghe.Residenza.DomicilioVia")
                 && Prova(() => iban = Iban.Parse(pg.Iban!), nameof(form.Iban))
                 && Prova(() => profilo.ImpostaPagamento(iban!, pg.IntestatarioConto, pg.Tfr, pg.FondoPensione, chi, adesso), "paghe.Pagamento.FondoPensione")
                 && Prova(() => profilo.ImpostaFamiglia(f.RichiedeDetrazioni, familiari, chi, adesso), "paghe.Famiglia.RichiedeDetrazioni")
                 && Prova(() => profilo.ImpostaAltriDati(new AltriDati(al.TitoloStudio, al.TipoDocumento!, al.NumeroDocumento!, al.ScadenzaDocumento!.Value,
                     al.ScadenzaPermessoSoggiorno), chi, adesso), "paghe.Altri.NumeroDocumento");
        if (ok && db.Entry(profilo).State == EntityState.Detached) db.ProfiliDipendente.Add(profilo);
        return ok;
    }

    /// <summary>
    /// Correzione delle condizioni del contratto dalla scheda (azienda o consulente). Il dominio rifà gli stessi controlli
    /// dell'assunzione; una data di fine cambiata a mano annulla la cessazione registrata.
    /// </summary>
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
            // Il modulo è nella scheda: gli errori tornano come un unico messaggio in cima.
            Errore(string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Distinct()));
            return SchedaAperta(id);
        }
        await db.SaveChangesAsync();
        Conferma("Contratto aggiornato.");
        return RedirectToAction(nameof(Dettaglio), new { id, sezione = "contratto" });
    }

    public async Task<IActionResult> DocumentoContratto(Guid id, Guid contrattoId)
    {
        var c = await TrovaContratto(id, contrattoId);
        if (c?.DocumentoChiave is null || await archivio.ApriAsync(c.DocumentoChiave) is not { } file) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file, "application/pdf", c.DocumentoNome ?? "contratto.pdf");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RimuoviContratto(Guid id, Guid contrattoId)
    {
        if (await TrovaContratto(id, contrattoId) is not { } c) return NotFound();
        if (c.DocumentoChiave is { } chiave)
        {
            c.RimuoviDocumento();
            await db.SaveChangesAsync();
            await archivio.EliminaAsync(chiave);
            Conferma("Documento del contratto rimosso.");
        }
        return SchedaAperta(id);
    }

    /// <summary>Torna alla scheda con i campi aperti, sulla sezione indicata.</summary>
    private RedirectToActionResult SchedaAperta(Guid id, string? sezione = "contratto") =>
        RedirectToAction(nameof(Dettaglio), new { id, sezione, modifica = true });

    /// <summary>
    /// Salvataggio della scheda (persona nuova o esistente). Il referente lavora solo sulla propria azienda; i dati per le
    /// paghe (campi "paghe.") si salvano solo se la loro card è stata aperta o toccata, e mai per una persona nuova.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(Immagini.DimensioneMassima + 256 * 1024)]
    public async Task<IActionResult> Salva(DipendenteForm form, [Bind(Prefix = "paghe")] DatiPagheForm paghe, bool salvaPaghe = false)
    {
        if (utente.IsAzienda) form.AziendaId = utente.AziendaId; // il referente non può scegliere un'altra azienda
        salvaPaghe &= form.Id is not null;
        if (salvaPaghe) ViewBag.DatiPagheForm = paghe;
        if (ModelState.Any(e => !e.Key.StartsWith("paghe.", StringComparison.Ordinal) && e.Value!.Errors.Count > 0)) return await MostraForm(form);
        // Dei campi per le paghe restano solo gli errori, e solo se si salvano: i controlli completi sono in ApplicaDatiPaghe.
        foreach (var (chiave, voce) in ModelState.Where(e => e.Key.StartsWith("paghe.", StringComparison.Ordinal)).ToList())
        {
            ModelState.Remove(chiave);
            if (salvaPaghe) foreach (var err in voce!.Errors) ModelState.AddModelError(chiave, err.ErrorMessage);
        }
        // L'azienda deve esistere ed essere nel perimetro dell'utente (per una persona esistente lo controlla anche Trova).
        if (form.AziendaId is not { } aziendaId || !utente.PuoGestireAzienda(aziendaId) || !await db.Aziende.AnyAsync(a => a.Id == aziendaId))
            return Forbid();

        // "&" e non "&&": codice fiscale e IBAN sbagliati si segnalano insieme.
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
            : new Indirizzo(form.Indirizzo ?? "", form.Cap ?? "", form.Comune ?? "", form.Provincia ?? "");
        if (!Prova(() => dip.ImpostaResidenza(residenza), nameof(form.Indirizzo))) return await MostraForm(form);
        dip.ImpostaIban(iban);
        dip.ImpostaMatricola(form.Matricola);
        dip.ImpostaIdentita(form.Genere, form.Pronomi);
        // Team e responsabile si accettano solo se sono della stessa azienda.
        var team = form.TeamId is { } tid ? await db.Team.FirstOrDefaultAsync(t => t.Id == tid && t.AziendaId == dip.AziendaId) : null;
        var resp = form.ResponsabileId is { } rid ? await db.Dipendenti.FirstOrDefaultAsync(x => x.Id == rid && x.AziendaId == dip.AziendaId) : null;
        if (!Prova(() => dip.ImpostaOrganizzazione(team, form.Posizione, resp), nameof(form.ResponsabileId))) return await MostraForm(form);
        // Referente paghe: uno per azienda, quindi chi lo era prima smette di esserlo.
        if (form.ReferentePaghe && !dip.ReferentePaghe)
            foreach (var altro in await db.Dipendenti.Where(x => x.AziendaId == dip.AziendaId && x.ReferentePaghe && x.Id != dip.Id).ToListAsync())
                altro.ImpostaReferentePaghe(false);
        dip.ImpostaReferentePaghe(form.ReferentePaghe);
        // Referente dei team scelti; dagli altri team dell'azienda, se lo era, non lo è più.
        foreach (var t in await db.Team.Where(t => t.AziendaId == dip.AziendaId).ToListAsync())
        {
            if (form.TeamReferente.Contains(t.Id)) t.ImpostaResponsabile(dip);
            else if (t.ResponsabileId == dip.Id) t.ImpostaResponsabile(null);
        }
        if (form.Sede is { Length: > 0 } sede && !(await SediAzienda(dip.AziendaId)).ContainsKey(sede))
        {
            ModelState.AddModelError(nameof(form.Sede), "Questa sede non è tra quelle dell'azienda.");
            return await MostraForm(form);
        }
        // "Unita:{id}" = unità locale dell'azienda; altrimenti sede legale, smart working (o la vecchia sede operativa).
        if (form.Sede is { } scelta && scelta.StartsWith("Unita:") && Guid.TryParse(scelta[6..], out var unita))
            dip.ImpostaSede(SedeLavoro.UnitaLocale, unita);
        else dip.ImpostaSede(Enum.TryParse<SedeLavoro>(form.Sede, out var sl) ? sl : null);
        if (form.Foto is { Length: > 0 } file && await foto.SalvaAsync(dip, file) is { } erroreFoto)
        {
            ModelState.AddModelError(nameof(form.Foto), erroreFoto);
            return await MostraForm(form);
        }
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
        var form = DipendenteForm.Da(d);
        form.TeamReferente = await db.Team.AsNoTracking().Where(t => t.ResponsabileId == d.Id).Select(t => t.Id).ToListAsync();
        return await MostraScheda(d, form, modifica, sezione);
    }

    public async Task<IActionResult> Assumi(Guid id)
    {
        var d = await Trova(id);
        if (d is null) return NotFound();
        return View(new ContrattoForm { DipendenteId = d.Id, NomeDipendente = d.NomeCompleto, DataInizio = Oggi });
    }

    /// <summary>Nuovo contratto (o incarico, per i professionisti esterni): il dominio rifiuta i periodi sovrapposti.</summary>
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

    /// <summary>Cessazione del rapporto: chiude il contratto attivo a quella data, che diventa anche la sua data di fine.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cessa(Guid id, DateOnly? dataCessazione)
    {
        var d = await Trova(id);
        if (d is null) return NotFound();
        if (dataCessazione is not { } data) Errore("Indica la data di cessazione.");
        else if (ProvaOAvvisa(() => d.Cessa(data)))
        {
            await db.SaveChangesAsync();
            Conferma($"Cessazione registrata al {data:dd/MM/yyyy}.");
        }
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

    /// <summary>La persona, solo se è nel perimetro dell'utente (consulente: tutte; referente: la propria azienda).</summary>
    private Task<Dipendente?> Trova(Guid id) => utente.Filtra(db.Dipendenti).FirstOrDefaultAsync(d => d.Id == id);

    private async Task<Contratto?> TrovaContratto(Guid id, Guid contrattoId) =>
        (await Trova(id))?.Contratti.FirstOrDefault(c => c.Id == contrattoId);

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
        ViewBag.Dipendente = d;
        ViewBag.Contratti = d?.Contratti.OrderByDescending(c => c.DataInizio).ToList() ?? new List<Contratto>();
        // Scelte legate all'azienda: per una persona nuova l'azienda può non essere ancora scelta.
        if (form.AziendaId is { } az)
        {
            ViewBag.NomeAzienda = await db.Aziende.Where(a => a.Id == az).Select(a => a.RagioneSociale).FirstOrDefaultAsync();
            ViewBag.Sedi = await SediAzienda(az);
            ViewBag.TeamAzienda = await db.Team.AsNoTracking().Where(t => t.AziendaId == az).OrderBy(t => t.Nome)
                .Select(t => new SelectListItem(t.Nome, t.Id.ToString())).ToListAsync();
            ViewBag.Colleghi = await db.Dipendenti.AsNoTracking().IgnoreAutoIncludes().Where(x => x.AziendaId == az && x.Id != form.Id)
                .OrderBy(x => x.Cognome).ThenBy(x => x.Nome).Select(x => new SelectListItem(x.Cognome + " " + x.Nome, x.Id.ToString())).ToListAsync();
        }
        else
        {
            ViewBag.Sedi = new Dictionary<string, string> { [nameof(SedeLavoro.SmartWorking)] = "Smart working" };
            ViewBag.TeamAzienda = new List<SelectListItem>();
            ViewBag.Colleghi = new List<SelectListItem>();
        }
        if (d is null) return View("Scheda", form);

        ViewBag.Accesso = await accessi.Elenco().FirstOrDefaultAsync(u => u.DipendenteId == d.Id);
        ViewBag.ProfiloPersonale = await db.ProfiliDipendente.AsNoTracking().FirstOrDefaultAsync(p => p.DipendenteId == d.Id);
        ViewBag.Buste = await db.BustePaga.AsNoTracking().Where(b => b.DipendenteId == d.Id)
            .OrderByDescending(b => b.Anno).ThenByDescending(b => b.Mese).Take(24)
            .Select(b => new BustaRiga
            {
                Id = b.Id, DipendenteId = b.DipendenteId, Dipendente = "", Azienda = "", Anno = b.Anno, Mese = b.Mese,
                Tipo = b.Tipo, Netto = b.Netto, Stato = b.Stato, PubblicataIl = b.PubblicataIl, VisualizzataIl = b.VisualizzataIl,
            })
            .ToListAsync();

        // Le richieste dell'anno servono sia alla tabella sia al calcolo dei saldi: si caricano una volta sola.
        var richieste = await saldi.RichiesteAnno(d.Id, oggi.Year);
        ViewBag.Saldi = await saldi.Calcola(d, oggi.Year, oggi.Month, richieste);
        // Smart working e straordinari dell'anno stanno nella stessa tabella delle richieste; i giorni in ufficio servono alle presenze.
        var (inizioAnno, fineAnno) = Filtri.Anno(oggi.Year);
        var smart = await db.GiorniSmartWorking.AsNoTracking().Where(g => g.DipendenteId == d.Id && g.Giorno >= inizioAnno && g.Giorno <= fineAnno).ToListAsync();
        var straordinari = await db.Straordinari.AsNoTracking().Where(g => g.DipendenteId == d.Id && g.Giorno >= inizioAnno && g.Giorno <= fineAnno).ToListAsync();
        var ufficio = (await db.GiorniInUfficio.AsNoTracking().Where(g => g.DipendenteId == d.Id && g.Giorno >= inizioAnno && g.Giorno <= fineAnno)
            .Select(g => g.Giorno).ToListAsync()).ToHashSet();
        ViewBag.Richieste = richieste
            .Select(r => new RichiestaRiga(r.Id, r.DipendenteId, d.NomeCompleto, "", r.Tipo, r.Dal, r.Al, r.AOre, r.Ore,
                r.Stato, r.RichiestaIl, r.Note, r.DecisaDa, r.MotivoRifiuto) { AnnullamentoInAttesa = r.AnnullamentoInAttesa, MotivoAnnullamento = r.MotivoAnnullamento })
            .Concat(smart.Select(g => new RichiestaRiga(g.Id, d.Id, d.NomeCompleto, "", TipoAssenza.Ferie, g.Giorno, g.Giorno, false, 0,
                g.Stato switch
                {
                    StatoSmartWorking.Approvato => StatoRichiesta.Approvata,
                    StatoSmartWorking.Rifiutato => StatoRichiesta.Rifiutata,
                    _ => StatoRichiesta.InAttesa,
                }, g.RichiestoIl, null, g.DecisoDa, g.MotivoRifiuto) { SmartWorking = true }))
            .Concat(straordinari.Select(g => new RichiestaRiga(g.Id, d.Id, d.NomeCompleto, "", TipoAssenza.Ferie, g.Giorno, g.Giorno, true, g.Ore,
                g.Stato, g.RichiestoIl, g.Note, g.DecisoDa, g.MotivoRifiuto) { Straordinario = true }))
            .OrderBy(r => r.Stato == StatoRichiesta.InAttesa ? 0 : 1).ThenByDescending(r => r.Dal)
            .ToList();
        var smartApprovati = smart.Where(g => g.Stato == StatoSmartWorking.Approvato).Select(g => g.Giorno).ToHashSet();
        ViewBag.Presenze = await PresenzeAnno(d, oggi, richieste, smartApprovati, ufficio);
        return View("Scheda", form);
    }

    /// <summary>
    /// Giorni lavorati nell'anno fino a oggi, mese per mese: in ufficio o da remoto (stessa regola del calendario: chi lavora
    /// a distanza è da remoto salvo i giorni segnati in ufficio, chi lavora in presenza è in ufficio salvo lo smart working
    /// approvato), più i giorni di assenza di tutta la giornata. Il dettaglio elenca i giorni "fuori dalla regola".
    /// </summary>
    private async Task<List<PresenzeMese>> PresenzeAnno(Dipendente d, DateOnly oggi, IEnumerable<RichiestaAssenza> richieste,
        HashSet<DateOnly> smart, HashSet<DateOnly> ufficio)
    {
        var azienda = await db.Aziende.AsNoTracking().FirstAsync(a => a.Id == d.AziendaId);
        var assenze = richieste.Where(r => r.Stato == StatoRichiesta.Approvata && !r.AOre).ToList();
        var mesi = new List<PresenzeMese>();
        for (var m = 1; m <= oggi.Month; m++)
        {
            int inUfficio = 0, remoto = 0, assente = 0;
            var smartMese = new List<DateOnly>();
            var ufficioMese = new List<DateOnly>();
            for (var g = new DateOnly(oggi.Year, m, 1); g.Month == m && g <= oggi; g = g.AddDays(1))
            {
                var c = d.ContrattoAttivo(g);
                if (c is null || !CalendarioLavorativo.IsLavorativo(g, c.GiorniLavorativiSettimana, azienda.SantoPatronoMese, azienda.SantoPatronoGiorno)) continue;
                if (assenze.Any(r => r.Dal <= g && r.Al >= g)) { assente++; continue; }
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

    /// <summary>Sedi censite nel profilo dell'azienda (legale e unità locali) più lo smart working, per chiave del menù.</summary>
    private async Task<Dictionary<string, string>> SediAzienda(Guid aziendaId)
    {
        var p = await db.ProfiliAzienda.AsNoTracking().FirstOrDefaultAsync(x => x.AziendaId == aziendaId);
        var sedi = new Dictionary<string, string>();
        static string Testo(Indirizzo i) => $"{i.Via}, {i.Comune} ({i.Provincia})";
        if (p?.SedeLegale is { } l) sedi[nameof(SedeLavoro.SedeLegale)] = "Sede legale · " + Testo(l);
        foreach (var u in p?.UnitaLocali ?? []) sedi[$"Unita:{u.Id}"] = $"{u.Nome} · {Testo(u.Indirizzo)}";
        if (p?.SedeOperativa is { } o) sedi[nameof(SedeLavoro.SedeOperativa)] = "Sede operativa · " + Testo(o);
        sedi[nameof(SedeLavoro.SmartWorking)] = "Smart working";
        return sedi;
    }

    private async Task<List<SelectListItem>> ElencoAziende(Guid? selezionata) =>
        await db.Aziende.AsNoTracking().Where(a => a.Attiva || a.Id == selezionata).OrderBy(a => a.RagioneSociale)
            .Select(a => new SelectListItem(a.RagioneSociale, a.Id.ToString(), a.Id == selezionata)).ToListAsync();
}
