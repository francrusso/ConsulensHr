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

/// <summary>Una tipologia nelle impostazioni, con la scelta dell'utente per l'email.</summary>
public sealed record ScelteNotifica(DefinizioneNotifica Definizione, bool Email);

public sealed record GruppoNotifiche(CategoriaNotifica Categoria, List<ScelteNotifica> Tipi);

public sealed class ImpostazioniNotificheViewModel
{
    public required List<GruppoNotifiche> Gruppi { get; init; }
    /// <summary>Server SMTP configurato: senza, per ora arrivano solo le notifiche in piattaforma.</summary>
    public bool EmailConfigurata { get; init; }
}

/// <summary>
/// Notifiche dell'utente (tutti i ruoli): elenco, lettura, e impostazioni dell'email per tipologia. Le notifiche in
/// piattaforma sono sempre attive; l'email si accende e si spegne per ogni tipologia (salvataggio immediato).
/// </summary>
public sealed class NotificheController(ServizioNotifiche notifiche, IInvioEmail email) : AppController
{
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

    /// <summary>Le tipologie che l'utente può ricevere, per sezione, con l'email accesa o spenta.</summary>
    public async Task<IActionResult> Impostazioni()
    {
        var scelte = await notifiche.EmailAsync();
        var gruppi = CatalogoNotifiche.Per(await notifiche.RuoliAsync())
            .GroupBy(d => d.Categoria)
            .Select(g => new GruppoNotifiche(g.Key, g.Select(d => new ScelteNotifica(d, scelte[d.Tipo])).ToList()))
            .ToList();
        return View(new ImpostazioniNotificheViewModel { Gruppi = gruppi, EmailConfigurata = email.Configurato });
    }

    /// <summary>
    /// Accende o spegne l'email delle tipologie indicate (una, quelle di una sezione o tutte). Chiamata dalla pagina
    /// delle impostazioni a ogni interruttore: risponde senza contenuto.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Email(List<TipoNotifica> tipi, bool attiva)
    {
        var ricevibili = CatalogoNotifiche.Per(await notifiche.RuoliAsync()).Select(d => d.Tipo).ToHashSet();
        var scelti = tipi.Where(ricevibili.Contains).Distinct().ToList();
        if (scelti.Count == 0) return BadRequest();
        await notifiche.ImpostaEmailAsync(scelti, attiva);
        return NoContent();
    }

    private IActionResult Torna(string? ritorno) => Url.IsLocalUrl(ritorno) ? LocalRedirect(ritorno) : RedirectToAction(nameof(Index));
}
