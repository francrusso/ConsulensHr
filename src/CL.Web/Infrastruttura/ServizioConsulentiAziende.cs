using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

/// <summary>Per il super amministratore: a quali consulenti è legata ogni azienda (nomi già pronti da mostrare).</summary>
public sealed class ServizioConsulentiAziende(AppDbContext db, UtenteCorrente utente)
{
    /// <summary>Consulenti (non super amministratori) per il filtro in cima alle liste; null per chi non è super amministratore.</summary>
    public async Task<List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>?> ElencoAsync(string? scelto)
    {
        if (!utente.IsAmministratore) return null;
        var admin = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                           where r.Name == CL.Infrastructure.Identity.Ruoli.Amministratore select ur.UserId).ToListAsync();
        return (await (from u in db.Users
                       join ur in db.UserRoles on u.Id equals ur.UserId
                       join r in db.Roles on ur.RoleId equals r.Id
                       where r.Name == CL.Infrastructure.Identity.Ruoli.Consulente && !admin.Contains(u.Id)
                       orderby u.Cognome, u.Nome
                       select new { u.Id, Nome = (u.Cognome + " " + u.Nome).Trim(), u.Email }).ToListAsync())
            .Select(x => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(string.IsNullOrWhiteSpace(x.Nome) ? x.Email : x.Nome, x.Id, x.Id == scelto))
            .ToList();
    }

    /// <summary>Aziende assegnate a un consulente (per il filtro del super amministratore).</summary>
    public async Task<List<Guid>> AziendeDiAsync(string consulenteId) =>
        await db.AssegnazioniAziende.Where(a => a.UserId == consulenteId).Select(a => a.AziendaId).ToListAsync();

    /// <summary>Azienda → "Rossi Mario, Bianchi Laura". Vuoto per chi non è super amministratore.</summary>
    public async Task<Dictionary<Guid, string>> NomiAsync()
    {
        if (!utente.IsAmministratore) return [];
        var righe = await (from a in db.AssegnazioniAziende
                           join u in db.Users on a.UserId equals u.Id
                           orderby u.Cognome, u.Nome
                           select new { a.AziendaId, Nome = (u.Cognome + " " + u.Nome).Trim(), u.Email }).ToListAsync();
        return righe.GroupBy(r => r.AziendaId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(r => string.IsNullOrWhiteSpace(r.Nome) ? r.Email! : r.Nome)));
    }
}
