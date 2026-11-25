using System.ComponentModel.DataAnnotations;
using CL.Domain.Aziende;
using CL.Domain.Common;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Archivio;
using CL.Infrastructure.Identity;
using CL.Infrastructure.Registro;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

public sealed class AziendaForm
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Inserisci la ragione sociale."), StringLength(200)]
    [Display(Name = "Ragione sociale")]
    public string RagioneSociale { get; set; } = "";

    [Required(ErrorMessage = "Inserisci la partita IVA.")]
    [Display(Name = "Partita IVA")]
    public string PartitaIva { get; set; } = "";

    [StringLength(16), Display(Name = "Codice fiscale")]
    [RegularExpression(@"^\s*(\d{11}|[A-Za-z0-9]{16})\s*$", ErrorMessage = "11 cifre (società) o 16 caratteri (ditta individuale).")]
    public string? CodiceFiscale { get; set; }

    [StringLength(300), Display(Name = "Indirizzo della sede")]
    public string? Indirizzo { get; set; }

    [RegularExpression(@"^\d{5}$", ErrorMessage = "CAP di 5 cifre."), Display(Name = "CAP")]
    public string? Cap { get; set; }

    [StringLength(100), Display(Name = "Comune")]
    public string? Comune { get; set; }

    [RegularExpression(@"^[A-Za-z]{2}$", ErrorMessage = "Sigla di 2 lettere (es. MI)."), Display(Name = "Provincia")]
    public string? Provincia { get; set; }

    [EmailAddress(ErrorMessage = "Email non valida."), StringLength(256), Display(Name = "Email")]
    public string? Email { get; set; }

    [Display(Name = "Logo")]
    public IFormFile? Logo { get; set; }

    [Display(Name = "Togli il logo attuale")]
    public bool RimuoviLogo { get; set; }

    public bool HaLogo { get; set; }

    /// <summary>Nuova attività o cambio consulente: decide se l'onboarding chiede i dati del subentro.</summary>
    [Display(Name = "Tipo di attività")]
    public TipoAvvio TipoAvvio { get; set; } = TipoAvvio.NuovaAttivita;

    public static AziendaForm Da(Azienda a) => new()
    {
        Id = a.Id,
        RagioneSociale = a.RagioneSociale,
        PartitaIva = a.PartitaIva.Valore,
        CodiceFiscale = a.CodiceFiscale,
        TipoAvvio = a.TipoAvvio,
        Indirizzo = a.Indirizzo,
        Cap = a.Cap,
        Comune = a.Comune,
        Provincia = a.Provincia,
        Email = a.Email,
        HaLogo = a.LogoChiave is not null,
    };
}

public sealed record AziendaRiga(Guid Id, string RagioneSociale, string PartitaIva, string? Comune, bool Attiva, int Dipendenti, bool ProfiloCompleto)
{
    public StatoAzienda Stato => !Attiva ? StatoAzienda.Disattivata : ProfiloCompleto ? StatoAzienda.Attiva : StatoAzienda.DaCompletare;
}

/// <summary>Stato di un'azienda nell'elenco (pillole e colonna): attiva con profilo completo, da completare, disattivata.</summary>
public enum StatoAzienda { Attiva, DaCompletare, Disattivata }

public static class StatoAziendaExtensions
{
    public static string Chiave(this StatoAzienda s) => s switch
    {
        StatoAzienda.Attiva => "attive", StatoAzienda.DaCompletare => "da-completare", _ => "disattivate",
    };

    public static string Descrizione(this StatoAzienda s, bool plurale = false) => s switch
    {
        StatoAzienda.Attiva => plurale ? "Attive" : "Attiva",
        StatoAzienda.DaCompletare => "Profilo da completare",
        _ => plurale ? "Disattivate" : "Disattivata",
    };

    public static string Colore(this StatoAzienda s) => s switch
    {
        StatoAzienda.Attiva => "#2FBA6C", StatoAzienda.DaCompletare => "#F2B544", _ => "#9AA3AE",
    };
}

