using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

/// <summary>
/// Per il super amministratore: a quali consulenti è legata ogni azienda (nomi già pronti da mostrare).
/// Le assegnazioni (tabella AssegnazioniAziende) decidono il perimetro di ogni consulente, vedi Program.cs.
/// </summary>
public sealed class ServizioConsulentiAziende(AppDbContext db, UtenteCorrente utente)
{
    /// <summary>Consulenti (non super amministratori) per il filtro in cima alle liste; null per chi non è super amministratore.</summary>
    public async Task<List<SelectListItem>?> ElencoAsync(string? scelto)
    {
        if (!utente.IsAmministratore) return null;
        var consulenti = db.IdConRuolo(Ruoli.Consulente);
        var amministratori = db.IdConRuolo(Ruoli.Amministratore);
        var righe = await db.Users
            .Where(u => consulenti.Contains(u.Id) && !amministratori.Contains(u.Id))
            .OrderBy(u => u.Cognome).ThenBy(u => u.Nome)
            .Select(u => new { u.Id, Nome = (u.Cognome + " " + u.Nome).Trim(), u.Email })
            .ToListAsync();
        return righe.Select(x => new SelectListItem(NomeOEmail(x.Nome, x.Email), x.Id, x.Id == scelto)).ToList();
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
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(r => NomeOEmail(r.Nome, r.Email))));
    }

    private static string NomeOEmail(string nome, string? email) => string.IsNullOrWhiteSpace(nome) ? email ?? "" : nome;
}
