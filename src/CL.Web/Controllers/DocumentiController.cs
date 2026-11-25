using System.ComponentModel.DataAnnotations;
using CL.Domain.Common;
using CL.Domain.Dipendenti;
using CL.Domain.Documenti;
using CL.Domain.ValueObjects;
using CL.Domain.Notifiche;
using CL.Infrastructure.Archivio;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace CL.Web.Controllers;

/// <summary>Riga delle CU: una per persona, con la CU dell'anno scelto se è stata caricata.</summary>
public sealed record RigaCu(Guid DipendenteId, string Persona, string Azienda, Guid? CuId, DateTime? CaricataIl, DateTime? VisualizzataIl);

public sealed record RigaContrattoDoc(Guid DipendenteId, string Persona, string Azienda, Guid ContrattoId, TipoContratto Tipo,
    DateOnly Dal, DateOnly? Al, bool HaDocumento, DateTime? CaricatoIl, DateOnly? DataCessazione, int Lettere = 0)
{
    /// <summary>Stato del singolo contratto, con gli stessi nomi e colori dell'elenco del personale.</summary>
    public StatoPersonale StatoIl(DateOnly oggi) =>
        DataCessazione is { } c && c <= oggi ? StatoPersonale.Cessato
        : Dal > oggi ? StatoPersonale.Firmato
        : Al is { } f && f < oggi ? StatoPersonale.Scaduto
        : StatoPersonale.Attivo;
}

public sealed record RigaDocumentoPersonale(Guid Id, Guid DipendenteId, string Persona, string Azienda, TipoDocumentoPersonale Tipo,
    string? Descrizione, DateOnly? Scadenza, DateTime CaricatoIl)
{
    public string Titolo => Tipo == TipoDocumentoPersonale.Altro && Descrizione is { } d ? d : Tipo.Descrizione();
    public StatoScadenza StatoIl(DateOnly oggi) => Scadenza switch
    {
        null => StatoScadenza.SenzaScadenza,
        { } s when s < oggi => StatoScadenza.Scaduto,
        { } s when s <= oggi.AddDays(DocumentoPersonale.GiorniPreavviso) => StatoScadenza.InScadenza,
        _ => StatoScadenza.Valido,
    };
}

public sealed record RigaStorico(Guid Id, Guid AziendaId, string Azienda, TipoDocumentoStorico Tipo, int Anno, string? Descrizione, DateTime CaricatoIl)
{
    public string Titolo => Tipo == TipoDocumentoStorico.Altro && Descrizione is { } d ? d : Tipo.Descrizione();
}

public sealed class ElencoDocumentiViewModel<T>
{
    public required List<T> Righe { get; init; }
    public int Anno { get; init; }
    public List<int> Anni { get; init; } = [];
    public string? Ricerca { get; init; }
    /// <summary>Filtro per scadenza dei documenti personali: "scaduti" o "in-scadenza".</summary>
    public string? Stato { get; init; }
    public Guid? AziendaId { get; init; }
    public List<SelectListItem>? Aziende { get; init; }
}

public sealed class NuovoContrattoForm
{
    /// <summary>Falso: contratto per una persona già presente; vero: si crea anche la persona.</summary>
    public bool NuovaPersona { get; set; }
    public Guid? DipendenteId { get; set; }
    public Guid? AziendaId { get; set; }
    public string? Nome { get; set; }
    public string? Cognome { get; set; }
    public string? CodiceFiscale { get; set; }
    public DateOnly? DataNascita { get; set; }
    public string? Email { get; set; }
}

