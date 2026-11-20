using CL.Domain.BustePaga;
using CL.Infrastructure.Archivio;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace CL.Web.Controllers;

public sealed class BustePagaViewModel
{
    public required List<BustaRiga> Buste { get; init; }
    public int? Anno { get; init; }
    public int? Mese { get; init; }
    public Guid? AziendaId { get; init; }
    public StatoBustaPaga? Stato { get; init; }
    public List<SelectListItem>? Aziende { get; init; }
    public List<int> Anni { get; init; } = [];
}

public sealed class CaricaBusteForm
{
    public Guid? AziendaId { get; set; }
    public int Anno { get; set; }
    public int Mese { get; set; }
    public TipoCedolino Tipo { get; set; } = TipoCedolino.Ordinario;
    public bool Pubblica { get; set; } = true;

    public static IEnumerable<SelectListItem> Tipi =>
        Enum.GetValues<TipoCedolino>().Select(t => new SelectListItem(t.Descrizione(), t.ToString()));
    public static IEnumerable<SelectListItem> Mesi =>
        Enumerable.Range(1, 12).Select(m => new SelectListItem(ServizioBustePaga.NomeMese(m), m.ToString()));
}

/// <summary>
/// Cedolini: li carica e pubblica il consulente; il referente vede quelli della propria azienda;
/// il dipendente vede solo i propri, una volta pubblicati (la prima apertura vale come consegna).
/// </summary>
[Authorize]
public sealed class BustePagaController(
    AppDbContext db, UtenteCorrente utente, ServizioBustePaga servizio, IArchivioDocumenti archivio, TimeProvider clock) : AppController
{
    private const long LimiteRichiesta = 200 * 1024 * 1024; // caricamento in blocco: fino a 200 MB complessivi

    public async Task<IActionResult> Index(int? anno, int? mese, Guid? aziendaId, StatoBustaPaga? stato)
    {
        var cedolini = db.BustePaga.AsNoTracking();
        if (utente.IsDipendente) cedolini = cedolini.Where(b => b.Stato == StatoBustaPaga.Pubblicata);
        else if (stato is { } s) cedolini = cedolini.Where(b => b.Stato == s);
        if (aziendaId is { } az) cedolini = cedolini.Where(b => b.AziendaId == az);
        if (anno is { } a) cedolini = cedolini.Where(b => b.Anno == a);
        if (mese is { } m) cedolini = cedolini.Where(b => b.Mese == m);

        var buste = await Righe(cedolini).OrderByDescending(b => b.Anno).ThenByDescending(b => b.Mese)
            .ThenBy(b => b.Dipendente).Take(1000).ToListAsync();
        var oggi = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        return View(new BustePagaViewModel
        {
            Buste = buste,
            Anno = anno, Mese = mese, AziendaId = aziendaId, Stato = stato,
            Aziende = utente.IsConsulente ? await ElencoAziende(aziendaId) : null,
            Anni = Enumerable.Range(oggi.Year - 5, 6).Reverse().ToList(),
        });
    }

    [Authorize(Policy = Policy.SoloConsulente)]
    public async Task<IActionResult> Carica(Guid? aziendaId)
    {
        var mesePrecedente = DateOnly.FromDateTime(clock.GetLocalNow().DateTime).AddMonths(-1);
        ViewBag.Aziende = await ElencoAziende(aziendaId, soloAttive: true);
        return View(new CaricaBusteForm { AziendaId = aziendaId, Anno = mesePrecedente.Year, Mese = mesePrecedente.Month });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.SoloConsulente)]
    [RequestSizeLimit(LimiteRichiesta), RequestFormLimits(MultipartBodyLengthLimit = LimiteRichiesta)]
    public async Task<IActionResult> Carica(CaricaBusteForm form, List<IFormFile> files)
    {
        ViewBag.Aziende = await ElencoAziende(form.AziendaId, soloAttive: true);
        if (form.AziendaId is not { } aziendaId || !await db.Aziende.AnyAsync(a => a.Id == aziendaId))
            ModelState.AddModelError(nameof(form.AziendaId), "Seleziona l'azienda.");
        if (files.Count == 0) ModelState.AddModelError("files", "Seleziona almeno un PDF.");
        if (!ModelState.IsValid) return View(form);

        var esito = await servizio.CaricaAsync(form.AziendaId!.Value, form.Anno, form.Mese, form.Tipo, Converti(files), form.Pubblica);
        ViewBag.Esito = esito;
        return View(form);
    }

    /// <summary>Un cedolino per un dipendente, dalla sua scheda.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.SoloConsulente)]
    [RequestSizeLimit(ServizioBustePaga.DimensioneMassima + 1024 * 1024)]
    public async Task<IActionResult> CaricaPerDipendente(Guid dipendenteId, int anno, int mese, TipoCedolino tipo,
        decimal? lordo, decimal? netto, bool pubblica, IFormFile? file)
    {
        var ritorno = Url.Action("Dettaglio", "Dipendenti", new { id = dipendenteId, sezione = "buste" })!;
        var dip = await db.Dipendenti.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(d => d.Id == dipendenteId);
        if (dip is null) return NotFound();
        if (file is null)
        {
            Errore("Seleziona il PDF del cedolino.");
            return LocalRedirect(ritorno);
        }

        var esito = await servizio.CaricaAsync(dip.AziendaId, anno, mese, tipo, Converti([file]), pubblica,
            soloDipendente: dip.Id, lordo: lordo ?? 0, netto: netto ?? 0);
        if (esito.Caricati.Count == 1)
            Conferma(pubblica ? "Cedolino caricato e pubblicato." : "Cedolino caricato come bozza.");
        else Errore($"Cedolino non caricato: {esito.Scartati.FirstOrDefault().Motivo}.");
        return LocalRedirect(ritorno);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.SoloConsulente)]
    public async Task<IActionResult> Pubblica(List<Guid> ids, string? ritorno)
    {
        var n = await servizio.PubblicaAsync(ids);
        if (n == 0) Errore("Nessun cedolino in bozza da pubblicare.");
        else Conferma(n == 1 ? "Cedolino pubblicato." : $"{n} cedolini pubblicati: i dipendenti con accesso ricevono un avviso via email.");
        return Torna(ritorno);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.SoloConsulente)]
    public async Task<IActionResult> Ritira(Guid id, string? ritorno)
    {
        var b = await db.BustePaga.FindAsync(id);
        if (b is null) return NotFound();
        if (Prova(b.Ritira))
        {
            await db.SaveChangesAsync();
            Conferma("Cedolino ritirato: il dipendente non lo vede più (resta in archivio).");
        }
        else Errore(ModelState[""]!.Errors[0].ErrorMessage);
        return Torna(ritorno);
    }

    /// <summary>Solo le bozze si possono eliminare (caricamento sbagliato): un cedolino pubblicato si ritira.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.SoloConsulente)]
    public async Task<IActionResult> Elimina(Guid id, string? ritorno)
    {
        var b = await db.BustePaga.FindAsync(id);
        if (b is null) return NotFound();
        if (b.Stato != StatoBustaPaga.Bozza)
        {
            Errore("Si possono eliminare solo i cedolini in bozza: quelli pubblicati si ritirano.");
            return Torna(ritorno);
        }
        db.BustePaga.Remove(b);
        await db.SaveChangesAsync();
        await archivio.EliminaAsync(b.ChiaveArchivio);
        Conferma("Bozza eliminata.");
        return Torna(ritorno);
    }

    /// <summary>Apre il PDF nel browser, dopo il controllo dei permessi. Mai in cache: sono dati retributivi.</summary>
    public async Task<IActionResult> Apri(Guid id)
    {
        var visibili = utente.Filtra(db.Dipendenti).Select(d => d.Id);
        var b = await db.BustePaga.FirstOrDefaultAsync(x => x.Id == id && visibili.Contains(x.DipendenteId));
        if (b is null || (utente.IsDipendente && b.Stato != StatoBustaPaga.Pubblicata)) return NotFound();

        var file = await archivio.ApriAsync(b.ChiaveArchivio);
        if (file is null) return NotFound();

        if (utente.IsDipendente && b.VisualizzataIl is null)
        {
            b.RegistraVisualizzazione(clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync();
        }

        var nome = await db.Dipendenti.Where(d => d.Id == b.DipendenteId).Select(d => d.Cognome + "_" + d.Nome).FirstAsync();
        var nomeFile = $"BustaPaga_{b.Anno}-{b.Mese:00}_{nome}.pdf".Replace(' ', '_');
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = nomeFile }.ToString();
        return File(file, "application/pdf");
    }

    /// <summary>Cedolini nel perimetro dell'utente, con nome del dipendente e dell'azienda.</summary>
    private IQueryable<BustaRiga> Righe(IQueryable<BustaPaga> cedolini) =>
        from b in cedolini
        join d in utente.Filtra(db.Dipendenti.AsNoTracking().IgnoreAutoIncludes()) on b.DipendenteId equals d.Id
        join a in db.Aziende.AsNoTracking() on b.AziendaId equals a.Id
        select new BustaRiga
        {
            Id = b.Id, DipendenteId = b.DipendenteId, Dipendente = d.Cognome + " " + d.Nome, Azienda = a.RagioneSociale,
            Anno = b.Anno, Mese = b.Mese, Tipo = b.Tipo, Netto = b.Netto, Stato = b.Stato,
            PubblicataIl = b.PubblicataIl, VisualizzataIl = b.VisualizzataIl,
        };

    private static List<FileCaricato> Converti(IEnumerable<IFormFile> files) =>
        files.Select(f => new FileCaricato(f.FileName, f.ContentType, f.OpenReadStream, f.Length)).ToList();

    private async Task<List<SelectListItem>> ElencoAziende(Guid? selezionata, bool soloAttive = false) =>
        await db.Aziende.AsNoTracking().Where(a => !soloAttive || a.Attiva || a.Id == selezionata).OrderBy(a => a.RagioneSociale)
            .Select(a => new SelectListItem(a.RagioneSociale, a.Id.ToString(), a.Id == selezionata)).ToListAsync();

    private IActionResult Torna(string? ritorno) =>
        Url.IsLocalUrl(ritorno) ? LocalRedirect(ritorno) : RedirectToAction(nameof(Index));
}
