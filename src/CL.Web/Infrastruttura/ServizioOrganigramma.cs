using CL.Domain.Organizzazione;
using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

/// <summary>Organigramma di un'azienda e calcolo di chi approva ferie e permessi di ciascun dipendente.</summary>
public sealed class ServizioOrganigramma(AppDbContext db)
{
    public Task<List<Team>> TeamAsync(Guid aziendaId) =>
        db.Team.AsNoTracking().Where(t => t.AziendaId == aziendaId).OrderBy(t => t.Nome).ToListAsync();

    /// <summary>Approvatore di ogni dipendente dell'azienda (null = referente dell'azienda).</summary>
    public async Task<Dictionary<Guid, Guid?>> ApprovatoriAsync(Guid aziendaId)
    {
        var team = await TeamAsync(aziendaId);
        var nodi = await db.Dipendenti.AsNoTracking().IgnoreAutoIncludes().Where(d => d.AziendaId == aziendaId)
            .Select(d => new NodoDipendente(d.Id, d.TeamId, d.ResponsabileId)).ToListAsync();
        return Organigramma.Approvatori(nodi, team);
    }

    public async Task<Guid?> ApprovatoreAsync(Guid dipendenteId)
    {
        var aziendaId = await db.Dipendenti.Where(d => d.Id == dipendenteId).Select(d => (Guid?)d.AziendaId).FirstOrDefaultAsync();
        if (aziendaId is null) return null;
        return (await ApprovatoriAsync(aziendaId.Value)).GetValueOrDefault(dipendenteId);
    }

    /// <summary>I dipendenti di cui <paramref name="responsabileId"/> approva le richieste.</summary>
    public async Task<List<Guid>> CollaboratoriAsync(Guid responsabileId)
    {
        var aziendaId = await db.Dipendenti.Where(d => d.Id == responsabileId).Select(d => (Guid?)d.AziendaId).FirstOrDefaultAsync();
        if (aziendaId is null) return [];
        return (await ApprovatoriAsync(aziendaId.Value)).Where(x => x.Value == responsabileId).Select(x => x.Key).ToList();
    }
}
