using CL.Domain.Notifiche;
using CL.Infrastructure.Email;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

public sealed class NotificheViewModel
{
    public required List<Notifica> Notifiche { get; init; }
    public int DaLeggere { get; init; }
    public string? Ricerca { get; init; }
    public CategoriaNotifica? Categoria { get; init; }
    public bool SoloDaLeggere { get; init; }
    public int Pagina { get; init; }
    public int Pagine { get; init; }
    public required List<SelectListItem> Categorie { get; init; }
}

/// <summary>Una tipologia nelle impostazioni, con le scelte dell'utente per email e WhatsApp.</summary>
public sealed record ScelteNotifica(DefinizioneNotifica Definizione, bool Email, bool WhatsApp);

public sealed record GruppoNotifiche(CategoriaNotifica Categoria, List<ScelteNotifica> Tipi);

public sealed class ImpostazioniNotificheViewModel
{
    public required List<GruppoNotifiche> Gruppi { get; init; }
    /// <summary>Server SMTP configurato: senza, per ora arrivano solo le notifiche in piattaforma.</summary>
    public bool EmailConfigurata { get; init; }
    public required StatoWhatsApp WhatsApp { get; init; }
    /// <summary>Numero a cui è appena partito il codice: la pagina chiede di inserirlo.</summary>
    public string? NumeroInVerifica { get; init; }
}

/// <summary>
/// Notifiche dell'utente (tutti i ruoli): elenco, lettura e impostazioni. Le notifiche in piattaforma sono sempre attive;
/// email e WhatsApp si accendono e si spengono per ogni tipologia (salvataggio immediato). WhatsApp richiede di collegare
/// il numero con consenso e codice di verifica.
/// </summary>
public sealed class NotificheController(ServizioNotifiche notifiche, CollegamentoWhatsApp whatsApp, IInvioEmail email) : AppController
{
    private const string ChiaveNumeroInVerifica = "WhatsAppNumero";

    private const int PerPagina = 20;

    public async Task<IActionResult> Index(string? q, CategoriaNotifica? categoria, bool daLeggere = false, int pagina = 1)
    {
        var query = notifiche.Mie().AsNoTracking();
        if (categoria is { } c)
        {
            var tipi = CatalogoNotifiche.Tutte.Where(d => d.Categoria == c).Select(d => d.Tipo).ToList();
            query = query.Where(n => tipi.Contains(n.Tipo));
        }
        if (daLeggere) query = query.Where(n => n.LettaIl == null);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim().ToLower();
            query = query.Where(n => n.Titolo.ToLower().Contains(t) || n.Testo.ToLower().Contains(t));
        }

        var totale = await query.CountAsync();
        var pagine = Math.Max(1, (totale + PerPagina - 1) / PerPagina);
        pagina = Math.Clamp(pagina, 1, pagine);
        var ruoli = await notifiche.RuoliAsync();
        return View(new NotificheViewModel
        {
            Notifiche = await query.OrderByDescending(n => n.CreataIl).Skip((pagina - 1) * PerPagina).Take(PerPagina).ToListAsync(),
            DaLeggere = await notifiche.DaLeggereAsync(),
            Ricerca = q, Categoria = categoria, SoloDaLeggere = daLeggere, Pagina = pagina, Pagine = pagine,
            Categorie = CatalogoNotifiche.Per(ruoli).Select(d => d.Categoria).Distinct()
                .Select(x => new SelectListItem(x.Descrizione(), x.ToString(), x == categoria)).ToList(),
        });
    }

    /// <summary>Apre la pagina dell'evento e segna la notifica come letta.</summary>
    public async Task<IActionResult> Apri(Guid id)
    {
        var n = await notifiche.ApriAsync(id);
        if (n is null) return NotFound();
        return Url.IsLocalUrl(n.Link) ? LocalRedirect(n.Link) : RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SegnaLetta(Guid id, string? ritorno)
    {
        if (await notifiche.ApriAsync(id) is null) return NotFound();
        return Torna(ritorno);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SegnaTutteLette(string? ritorno)
    {
        var n = await notifiche.SegnaTutteLetteAsync();
        if (n > 0) Conferma(n == 1 ? "Notifica segnata come letta." : $"{n} notifiche segnate come lette.");
        return Torna(ritorno);
    }

    /// <summary>Le tipologie che l'utente può ricevere, per sezione, con email e WhatsApp accesi o spenti.</summary>
    public async Task<IActionResult> Impostazioni()
    {
        var scelte = await notifiche.ScelteAsync();
        var gruppi = CatalogoNotifiche.Per(await notifiche.RuoliAsync())
            .GroupBy(d => d.Categoria)
            .Select(g => new GruppoNotifiche(g.Key, g.Select(d => new ScelteNotifica(d, scelte[d.Tipo].Email, scelte[d.Tipo].WhatsApp)).ToList()))
            .ToList();
        var stato = await whatsApp.StatoAsync();
        return View(new ImpostazioniNotificheViewModel
        {
            Gruppi = gruppi, EmailConfigurata = email.Configurato, WhatsApp = stato,
            NumeroInVerifica = stato.Collegato ? null : TempData[ChiaveNumeroInVerifica] as string,
        });
    }

    /// <summary>
    /// Accende o spegne un canale per le tipologie indicate (una, quelle di una sezione o tutte). Chiamata dalla pagina
    /// delle impostazioni a ogni interruttore: risponde senza contenuto. WhatsApp solo con il numero collegato.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Scelta(List<TipoNotifica> tipi, CanaleNotifica canale, bool attiva)
    {
        var ricevibili = CatalogoNotifiche.Per(await notifiche.RuoliAsync()).Select(d => d.Tipo).ToHashSet();
        var scelti = tipi.Where(ricevibili.Contains).Distinct().ToList();
        if (scelti.Count == 0) return BadRequest();
        if (canale == CanaleNotifica.WhatsApp && !(await whatsApp.StatoAsync()).Collegato) return BadRequest();
        await notifiche.ImpostaAsync(scelti, canale, attiva);
        return NoContent();
    }

    /// <summary>Primo passo del collegamento: consenso e numero, a cui parte un codice su WhatsApp.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> WhatsAppCodice(string? numero, bool consenso)
    {
        var (inVerifica, errore) = await whatsApp.InviaCodiceAsync(numero, consenso);
        if (inVerifica is not null) TempData[ChiaveNumeroInVerifica] = inVerifica;
        if (errore is not null) Errore(errore);
        else Conferma($"Ti abbiamo inviato un codice su WhatsApp al {NumeroWhatsApp.Leggibile(inVerifica!)}.");
        return RedirectToAction(nameof(Impostazioni));
    }

    /// <summary>Secondo passo: il codice ricevuto conferma il numero e attiva il canale.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> WhatsAppVerifica(string? numero, string? codice)
    {
        if (await whatsApp.VerificaAsync(numero, codice) is { } errore)
        {
            Errore(errore);
            TempData[ChiaveNumeroInVerifica] = numero;
        }
        else Conferma("WhatsApp collegato: ora scegli quali notifiche ricevere anche su WhatsApp.");
        return RedirectToAction(nameof(Impostazioni));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> WhatsAppScollega()
    {
        await whatsApp.ScollegaAsync();
        Conferma("Numero WhatsApp scollegato: non riceverai più notifiche su WhatsApp.");
        return RedirectToAction(nameof(Impostazioni));
    }

    private IActionResult Torna(string? ritorno) => Url.IsLocalUrl(ritorno) ? LocalRedirect(ritorno) : RedirectToAction(nameof(Index));
}
