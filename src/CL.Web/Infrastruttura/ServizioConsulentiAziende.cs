using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

/// <summary>Per il super amministratore: a quali consulenti è legata ogni azienda (nomi già pronti da mostrare).</summary>
public sealed class ServizioConsulentiAziende(AppDbContext db, UtenteCorrente utente)
{
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
