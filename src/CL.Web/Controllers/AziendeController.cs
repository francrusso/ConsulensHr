using System.ComponentModel.DataAnnotations;
using CL.Domain.Aziende;
using CL.Domain.ValueObjects;
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
    public string? CodiceFiscale { get; set; }

    [StringLength(300), Display(Name = "Indirizzo sede")]
    public string? Indirizzo { get; set; }

    [StringLength(100), Display(Name = "Comune")]
    public string? Comune { get; set; }

    [EmailAddress(ErrorMessage = "Email non valida."), StringLength(256), Display(Name = "Email")]
    public string? Email { get; set; }

    [Range(1, 31, ErrorMessage = "Giorno non valido."), Display(Name = "Giorno")]
    public int? PatronoGiorno { get; set; }

    [Range(1, 12, ErrorMessage = "Mese non valido."), Display(Name = "Mese")]
    public int? PatronoMese { get; set; }

    public static AziendaForm Da(Azienda a) => new()
    {
        Id = a.Id,
        RagioneSociale = a.RagioneSociale,
        PartitaIva = a.PartitaIva.Valore,
        CodiceFiscale = a.CodiceFiscale,
        Indirizzo = a.Indirizzo,
        Comune = a.Comune,
        Email = a.Email,
        PatronoGiorno = a.SantoPatronoGiorno,
        PatronoMese = a.SantoPatronoMese,
    };
}

public sealed record AziendaRiga(Guid Id, string RagioneSociale, string PartitaIva, string? Comune, bool Attiva, int Dipendenti);

[Authorize(Policy = Policy.SoloConsulente)]
public sealed class AziendeController(AppDbContext db) : AppController
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
                db.Dipendenti.Count(d => d.AziendaId == a.Id)))
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

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Salva(AziendaForm form)
    {
        if (!ModelState.IsValid) return View("Form", form);
        if ((form.PatronoGiorno is null) != (form.PatronoMese is null))
        {
            ModelState.AddModelError(nameof(form.PatronoGiorno), "Indica sia il giorno sia il mese del santo patrono, oppure nessuno dei due.");
            return View("Form", form);
        }

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

        var ok = Prova(() =>
        {
            azienda.AggiornaAnagrafica(form.RagioneSociale, form.CodiceFiscale?.ToUpperInvariant(), form.Indirizzo, form.Comune, form.Email);
            if (form.PatronoGiorno is { } g && form.PatronoMese is { } m) azienda.ImpostaSantoPatrono(m, g);
            else azienda.RimuoviSantoPatrono();
        }, nameof(form.PatronoGiorno));
        if (!ok) return View("Form", form);

        await db.SaveChangesAsync();
        Conferma(form.Id is null ? $"Azienda {azienda.RagioneSociale} creata." : "Modifiche salvate.");
        return RedirectToAction(nameof(Dettaglio), new { id = azienda.Id });
    }

    public async Task<IActionResult> Dettaglio(Guid id)
    {
        var azienda = await db.Aziende.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (azienda is null) return NotFound();
        ViewBag.Dipendenti = await db.Dipendenti.AsNoTracking()
            .Where(d => d.AziendaId == id)
            .OrderBy(d => d.Cognome).ThenBy(d => d.Nome)
            .ToListAsync();
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