/// <summary>
/// Aziende clienti dello studio. Il consulente vede (e trova nella ricerca) solo quelle del suo perimetro;
/// quelle che crea gli vengono assegnate subito. Il super amministratore le vede tutte e può filtrarle per consulente.
/// </summary>
[Authorize(Policy = Policy.SoloConsulente)]
public sealed class AziendeController(AppDbContext db, ServizioConsulentiAziende consulenti, LoghiAziende loghi,
    IArchivioDocumenti archivio, IRicercaPartitaIva registro, UtenteCorrente utente, EliminazioneAzienda eliminazione) : AppController
{
    /// <summary>Tutte le aziende del perimetro, anche disattivate, con le pillole per stato (come nel personale).</summary>
    public async Task<IActionResult> Index(string? q, string? consulenteId, string? stato)
    {
        var query = db.Aziende.AsNoTracking();
        if (utente.IsAmministratore && !string.IsNullOrEmpty(consulenteId))
        {
            var sue = await consulenti.AziendeDiAsync(consulenteId);
            query = query.Where(a => sue.Contains(a.Id));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var testo = q.Trim().ToLower();
            // (string)(object): la partita IVA è un value object salvato come testo; il doppio cast fa confrontare a EF la colonna.
            query = query.Where(a => a.RagioneSociale.ToLower().Contains(testo) || ((string)(object)a.PartitaIva).Contains(testo));
        }

        var tutte = await query
            .OrderBy(a => a.RagioneSociale)
            .Select(a => new AziendaRiga(a.Id, a.RagioneSociale, a.PartitaIva.Valore, a.Comune, a.Attiva,
                db.Dipendenti.Count(d => d.AziendaId == a.Id),
                db.ProfiliAzienda.Any(p => p.AziendaId == a.Id && p.CompletatoIl != null)))
            .ToListAsync();
        var scelto = Enum.GetValues<StatoAzienda>().Cast<StatoAzienda?>().FirstOrDefault(s => s!.Value.Chiave() == stato);
        var righe = scelto is { } st ? tutte.Where(a => a.Stato == st).ToList() : tutte;

        ViewBag.Ricerca = q;
        ViewBag.Stato = scelto;
        ViewBag.Totale = tutte.Count;
        ViewBag.Conteggi = Enum.GetValues<StatoAzienda>().ToDictionary(s => s, s => tutte.Count(a => a.Stato == s));
        ViewBag.Consulenti = await consulenti.ElencoAsync(consulenteId);
        ViewBag.ConsulentiAziende = await consulenti.NomiAsync();
        return View(righe);
    }

    public IActionResult Nuova() => View("Form", new AziendaForm());

    public async Task<IActionResult> Modifica(Guid id)
    {
        var azienda = await db.Aziende.FindAsync(id);
        return azienda is null ? NotFound() : View("Form", AziendaForm.Da(azienda));
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(LoghiAziende.DimensioneMassima + 256 * 1024)]
    public async Task<IActionResult> Salva(AziendaForm form)
    {
        if (form.Provincia is { Length: 2 } p && !Province.IsValida(p))
            ModelState.AddModelError(nameof(form.Provincia), $"La provincia \"{p.ToUpperInvariant()}\" non esiste.");
        if (!ModelState.IsValid) return View("Form", form);

        Azienda? azienda;
        if (form.Id is { } id)
        {
            azienda = await db.Aziende.FindAsync(id);
            if (azienda is null) return NotFound();
        }
        else
        {
            PartitaIva? piva = null;
            if (!Prova(() => piva = PartitaIva.Parse(form.PartitaIva), nameof(form.PartitaIva)))
                return View("Form", form);
            // Il controllo guarda tutte le aziende, anche quelle di altri consulenti (la partita IVA è unica).
            if (await db.Aziende.IgnoreQueryFilters().AnyAsync(a => a.PartitaIva == piva))
            {
                ModelState.AddModelError(nameof(form.PartitaIva), "Esiste già un'azienda con questa partita IVA.");
                return View("Form", form);
            }
            azienda = new Azienda(form.RagioneSociale, piva!);
            db.Aziende.Add(azienda);
            // L'azienda creata da un consulente è sua: gliela si assegna subito.
            if (!utente.IsAmministratore && utente.UserId is { } mio)
                db.AssegnazioniAziende.Add(new AssegnazioneAzienda { AziendaId = azienda.Id, UserId = mio, AssegnataIl = DateTime.UtcNow });
        }

        try
        {
            azienda.AggiornaAnagrafica(form.RagioneSociale, form.CodiceFiscale, form.Indirizzo, form.Cap, form.Comune, form.Provincia, form.Email);
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(CampoDellErrore(ex.Message), ex.Message);
            return View("Form", form);
        }

        azienda.ImpostaTipoAvvio(form.TipoAvvio);

        if (form.Logo is { Length: > 0 } file && await loghi.SalvaAsync(azienda, file) is { } errore)
        {
            ModelState.AddModelError(nameof(form.Logo), errore);
            form.HaLogo = azienda.LogoChiave is not null;
            return View("Form", form);
        }
        if (form.RimuoviLogo && form.Logo is null) await loghi.RimuoviAsync(azienda);

        await db.SaveChangesAsync();
        Conferma(form.Id is null ? $"Azienda {azienda.RagioneSociale} creata." : "Modifiche salvate.");
        return RedirectToAction("Index", "Onboarding", new { aziendaId = azienda.Id });
    }

    /// <summary>Precompilazione del modulo: dati dell'impresa dal registro VIES a partire dalla partita IVA.</summary>
    [HttpGet]
    public async Task<IActionResult> CercaPartitaIva(string? partitaIva, CancellationToken ct)
    {
        PartitaIva piva;
        try { piva = PartitaIva.Parse(partitaIva ?? ""); }
        catch (DomainException ex) { return Json(new { esito = "non-valida", messaggio = ex.Message }); }

        if (await db.Aziende.AnyAsync(a => a.PartitaIva == piva, ct))
            return Json(new { esito = "esistente", messaggio = "Questa azienda è già in archivio." });

        var r = await registro.CercaAsync(piva, ct);
        return r.Esito switch
        {
            EsitoRicerca.Trovata => Json(new
            {
                esito = "trovata", ragioneSociale = r.Dati!.RagioneSociale, indirizzo = r.Dati.Indirizzo, cap = r.Dati.Cap,
                comune = r.Dati.Comune, provincia = r.Dati.Provincia,
            }),
            EsitoRicerca.NonTrovata => Json(new { esito = "non-trovata", messaggio = "Partita IVA non attiva o non presente nel registro: compila i dati a mano." }),
            _ => Json(new { esito = "non-disponibile", messaggio = "Il servizio di ricerca non risponde in questo momento: riprova tra poco o compila a mano." }),
        };
    }

    /// <summary>Logo dell'azienda (anche per referenti e dipendenti della stessa azienda).</summary>
    [AllowAnonymous] // supera il "solo consulente" del controller; il login è verificato qui sotto
    public async Task<IActionResult> Logo(Guid id)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        if (!utente.IsConsulente && utente.AziendaId != id) return NotFound();
        var chiave = await db.Aziende.Where(a => a.Id == id).Select(a => a.LogoChiave).FirstOrDefaultAsync();
        if (chiave is null || await archivio.ApriAsync(chiave) is not { } file) return NotFound();
        Response.Headers.CacheControl = "private, max-age=300";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file, LoghiAziende.TipoMime(chiave));
    }

    /// <summary>
    /// La scheda dell'azienda è la stessa pagina "Profilo azienda" del referente, con in più le azioni dello studio:
    /// questo indirizzo resta valido (link nelle notifiche, negli elenchi) e porta lì.
    /// </summary>
    public IActionResult Dettaglio(Guid id) => RedirectToAction("Index", "Onboarding", new { aziendaId = id });

    /// <summary>
    /// Elimina per sempre un'azienda disattivata con tutti i suoi dati (vedi <see cref="EliminazioneAzienda"/>):
    /// la conferma è la ragione sociale scritta per intero.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Elimina(Guid id, string? conferma)
    {
        var nome = await db.Aziende.Where(a => a.Id == id).Select(a => a.RagioneSociale).FirstOrDefaultAsync();
        if (nome is null) return NotFound();
        if (await eliminazione.EliminaAsync(id, conferma) is { } errore)
        {
            Errore(errore);
            return RedirectToAction("Index", "Onboarding", new { aziendaId = id });
        }
        Conferma($"{nome} è stata eliminata con tutti i suoi dati.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiaStato(Guid id)
    {
        var azienda = await db.Aziende.FindAsync(id);
        if (azienda is null) return NotFound();
        if (azienda.Attiva) azienda.Disattiva(); else azienda.Riattiva();
        await db.SaveChangesAsync();
        Conferma(azienda.Attiva ? "Azienda riattivata." : "Azienda disattivata.");
        return RedirectToAction("Index", "Onboarding", new { aziendaId = id });
    }

    /// <summary>Il campo del form a cui si riferisce un messaggio del dominio (riconosciuto dal testo); "" = errore generale.</summary>
    private static string CampoDellErrore(string messaggio) =>
        messaggio.Contains("CAP") ? nameof(AziendaForm.Cap)
        : messaggio.Contains("provincia") ? nameof(AziendaForm.Provincia)
        : messaggio.Contains("email") ? nameof(AziendaForm.Email)
        : messaggio.Contains("odice fiscale") || messaggio.Contains("arattere") ? nameof(AziendaForm.CodiceFiscale)
        : "";
}
