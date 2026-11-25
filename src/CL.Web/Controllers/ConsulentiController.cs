using CL.Domain.Notifiche;
using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

public sealed record ConsulenteRiga(string Id, string Nome, string Email, StatoAccesso Stato, bool Amministratore, int Aziende)
{
    /// <summary>Nome da mostrare: l'email se nome e cognome mancano.</summary>
    public string NomeVisibile => string.IsNullOrWhiteSpace(Nome) ? Email : Nome;
}

public sealed record AziendaAssegnabile(Guid Id, string RagioneSociale, string PartitaIva, bool Assegnata, int AltriConsulenti);

public sealed class ConsulenteViewModel
{
    public required ConsulenteRiga Consulente { get; init; }
    public required List<AziendaAssegnabile> Aziende { get; init; }
}

/// <summary>
/// Super amministratore: crea i consulenti e decide quali aziende vede ciascuno.
/// Un consulente vede solo le aziende assegnate (o create da lui), con il loro personale e i documenti.
/// </summary>
[Authorize(Roles = Ruoli.Amministratore)]
public sealed class ConsulentiController(AppDbContext db, ServizioNotifiche notifiche) : AppController
{
    public async Task<IActionResult> Index(string? q)
    {
        var idConsulenti = db.IdConRuolo(Ruoli.Consulente);
        var consulenti = await db.Users.Where(u => idConsulenti.Contains(u.Id)).ToListAsync();
        var amministratori = (await db.IdConRuolo(Ruoli.Amministratore).ToListAsync()).ToHashSet();
        var conteggi = await db.AssegnazioniAziende.GroupBy(a => a.UserId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        var righe = consulenti
            .Select(u => Riga(u, amministratori.Contains(u.Id), conteggi.GetValueOrDefault(u.Id)))
            .Where(c => string.IsNullOrWhiteSpace(q) || c.Nome.Contains(q, StringComparison.CurrentCultureIgnoreCase) || c.Email.Contains(q, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(c => c.Nome).ToList();
        ViewBag.Ricerca = q;
        return View(righe);
    }

    public async Task<IActionResult> Dettaglio(string id)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id);
        if (u is null) return NotFound();
        var mie = (await db.AssegnazioniAziende.Where(a => a.UserId == id).Select(a => a.AziendaId).ToListAsync()).ToHashSet();
        var altri = await db.AssegnazioniAziende.Where(a => a.UserId != id).GroupBy(a => a.AziendaId)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        // Prima le aziende già assegnate, poi le altre; entrambe in ordine alfabetico.
        var aziende = (await db.Aziende.AsNoTracking().OrderBy(a => a.RagioneSociale).ToListAsync())
            .Select(a => new AziendaAssegnabile(a.Id, a.RagioneSociale, a.PartitaIva.Valore, mie.Contains(a.Id), altri.GetValueOrDefault(a.Id)))
            .OrderByDescending(a => a.Assegnata).ThenBy(a => a.RagioneSociale).ToList();
        var admin = await db.IdConRuolo(Ruoli.Amministratore).ContainsAsync(id);
        return View(new ConsulenteViewModel { Consulente = Riga(u, admin, mie.Count), Aziende = aziende });
    }

    /// <summary>Le aziende spuntate diventano quelle del consulente: le altre gli vengono tolte.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Assegna(string id, List<Guid>? aziende)
    {
        if (!await db.Users.AnyAsync(x => x.Id == id)) return NotFound();
        var scelte = (aziende ?? []).ToHashSet();
        var attuali = await db.AssegnazioniAziende.Where(a => a.UserId == id).ToListAsync();
        db.AssegnazioniAziende.RemoveRange(attuali.Where(a => !scelte.Contains(a.AziendaId)));
        var esistenti = await db.Aziende.Where(a => scelte.Contains(a.Id)).Select(a => a.Id).ToListAsync();
        var nuove = esistenti.Where(az => attuali.All(a => a.AziendaId != az)).ToList();
        foreach (var az in nuove)
            db.AssegnazioniAziende.Add(new AssegnazioneAzienda { AziendaId = az, UserId = id, AssegnataIl = DateTime.UtcNow });
        await db.SaveChangesAsync();
        if (nuove.Count > 0)
        {
            var nomi = await db.Aziende.Where(a => nuove.Contains(a.Id)).OrderBy(a => a.RagioneSociale).Select(a => a.RagioneSociale).ToListAsync();
            await notifiche.InviaAsync(TipoNotifica.AziendaAssegnata, await db.UtentiAsync([id]),
                nomi.Count == 1 ? $"Nuova azienda assegnata: {nomi[0]}" : $"{nomi.Count} nuove aziende assegnate",
                $"Il super amministratore ti ha assegnato {string.Join(", ", nomi)}: ora {(nomi.Count == 1 ? "la trovi" : "le trovi")} tra le tue aziende.",
                nomi.Count == 1 ? $"/Aziende/Dettaglio/{nuove[0]}" : "/Aziende");
        }
        Conferma($"Aziende assegnate: {esistenti.Count}.");
        return RedirectToAction(nameof(Dettaglio), new { id });
    }

    private static ConsulenteRiga Riga(ApplicationUser u, bool amministratore, int aziende) =>
        new(u.Id, $"{u.Cognome} {u.Nome}".Trim(), u.Email!, ServizioAccessi.Stato(u), amministratore, aziende);
}
