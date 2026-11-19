using System.ComponentModel.DataAnnotations;
using CL.Domain.Dipendenti;
using CL.Domain.ValueObjects;
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
    };
}

public sealed class ContrattoForm
{
    public Guid DipendenteId { get; set; }
    public string NomeDipendente { get; set; } = "";

    [Display(Name = "Tipo di contratto")]
    public TipoContratto Tipo { get; set; } = TipoContratto.TempoIndeterminato;

    [Required(ErrorMessage = "Inserisci il CCNL."), StringLength(100), Display(Name = "CCNL applicato")]
    public string Ccnl { get; set; } = "";

    [Required(ErrorMessage = "Inserisci il livello."), StringLength(20), Display(Name = "Livello")]
    public string Livello { get; set; } = "";

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

    public static IEnumerable<SelectListItem> Tipi =>
        Enum.GetValues<TipoContratto>().Select(t => new SelectListItem(t.Descrizione(), t.ToString()));
}

public sealed record DipendenteRiga(Guid Id, string NomeCompleto, string CodiceFiscale, string Azienda, string? Livello, bool InForza);

[Authorize(Policy = Policy.GestionePersonale)]
public sealed class DipendentiController(AppDbContext db, UtenteCorrente utente, ServizioSaldi saldi, TimeProvider clock) : AppController
{
    private DateOnly Oggi => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<IActionResult> Index(string? q, Guid? aziendaId, bool tutti = false)
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
        if (!tutti)
            query = query.Where(d => d.Contratti.Any(c => c.DataInizio <= oggi && (c.DataFine == null || c.DataFine >= oggi))
                                     || !d.Contratti.Any());

        var dipendenti = await query.OrderBy(d => d.Cognome).ThenBy(d => d.Nome).ToListAsync();
        var aziende = await db.Aziende.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.RagioneSociale);

        var righe = dipendenti.Select(d => new DipendenteRiga(d.Id, d.NomeCompleto, d.CodiceFiscale.Valore,
            aziende.GetValueOrDefault(d.AziendaId, "—"), d.ContrattoAttivo(oggi)?.Livello, d.InForzaIl(oggi))).ToList();

        ViewBag.Ricerca = q;
        ViewBag.Tutti = tutti;
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

    [HttpPost, ValidateAntiForgeryToken]
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
        var d = await Trova(id);
        if (d is null) return NotFound();
        var oggi = Oggi;
        ViewBag.Azienda = await db.Aziende.AsNoTracking().Where(a => a.Id == d.AziendaId).Select(a => a.RagioneSociale).FirstAsync();
        ViewBag.Oggi = oggi;
        ViewBag.Saldi = await saldi.Calcola(d, oggi.Year, oggi.Month);
        ViewBag.Richieste = (await db.RichiesteAssenza.AsNoTracking()
                .Where(r => r.DipendenteId == d.Id && r.Dal.Year == oggi.Year)
                .ToListAsync())
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
        if (!ModelState.IsValid) return View(form);

        if (!Prova(() => d.Assumi(form.Tipo, form.Ccnl, form.Livello, form.DataInizio!.Value, form.DataFine,
                form.RetribuzioneLordaAnnua!.Value, form.MensilitaAnnue, form.OreSettimanaliFullTime, form.PercentualePartTime,
                form.GiorniLavorativiSettimana, form.OreFerieAnnue, form.OreRolAnnue, form.OreExFestivitaAnnue, form.Mansione)))
            return View(form);

        await db.SaveChangesAsync();
        Conferma("Contratto registrato.");
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
