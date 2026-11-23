using CL.Domain.Dipendenti;
using CL.Domain.Documenti;
using CL.Infrastructure.Archivio;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace CL.Web.Controllers;

public sealed record RigaCu(Guid DipendenteId, string Persona, string Azienda, Guid? CuId, string? NomeFile, DateTime? CaricataIl, DateTime? VisualizzataIl);

public sealed record RigaContrattoDoc(Guid DipendenteId, string Persona, string Azienda, Guid ContrattoId, TipoContratto Tipo,
    DateOnly Dal, DateOnly? Al, bool HaDocumento, string? NomeFile, DateTime? CaricatoIl, DateOnly? DataCessazione = null)
{
    /// <summary>Stato del contratto, con gli stessi nomi e colori dell'elenco del personale.</summary>
    public StatoPersonale StatoIl(DateOnly oggi) =>
        DataCessazione is { } c && c <= oggi ? StatoPersonale.Cessato
        : Dal > oggi ? StatoPersonale.Firmato
        : Al is { } f && f < oggi ? StatoPersonale.Scaduto
        : StatoPersonale.Attivo;
}

public sealed class ElencoDocumentiViewModel<T>
{
    public required List<T> Righe { get; init; }
    public int Anno { get; init; }
    public List<int> Anni { get; init; } = [];
    public string? Ricerca { get; init; }
    public Guid? AziendaId { get; init; }
    public List<SelectListItem>? Aziende { get; init; }
}

/// <summary>
/// Documenti del personale: CU per anno e contratti firmati (le buste paga hanno la loro pagina).
/// Consulente e azienda li caricano e li consultano; il dipendente vede solo i propri.
/// </summary>
[Authorize]
public sealed class DocumentiController(AppDbContext db, UtenteCorrente utente, IArchivioDocumenti archivio, TimeProvider clock) : AppController
{
    public const long DimensioneMassima = 10 * 1024 * 1024;
    private int AnnoCorrente => clock.GetLocalNow().Year;

    public IActionResult Index() => RedirectToAction("Index", "BustePaga");

    /// <summary>CU dell'anno d'imposta: una riga per persona, con la CU caricata o da caricare.</summary>
    public async Task<IActionResult> Cu(int? anno, Guid? aziendaId, string? q)
    {
        var a = anno ?? AnnoCorrente - 1;
        var persone = Persone(aziendaId, q);
        // Il dipendente vede tutte le sue CU, di ogni anno.
        if (utente.IsDipendente)
        {
            var mie = await db.CertificazioniUniche.AsNoTracking().Where(c => c.DipendenteId == utente.DipendenteId)
                .OrderByDescending(c => c.AnnoImposta).ToListAsync();
            ViewBag.Mie = mie;
        }
        var righe = await (
            from d in persone
            join az in db.Aziende.AsNoTracking() on d.AziendaId equals az.Id
            let cu = db.CertificazioniUniche.FirstOrDefault(c => c.DipendenteId == d.Id && c.AnnoImposta == a)
            orderby d.Cognome, d.Nome
            select new RigaCu(d.Id, d.Cognome + " " + d.Nome, az.RagioneSociale, cu == null ? null : cu.Id, cu == null ? null : cu.NomeFile,
                cu == null ? null : cu.CaricataIl, cu == null ? null : cu.VisualizzataIl)).ToListAsync();
        return View(new ElencoDocumentiViewModel<RigaCu>
        {
            Righe = righe, Anno = a, Anni = Enumerable.Range(AnnoCorrente - 6, 7).Reverse().ToList(), Ricerca = q, AziendaId = aziendaId,
            Aziende = await ElencoAziende(aziendaId),
        });
    }

    /// <summary>Carica (o sostituisce) la CU di una persona per un anno d'imposta.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.GestionePersonale), RequestSizeLimit(DimensioneMassima + 256 * 1024)]
    public async Task<IActionResult> CaricaCu(Guid dipendenteId, int anno, IFormFile? documento, string? ritorno)
    {
        var d = await utente.Filtra(db.Dipendenti.IgnoreAutoIncludes()).FirstOrDefaultAsync(x => x.Id == dipendenteId);
        if (d is null) return NotFound();
        if (await ErrorePdf(documento) is { } errore) Errore(errore);
        else
        {
            var chiave = $"cu/{d.Id}-{Guid.NewGuid():N}.pdf";
            await using (var s = documento!.OpenReadStream()) await archivio.SalvaAsync(chiave, s);
            var adesso = clock.GetUtcNow().UtcDateTime;
            var esistente = await db.CertificazioniUniche.FirstOrDefaultAsync(c => c.DipendenteId == d.Id && c.AnnoImposta == anno);
            string? precedente = null;
            if (esistente is null)
                db.CertificazioniUniche.Add(new CertificazioneUnica(d.AziendaId, d.Id, anno, documento.FileName, chiave, documento.Length, utente.Email, adesso));
            else
            {
                precedente = esistente.ChiaveArchivio;
                esistente.Sostituisci(documento.FileName, chiave, documento.Length, utente.Email, adesso);
            }
            await db.SaveChangesAsync();
            if (precedente is not null) await archivio.EliminaAsync(precedente);
            Conferma($"CU {anno} di {d.NomeCompleto} caricata.");
        }
        return Torna(ritorno, nameof(Cu), new { anno });
    }