/// <summary>
/// Documenti del personale: CU per anno e contratti firmati (le buste paga hanno la loro pagina).
/// Consulente e azienda li caricano e li consultano; il dipendente vede solo i propri.
/// I PDF stanno nell'archivio privato (cartelle <c>cu/</c> e <c>contratti/</c>) e si aprono solo da qui, dopo il controllo del perimetro.
/// </summary>
[Authorize]
public sealed class DocumentiController(AppDbContext db, UtenteCorrente utente, IArchivioDocumenti archivio, ServizioNotifiche notifiche,
    ArchivioStorico storico, TimeProvider clock) : AppController
{
    public const long DimensioneMassima = 10 * 1024 * 1024;
    private int AnnoCorrente => clock.GetLocalNow().Year;
    /// <summary>Anni d'imposta proposti: i sette più recenti, dal più nuovo.</summary>
    private List<int> Anni => Enumerable.Range(AnnoCorrente - 6, 7).Reverse().ToList();

    public IActionResult Index() => RedirectToAction("Index", "BustePaga");

    /// <summary>
    /// CU dell'anno d'imposta (di default l'anno scorso: la CU si consegna l'anno dopo): una riga per persona, con la CU
    /// caricata o da caricare. Il dipendente vede invece tutte le sue CU, di ogni anno.
    /// </summary>
    public async Task<IActionResult> Cu(int? anno, Guid? aziendaId, string? q)
    {
        var a = anno ?? AnnoCorrente - 1;
        if (utente.IsDipendente)
        {
            ViewBag.Mie = await db.CertificazioniUniche.AsNoTracking().Where(c => c.DipendenteId == utente.DipendenteId)
                .OrderByDescending(c => c.AnnoImposta).ToListAsync();
            return View(new ElencoDocumentiViewModel<RigaCu> { Righe = [], Anno = a });
        }
        var righe = await (
            from d in Persone(aziendaId, q)
            join az in db.Aziende.AsNoTracking() on d.AziendaId equals az.Id
            let cu = db.CertificazioniUniche.FirstOrDefault(c => c.DipendenteId == d.Id && c.AnnoImposta == a)
            orderby d.Cognome, d.Nome
            select new RigaCu(d.Id, d.Cognome + " " + d.Nome, az.RagioneSociale, cu == null ? null : cu.Id,
                cu == null ? null : cu.CaricataIl, cu == null ? null : cu.VisualizzataIl)).ToListAsync();
        return View(new ElencoDocumentiViewModel<RigaCu>
        {
            Righe = righe, Anno = a, Anni = Anni, Ricerca = q, AziendaId = aziendaId, Aziende = await ElencoAziende(aziendaId),
        });
    }

    /// <summary>Carica (o sostituisce) la CU di una persona per un anno d'imposta: il file sostituito si elimina dopo il salvataggio.</summary>
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
            await notifiche.InviaAsync(TipoNotifica.CuDisponibile, await db.PersoneAsync([d.Id]),
                esistente is null ? $"CU dei redditi {anno} disponibile" : $"CU dei redditi {anno} aggiornata",
                esistente is null ? $"È disponibile la tua Certificazione Unica dei redditi {anno}. Puoi scaricarla dal portale."
                                  : $"La tua Certificazione Unica dei redditi {anno} è stata sostituita con una nuova versione.",
                "/Documenti/Cu");
            Conferma($"CU {anno} di {d.NomeCompleto} caricata.");
        }
        return Torna(ritorno, nameof(Cu), new { anno });
    }

    /// <summary>PDF della CU; la prima apertura del dipendente resta registrata (consegna).</summary>
    public async Task<IActionResult> ApriCu(Guid id)
    {
        var cu = await CuVisibile(id);
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
        var cu = await CuVisibile(id);
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
        if (utente.IsDipendente) return RedirectToAction("Index", "MioContratto"); // il dipendente ha "Il mio contratto"
        var righe = await (
            from d in Persone(aziendaId, q)
            join az in db.Aziende.AsNoTracking() on d.AziendaId equals az.Id
            from c in db.Contratti.AsNoTracking().Where(c => c.DipendenteId == d.Id)
            orderby d.Cognome, d.Nome, c.DataInizio descending
            select new RigaContrattoDoc(d.Id, d.Cognome + " " + d.Nome, az.RagioneSociale, c.Id, c.Tipo, c.DataInizio, c.DataFine,
                c.DocumentoChiave != null, c.DocumentoCaricatoIl, c.DataCessazione, c.Lettere.Count)).ToListAsync();
        return View(new ElencoDocumentiViewModel<RigaContrattoDoc> { Righe = righe, Ricerca = q, AziendaId = aziendaId, Aziende = await ElencoAziende(aziendaId) });
    }

    /// <summary>
    /// Documenti personali (identità, tessera sanitaria, permesso di soggiorno, moduli detrazioni/TFR) con la scadenza.
    /// Il dipendente vede e carica i propri; azienda e consulente quelli del personale, filtrabili per scadenza.
    /// </summary>
    public async Task<IActionResult> Personali(Guid? aziendaId, string? q, string? stato)
    {
        var oggi = DateOnly.FromDateTime(clock.GetLocalNow().Date);
        var soglia = oggi.AddDays(DocumentoPersonale.GiorniPreavviso);
        var query =
            from d in Persone(aziendaId, q)
            join az in db.Aziende.AsNoTracking() on d.AziendaId equals az.Id
            join doc in db.DocumentiPersonali.AsNoTracking() on d.Id equals doc.DipendenteId
            select new { d, az, doc };
        if (stato == "scaduti") query = query.Where(x => x.doc.Scadenza != null && x.doc.Scadenza < oggi);
        else if (stato == "in-scadenza") query = query.Where(x => x.doc.Scadenza != null && x.doc.Scadenza <= soglia);
        var righe = (await query.Select(x => new RigaDocumentoPersonale(x.doc.Id, x.d.Id, x.d.Cognome + " " + x.d.Nome, x.az.RagioneSociale,
                x.doc.Tipo, x.doc.Descrizione, x.doc.Scadenza, x.doc.CaricatoIl)).ToListAsync())
            // Prima ciò che è scaduto o sta per scadere, poi per persona.
            .OrderBy(r => r.StatoIl(oggi) switch { StatoScadenza.Scaduto => 0, StatoScadenza.InScadenza => 1, _ => 2 })
            .ThenBy(r => r.Persona).ThenBy(r => r.Tipo).ToList();
        return View(new ElencoDocumentiViewModel<RigaDocumentoPersonale>
        {
            Righe = righe, Ricerca = q, Stato = stato, AziendaId = aziendaId, Aziende = await ElencoAziende(aziendaId),
        });
    }

    /// <summary>Carica un documento personale: il dipendente per sé, azienda e consulente per una persona del personale.</summary>
    public async Task<IActionResult> NuovoPersonale(Guid? dipendenteId)
    {
        ViewBag.Persone = utente.IsDipendente ? null : await SceltePersone();
        ViewBag.DipendenteId = dipendenteId;
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(DimensioneMassima + 256 * 1024)]
    public async Task<IActionResult> CaricaPersonale(Guid? dipendenteId, TipoDocumentoPersonale tipo, string? descrizione, DateOnly? scadenza,
        IFormFile? documento, string? ritorno)
    {
        var id = utente.IsDipendente ? utente.DipendenteId : dipendenteId;
        var d = id is null ? null : await utente.Filtra(db.Dipendenti.IgnoreAutoIncludes()).FirstOrDefaultAsync(x => x.Id == id);
        if (d is null) { Errore("Scegli la persona."); return RedirectToAction(nameof(NuovoPersonale)); }
        var (tipoFile, estensione, errore) = await ControllaFile(documento);
        if (errore is not null) { Errore(errore); return RedirectToAction(nameof(NuovoPersonale), new { dipendenteId = d.Id }); }

        var chiave = $"personali/{d.Id}-{Guid.NewGuid():N}{estensione}";
        DocumentoPersonale doc;
        try
        {
            doc = new DocumentoPersonale(d.AziendaId, d.Id, tipo, descrizione, scadenza, documento!.FileName, tipoFile!, chiave,
                documento.Length, utente.Email, clock.GetUtcNow().UtcDateTime);
        }
        catch (DomainException ex) { Errore(ex.Message); return RedirectToAction(nameof(NuovoPersonale), new { dipendenteId = d.Id }); }
        await using (var s = documento.OpenReadStream()) await archivio.SalvaAsync(chiave, s);
        db.DocumentiPersonali.Add(doc);
        await db.SaveChangesAsync();
        // Caricato dalla persona: lo sanno azienda e studio; caricato da loro: lo sa la persona.
        var scade = doc.Scadenza is { } sc ? $"Scade il {sc:dd/MM/yyyy}." : null;
        if (utente.IsDipendente)
            await notifiche.InviaAsync(TipoNotifica.DocumentoPersonaleCaricato,
                [.. await db.ReferentiAsync(d.AziendaId), .. await db.StudioAsync(d.AziendaId)],
                $"Nuovo documento di {d.NomeCompleto}: {doc.Titolo}",
                NotificheRichieste.Righe($"{d.NomeCompleto} ha caricato {doc.Titolo}.", scade), "/Documenti/Personali");
        else
            await notifiche.InviaAsync(TipoNotifica.DocumentoPersonaleCaricato, await db.PersoneAsync([d.Id]),
                $"Nuovo documento personale: {doc.Titolo}",
                NotificheRichieste.Righe($"Tra i tuoi documenti è stato caricato: {doc.Titolo}.", scade), "/Documenti/Personali");
        Conferma(utente.IsDipendente ? $"{doc.Titolo} caricato." : $"{doc.Titolo} di {d.NomeCompleto} caricato.");
        return Torna(ritorno, nameof(Personali), new { });
    }

    public async Task<IActionResult> ApriPersonale(Guid id)
    {
        var doc = await PersonaleVisibile(id);
        if (doc is null || await archivio.ApriAsync(doc.ChiaveArchivio) is not { } file) return NotFound();
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = doc.NomeFile.Replace(' ', '_') }.ToString();
        return File(file, doc.TipoFile);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminaPersonale(Guid id, string? ritorno)
    {
        var doc = await PersonaleVisibile(id);
        if (doc is null) return NotFound();
        db.DocumentiPersonali.Remove(doc);
        await db.SaveChangesAsync();
        await archivio.EliminaAsync(doc.ChiaveArchivio);
        Conferma($"{doc.Titolo} eliminato.");
        return Torna(ritorno, nameof(Personali), new { });
    }

    /// <summary>Il documento, solo se è di una persona nel perimetro dell'utente (per il dipendente: solo i suoi).</summary>
    private Task<DocumentoPersonale?> PersonaleVisibile(Guid id)
    {
        var visibili = utente.Filtra(db.Dipendenti).Select(d => d.Id);
        return db.DocumentiPersonali.FirstOrDefaultAsync(c => c.Id == id && visibili.Contains(c.DipendenteId));
    }

    /// <summary>PDF, JPEG o PNG fino a 10 MB, riconosciuti dai primi byte del file.</summary>
    private static async Task<(string? Tipo, string? Estensione, string? Errore)> ControllaFile(IFormFile? f)
    {
        if (f is not { Length: > 0 }) return (null, null, "Scegli il file.");
        if (f.Length > DimensioneMassima) return (null, null, "Il file supera 10 MB.");
        var testa = new byte[5];
        await using (var s = f.OpenReadStream()) _ = await s.ReadAtLeastAsync(testa, testa.Length, throwOnEndOfStream: false);
        if (testa.AsSpan().SequenceEqual("%PDF-"u8)) return ("application/pdf", ".pdf", null);
        if (testa[0] == 0xFF && testa[1] == 0xD8 && testa[2] == 0xFF) return ("image/jpeg", ".jpg", null);
        if (testa[0] == 0x89 && testa[1] == 0x50 && testa[2] == 0x4E && testa[3] == 0x47) return ("image/png", ".png", null);
        return (null, null, "Carica un PDF o una foto (JPEG o PNG).");
    }

    /// <summary>
    /// Archivio storico dell'azienda: i documenti degli anni passati portati dal consulente precedente
    /// (CU, 770, UniEmens/DM10, autoliquidazione INAIL, F24, verbali e contenziosi), per anno. Solo per le aziende che
    /// arrivano da un altro consulente (<see cref="ArchivioStorico"/>).
    /// </summary>
    [Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> Storico(Guid? aziendaId, int? anno, string? tipo)
    {
        if (!await storico.VisibileAsync()) return NotFound();
        var query = from s in db.DocumentiStorici.AsNoTracking()
                    join az in db.Aziende.AsNoTracking().Where(ArchivioStorico.Interessata) on s.AziendaId equals az.Id
                    select new { s, az };
        if (utente.IsAzienda) query = query.Where(x => x.s.AziendaId == utente.AziendaId);
        else if (aziendaId is { } scelta) query = query.Where(x => x.s.AziendaId == scelta);
        var anni = await query.Select(x => x.s.Anno).Distinct().OrderByDescending(a => a).ToListAsync();
        if (anno is { } a) query = query.Where(x => x.s.Anno == a);
        if (Enum.TryParse<TipoDocumentoStorico>(tipo, out var t)) query = query.Where(x => x.s.Tipo == t);
        var righe = await query.OrderByDescending(x => x.s.Anno).ThenBy(x => x.az.RagioneSociale).ThenBy(x => x.s.Tipo)
            .Select(x => new RigaStorico(x.s.Id, x.az.Id, x.az.RagioneSociale, x.s.Tipo, x.s.Anno, x.s.Descrizione, x.s.CaricatoIl)).ToListAsync();
        ViewBag.Tipo = tipo;
        return View(new ElencoDocumentiViewModel<RigaStorico>
        {
            Righe = righe, Anno = anno ?? 0, Anni = anni, AziendaId = aziendaId, Aziende = await ElencoAziende(aziendaId, soloStorico: true),
        });
    }

    [Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> NuovoStorico()
    {
        if (!await storico.VisibileAsync()) return NotFound();
        ViewBag.Aziende = await ElencoAziende(null, soloStorico: true);
        ViewBag.AnnoProposto = AnnoCorrente - 1;
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.GestionePersonale), RequestSizeLimit(DimensioneMassima + 256 * 1024)]
    public async Task<IActionResult> CaricaStorico(Guid? aziendaId, TipoDocumentoStorico tipo, int anno, string? descrizione, IFormFile? documento)
    {
        var az = utente.IsAzienda ? utente.AziendaId : aziendaId;
        if (az is not { } id || !await db.Aziende.AnyAsync(a => a.Id == id)) { Errore("Scegli l'azienda."); return RedirectToAction(nameof(NuovoStorico)); }
        if (!await storico.PerAziendaAsync(id))
        {
            Errore("L'archivio storico è solo per le aziende che arrivano da un altro consulente.");
            return RedirectToAction(nameof(NuovoStorico));
        }
        var (tipoFile, estensione, errore) = await ControllaFile(documento);
        if (errore is not null) { Errore(errore); return RedirectToAction(nameof(NuovoStorico)); }
        var chiave = $"storico/{id}-{Guid.NewGuid():N}{estensione}";
        DocumentoStorico doc;
        try
        {
            doc = new DocumentoStorico(id, tipo, anno, descrizione, documento!.FileName, tipoFile!, chiave, documento.Length, utente.Email,
                clock.GetUtcNow().UtcDateTime);
        }
        catch (DomainException ex) { Errore(ex.Message); return RedirectToAction(nameof(NuovoStorico)); }
        await using (var s = documento.OpenReadStream()) await archivio.SalvaAsync(chiave, s);
        db.DocumentiStorici.Add(doc);
        await db.SaveChangesAsync();
        // Lo carica lo studio: lo sanno i referenti; lo carica il referente: lo sa lo studio.
        var nomeAzienda = await db.Aziende.Where(a => a.Id == id).Select(a => a.RagioneSociale).FirstAsync();
        await notifiche.InviaAsync(TipoNotifica.DocumentoStoricoCaricato,
            utente.IsConsulente ? await db.ReferentiAsync(id) : await db.StudioAsync(id),
            $"Archivio storico: {doc.Titolo} {doc.Anno}",
            $"Nell'archivio storico di {nomeAzienda} è stato caricato {doc.Titolo} {doc.Anno}.",
            utente.IsConsulente ? "/Documenti/Storico" : $"/Documenti/Storico?aziendaId={id}");
        Conferma($"{doc.Titolo} {doc.Anno} caricato.");
        return RedirectToAction(nameof(Storico), new { aziendaId = utente.IsConsulente ? id : (Guid?)null });
    }

    [Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> ApriStorico(Guid id)
    {
        var doc = await StoricoVisibile(id);
        if (doc is null || await archivio.ApriAsync(doc.ChiaveArchivio) is not { } file) return NotFound();
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = doc.NomeFile.Replace(' ', '_') }.ToString();
        return File(file, doc.TipoFile);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> EliminaStorico(Guid id, string? ritorno)
    {
        var doc = await StoricoVisibile(id);
        if (doc is null) return NotFound();
        db.DocumentiStorici.Remove(doc);
        await db.SaveChangesAsync();
        await archivio.EliminaAsync(doc.ChiaveArchivio);
        Conferma($"{doc.Titolo} {doc.Anno} eliminato.");
        return Torna(ritorno, nameof(Storico), new { });
    }

    /// <summary>Il documento storico, solo se è di un'azienda dell'utente (il filtro globale vale per il consulente).</summary>
    private Task<DocumentoStorico?> StoricoVisibile(Guid id) =>
        db.DocumentiStorici.FirstOrDefaultAsync(x => x.Id == id && (!utente.IsAzienda || x.AziendaId == utente.AziendaId));

    /// <summary>Lettera di variazione del contratto (proroga, trasformazione…): anche il dipendente può aprire le proprie.</summary>
    public async Task<IActionResult> ApriLettera(Guid id, Guid letteraId)
    {
        var visibili = utente.Filtra(db.Dipendenti).Select(d => d.Id);
        var c = await db.Contratti.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && visibili.Contains(x.DipendenteId));
        var l = c?.Lettere.FirstOrDefault(x => x.Id == letteraId);
        if (l is null || await archivio.ApriAsync(l.Chiave) is not { } file) return NotFound();
        return Pdf(file, l.NomeFile);
    }

    /// <summary>PDF del contratto: anche il dipendente può aprire i propri.</summary>
    public async Task<IActionResult> ApriContratto(Guid id)
    {
        var visibili = utente.Filtra(db.Dipendenti).Select(d => d.Id);
        var c = await db.Contratti.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && visibili.Contains(x.DipendenteId));
        if (c?.DocumentoChiave is null || await archivio.ApriAsync(c.DocumentoChiave) is not { } file) return NotFound();
        return Pdf(file, c.DocumentoNome ?? "contratto.pdf");
    }

    /// <summary>Persone del perimetro per le scelte dei moduli (al consulente si mostra anche l'azienda).</summary>
    private async Task<List<SelectListItem>> SceltePersone() =>
        (await (from d in Persone(null, null)
                join az in db.Aziende.AsNoTracking() on d.AziendaId equals az.Id
                orderby d.Cognome, d.Nome
                select new { d.Id, Nome = d.Cognome + " " + d.Nome, az.RagioneSociale }).ToListAsync())
            .Select(x => new SelectListItem(utente.IsConsulente ? $"{x.Nome} · {x.RagioneSociale}" : x.Nome, x.Id.ToString())).ToList();

    /// <summary>Carica una CU scegliendo la persona (pulsante "Carica CU").</summary>
    [Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> NuovaCu(int? anno)
    {
        ViewBag.Persone = await SceltePersone();
        ViewBag.Anno = anno ?? AnnoCorrente - 1;
        ViewBag.Anni = Anni;
        return View();
    }

    /// <summary>Nuovo contratto: per una persona già presente o per una nuova, creata dalla stessa schermata.</summary>
    [Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> NuovoContratto() => await MostraNuovoContratto(new NuovoContrattoForm());

    /// <summary>Prima si sceglie (o si crea) la persona, poi il contratto si compila nella pagina "Assumi" del personale.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.GestionePersonale)]
    public async Task<IActionResult> NuovoContratto(NuovoContrattoForm form)
    {
        if (!form.NuovaPersona)
        {
            if (form.DipendenteId is not { } id || !await Persone(null, null).AnyAsync(d => d.Id == id))
            {
                ModelState.AddModelError(nameof(form.DipendenteId), "Scegli la persona.");
                return await MostraNuovoContratto(form);
            }
            return RedirectToAction("Assumi", "Dipendenti", new { id });
        }

        var aziendaId = utente.IsAzienda ? utente.AziendaId : form.AziendaId; // il referente crea persone solo nella propria azienda
        if (aziendaId is null || !await db.Aziende.AnyAsync(a => a.Id == aziendaId)) ModelState.AddModelError(nameof(form.AziendaId), "Scegli l'azienda.");
        if (string.IsNullOrWhiteSpace(form.Nome)) ModelState.AddModelError(nameof(form.Nome), "Inserisci il nome.");
        if (string.IsNullOrWhiteSpace(form.Cognome)) ModelState.AddModelError(nameof(form.Cognome), "Inserisci il cognome.");
        if (form.DataNascita is null) ModelState.AddModelError(nameof(form.DataNascita), "Inserisci la data di nascita.");
        if (string.IsNullOrWhiteSpace(form.Email)) ModelState.AddModelError(nameof(form.Email), "Inserisci l'email.");
        else if (!new EmailAddressAttribute().IsValid(form.Email.Trim())) ModelState.AddModelError(nameof(form.Email), "Email non valida.");
        CodiceFiscale? cf = null;
        try { cf = CodiceFiscale.Parse(form.CodiceFiscale); }
        catch (DomainException ex) { ModelState.AddModelError(nameof(form.CodiceFiscale), ex.Message); }
        if (cf is not null && aziendaId is { } az && await db.Dipendenti.AnyAsync(d => d.AziendaId == az && d.CodiceFiscale == cf))
            ModelState.AddModelError(nameof(form.CodiceFiscale), "Questa persona c'è già: sceglila tra le persone presenti.");
        if (!ModelState.IsValid) return await MostraNuovoContratto(form);

        Dipendente? nuova = null;
        if (!Prova(() => nuova = new Dipendente(aziendaId!.Value, form.Nome!.Trim(), form.Cognome!.Trim(), cf!, form.DataNascita!.Value)))
            return await MostraNuovoContratto(form);
        nuova!.AggiornaRecapiti(form.Email!.Trim(), null);
        db.Dipendenti.Add(nuova);
        await db.SaveChangesAsync();
        Conferma($"{nuova.NomeCompleto} aggiunta al personale: ora inserisci il contratto.");
        return RedirectToAction("Assumi", "Dipendenti", new { id = nuova.Id });
    }

    private async Task<IActionResult> MostraNuovoContratto(NuovoContrattoForm form)
    {
        ViewBag.Persone = await SceltePersone();
        ViewBag.Aziende = await ElencoAziende(form.AziendaId);
        return View("NuovoContratto", form);
    }

    /// <summary>Persone nel perimetro dell'utente; il filtro per azienda vale solo per il consulente.</summary>
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

    /// <summary>La CU, solo se è di una persona nel perimetro dell'utente.</summary>
    private Task<CertificazioneUnica?> CuVisibile(Guid id)
    {
        var visibili = utente.Filtra(db.Dipendenti).Select(d => d.Id);
        return db.CertificazioniUniche.FirstOrDefaultAsync(c => c.Id == id && visibili.Contains(c.DipendenteId));
    }

    /// <summary>Elenco delle aziende per il filtro: solo per il consulente.</summary>
    /// <summary>Aziende per il filtro del consulente (null per gli altri ruoli); con <paramref name="soloStorico"/> solo quelle con l'archivio storico.</summary>
    private async Task<List<SelectListItem>?> ElencoAziende(Guid? scelta, bool soloStorico = false) => !utente.IsConsulente ? null
        : await db.Aziende.AsNoTracking().Where(a => a.Attiva || a.Id == scelta)
            .Where(soloStorico ? ArchivioStorico.Interessata : a => true).OrderBy(a => a.RagioneSociale)
            .Select(a => new SelectListItem(a.RagioneSociale, a.Id.ToString(), a.Id == scelta)).ToListAsync();

    private static async Task<string?> ErrorePdf(IFormFile? f)
    {
        if (f is not { Length: > 0 }) return "Scegli il file PDF.";
        if (f.Length > DimensioneMassima) return "Il file supera 10 MB.";
        return await EPdfAsync(f) ? null : "Il documento deve essere un file PDF.";
    }

    /// <summary>Un PDF si riconosce dai primi byte ("%PDF-"), non dall'estensione o dal tipo dichiarati dal browser.</summary>
    internal static async Task<bool> EPdfAsync(IFormFile file)
    {
        var testa = new byte[5];
        await using (var s = file.OpenReadStream()) _ = await s.ReadAtLeastAsync(testa, testa.Length, throwOnEndOfStream: false);
        return testa.AsSpan().SequenceEqual("%PDF-"u8);
    }

    /// <summary>PDF aperto nel browser e mai in cache.</summary>
    private FileStreamResult Pdf(Stream file, string nome)
    {
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = nome.Replace(' ', '_') }.ToString();
        return File(file, "application/pdf");
    }

    /// <summary>Torna alla pagina di partenza (solo indirizzi locali), altrimenti all'azione indicata.</summary>
    private IActionResult Torna(string? ritorno, string azione, object valori) =>
        Url.IsLocalUrl(ritorno) ? LocalRedirect(ritorno) : RedirectToAction(azione, valori);
}
