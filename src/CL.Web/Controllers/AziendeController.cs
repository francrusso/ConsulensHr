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

    [Display(Name = "Logo (PNG, JPEG o WebP, massimo 1 MB)")]
    public IFormFile? Logo { get; set; }

    [Display(Name = "Togli il logo attuale")]
    public bool RimuoviLogo { get; set; }

    public bool HaLogo { get; set; }

    public static AziendaForm Da(Azienda a) => new()
    {
        Id = a.Id,
        RagioneSociale = a.RagioneSociale,
        PartitaIva = a.PartitaIva.Valore,
        CodiceFiscale = a.CodiceFiscale,
        Indirizzo = a.Indirizzo,
        Cap = a.Cap,
        Comune = a.Comune,
        Provincia = a.Provincia,
        Email = a.Email,
        HaLogo = a.LogoChiave is not null,
    };
}

public sealed record AziendaRiga(Guid Id, string RagioneSociale, string PartitaIva, string? Comune, bool Attiva, int Dipendenti, bool ProfiloCompleto);

[Authorize(Policy = Policy.SoloConsulente)]
public sealed class AziendeController(AppDbContext db, ServizioAccessi accessi, LoghiAziende loghi, IArchivioDocumenti archivio,
    IRicercaPartitaIva registro, UtenteCorrente utente) : AppController
{
    public async Task<IActionResult> Index(string? q)
    {
        var query = db.Aziende.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var testo = q.Trim().ToLower();
            query = query.Where(a => a.RagioneSociale.ToLower().Contains(testo) || ((string)(object)a.PartitaIva).Contains(testo));
        }

        var righe = await query
            .OrderBy(a => a.RagioneSociale)
            .Select(a => new AziendaRiga(a.Id, a.RagioneSociale, ((string)(object)a.PartitaIva), a.Comune, a.Attiva,
                db.Dipendenti.Count(d => d.AziendaId == a.Id),
                db.ProfiliAzienda.Any(p => p.AziendaId == a.Id && p.CompletatoIl != null)))
            .ToListAsync();

        ViewBag.Ricerca = q;
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
            if (await db.Aziende.AnyAsync(a => a.PartitaIva == piva))
            {
                ModelState.AddModelError(nameof(form.PartitaIva), "Esiste già un'azienda con questa partita IVA.");
                return View("Form", form);
            }
            azienda = new Azienda(form.RagioneSociale, piva!);
            db.Aziende.Add(azienda);
        }

        // Il messaggio del dominio va sul campo giusto.
        var campo = "";
        if (!Prova(() =>
            {
                try { azienda.AggiornaAnagrafica(form.RagioneSociale, form.CodiceFiscale, form.Indirizzo, form.Cap, form.Comune, form.Provincia, form.Email); }
                catch (DomainException ex)
                {
                    campo = ex.Message.Contains("CAP") ? nameof(form.Cap) : ex.Message.Contains("provincia") ? nameof(form.Provincia)
                        : ex.Message.Contains("email") ? nameof(form.Email) : ex.Message.Contains("odice fiscale") || ex.Message.Contains("arattere") ? nameof(form.CodiceFiscale) : "";
                    throw;
                }
            })) { MoveErrore(campo); return View("Form", form); }

        if (form.Logo is { Length: > 0 } file && await loghi.SalvaAsync(azienda, file) is { } errore)
        {
            ModelState.AddModelError(nameof(form.Logo), errore);
            form.HaLogo = azienda.LogoChiave is not null;
            return View("Form", form);
        }
        if (form.RimuoviLogo && form.Logo is null) await loghi.RimuoviAsync(azienda);

        await db.SaveChangesAsync();
        Conferma(form.Id is null ? $"Azienda {azienda.RagioneSociale} creata." : "Modifiche salvate.");
        return RedirectToAction(nameof(Dettaglio), new { id = azienda.Id });
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

    private void MoveErrore(string campo)
    {
        if (campo.Length == 0 || ModelState[""] is not { Errors.Count: > 0 } generale) return;
        var messaggio = generale.Errors[0].ErrorMessage;
        ModelState.Remove("");
        ModelState.AddModelError(campo, messaggio);
    }

    public async Task<IActionResult> Dettaglio(Guid id)
    {
        var azienda = await db.Aziende.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (azienda is null) return NotFound();
        ViewBag.Dipendenti = await db.Dipendenti.AsNoTracking()
            .Where(d => d.AziendaId == id)
            .OrderBy(d => d.Cognome).ThenBy(d => d.Nome)
            .ToListAsync();
        ViewBag.Profilo = await db.ProfiliAzienda.AsNoTracking().FirstOrDefaultAsync(p => p.AziendaId == id);
        ViewBag.Referenti = await accessi.Elenco().Where(u => u.AziendaId == id && u.Ruolo == Ruoli.Azienda)
            .OrderBy(u => u.Nome).ToListAsync();
        return View(azienda);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiaStato(Guid id)
    {
        var azienda = await db.Aziende.FindAsync(id);
        if (azienda is null) return NotFound();
        if (azienda.Attiva) azienda.Disattiva(); else azienda.Riattiva();
        await db.SaveChangesAsync();
        Conferma(azienda.Attiva ? "Azienda riattivata." : "Azienda disattivata.");
        return RedirectToAction(nameof(Dettaglio), new { id });
    }
}