    public async Task<IActionResult> ApriCu(Guid id)
    {
        var visibili = utente.Filtra(db.Dipendenti).Select(d => d.Id);
        var cu = await db.CertificazioniUniche.FirstOrDefaultAsync(c => c.Id == id && visibili.Contains(c.DipendenteId));
        if (cu is null || await archivio.ApriAsync(cu.ChiaveArchivio) is not { } file) return NotFound();
        if (utente.IsDipendente && cu.VisualizzataIl is null)
        {
            cu.RegistraVisualizzazione(clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync();
        }
        return Pdf(file, $"CU_{cu.AnnoImposta}_{cu.NomeFile}");
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> EliminaCu(Guid id, string? ritorno)
    {
        var visibili = utente.Filtra(db.Dipendenti).Select(d => d.Id);
        var cu = await db.CertificazioniUniche.FirstOrDefaultAsync(c => c.Id == id && visibili.Contains(c.DipendenteId));
        if (cu is null) return NotFound();
        db.CertificazioniUniche.Remove(cu);
        await db.SaveChangesAsync();
        await archivio.EliminaAsync(cu.ChiaveArchivio);
        Conferma($"CU {cu.AnnoImposta} eliminata.");
        return Torna(ritorno, nameof(Cu), new { anno = cu.AnnoImposta });
    }

    /// <summary>Contratti di tutto il personale, con il PDF firmato o da caricare.</summary>
    public async Task<IActionResult> Contratti(Guid? aziendaId, string? q)
    {
        var righe = await (
            from d in Persone(aziendaId, q)
            join az in db.Aziende.AsNoTracking() on d.AziendaId equals az.Id
            from c in db.Contratti.AsNoTracking().Where(c => c.DipendenteId == d.Id)
            orderby d.Cognome, d.Nome, c.DataInizio descending
            select new RigaContrattoDoc(d.Id, d.Cognome + " " + d.Nome, az.RagioneSociale, c.Id, c.Tipo, c.DataInizio, c.DataFine,
                c.DocumentoChiave != null, c.DocumentoNome, c.DocumentoCaricatoIl, c.DataCessazione)).ToListAsync();
        return View(new ElencoDocumentiViewModel<RigaContrattoDoc> { Righe = righe, Ricerca = q, AziendaId = aziendaId, Aziende = await ElencoAziende(aziendaId) });
    }

    /// <summary>PDF del contratto: anche il dipendente può aprire i propri.</summary>
    public async Task<IActionResult> ApriContratto(Guid id)
    {
        var visibili = utente.Filtra(db.Dipendenti).Select(d => d.Id);
        var c = await db.Contratti.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && visibili.Contains(x.DipendenteId));
        if (c?.DocumentoChiave is null || await archivio.ApriAsync(c.DocumentoChiave) is not { } file) return NotFound();
        return Pdf(file, c.DocumentoNome ?? "contratto.pdf");
    }

    private IQueryable<Dipendente> Persone(Guid? aziendaId, string? q)
    {
        var query = utente.Filtra(db.Dipendenti.AsNoTracking().IgnoreAutoIncludes());
        if (utente.IsConsulente && aziendaId is { } az) query = query.Where(d => d.AziendaId == az);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim().ToLower();
            query = query.Where(d => d.Cognome.ToLower().Contains(t) || d.Nome.ToLower().Contains(t));
        }
        return query;
    }

    private async Task<List<SelectListItem>?> ElencoAziende(Guid? scelta) => !utente.IsConsulente ? null
        : await db.Aziende.AsNoTracking().Where(a => a.Attiva || a.Id == scelta).OrderBy(a => a.RagioneSociale)
            .Select(a => new SelectListItem(a.RagioneSociale, a.Id.ToString(), a.Id == scelta)).ToListAsync();

    private static async Task<string?> ErrorePdf(IFormFile? f)
    {
        if (f is not { Length: > 0 }) return "Scegli il file PDF.";
        if (f.Length > DimensioneMassima) return "Il file supera 10 MB.";
        var testa = new byte[5];
        await using (var s = f.OpenReadStream()) _ = await s.ReadAtLeastAsync(testa, testa.Length, throwOnEndOfStream: false);
        return testa.AsSpan().SequenceEqual("%PDF-"u8) ? null : "Il documento deve essere un file PDF.";
    }

    private FileStreamResult Pdf(Stream file, string nome)
    {
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = nome.Replace(' ', '_') }.ToString();
        return File(file, "application/pdf");
    }

    private IActionResult Torna(string? ritorno, string azione, object valori) =>
        Url.IsLocalUrl(ritorno) ? LocalRedirect(ritorno) : RedirectToAction(azione, valori);
}
