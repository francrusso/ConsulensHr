using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

public sealed record ConsulenteRiga(string Id, string Nome, string Email, StatoAccesso Stato, bool Amministratore, int Aziende);
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
public sealed class ConsulentiController(AppDbContext db) : AppController
{
    public async Task<IActionResult> Index(string? q)
    {
        var consulenti = await (from u in db.Users
                                join ur in db.UserRoles on u.Id equals ur.UserId
                                join r in db.Roles on ur.RoleId equals r.Id
                                where r.Name == Ruoli.Consulente
                                select u).ToListAsync();
        var amministratori = (await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                                     where r.Name == Ruoli.Amministratore select ur.UserId).ToListAsync()).ToHashSet();
        var conteggi = await db.AssegnazioniAziende.GroupBy(a => a.UserId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        var righe = consulenti
            .Select(u => new ConsulenteRiga(u.Id, $"{u.Cognome} {u.Nome}".Trim(), u.Email!, ServizioAccessi.Stato(u), amministratori.Contains(u.Id),
                conteggi.GetValueOrDefault(u.Id)))
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
        var aziende = (await db.Aziende.AsNoTracking().OrderBy(a => a.RagioneSociale).ToListAsync())
            .Select(a => new AziendaAssegnabile(a.Id, a.RagioneSociale, a.PartitaIva.Valore, mie.Contains(a.Id), altri.GetValueOrDefault(a.Id)))
            .OrderByDescending(a => a.Assegnata).ThenBy(a => a.RagioneSociale).ToList();
        var admin = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                           where ur.UserId == id && r.Name == Ruoli.Amministratore select ur).AnyAsync();
        return View(new ConsulenteViewModel
        {
            Consulente = new ConsulenteRiga(u.Id, $"{u.Cognome} {u.Nome}".Trim(), u.Email!, ServizioAccessi.Stato(u), admin, mie.Count),
            Aziende = aziende,
        });
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
        foreach (var az in esistenti.Where(az => attuali.All(a => a.AziendaId != az)))
            db.AssegnazioniAziende.Add(new AssegnazioneAzienda { AziendaId = az, UserId = id, AssegnataIl = DateTime.UtcNow });
        await db.SaveChangesAsync();
        Conferma($"Aziende assegnate: {esistenti.Count}.");
        return RedirectToAction(nameof(Dettaglio), new { id });
    }
}
