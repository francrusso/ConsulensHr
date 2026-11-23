using CL.Domain.Organizzazione;
using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

/// <summary>
/// Organigramma di un'azienda e calcolo di chi approva ferie e permessi di ciascun dipendente.
/// La regola sta nel dominio (<see cref="Organigramma.Approvatori"/>): qui si caricano solo team e persone dell'azienda.
/// </summary>
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

    public async Task<Guid?> ApprovatoreAsync(Guid dipendenteId) =>
        await AziendaDi(dipendenteId) is { } aziendaId ? (await ApprovatoriAsync(aziendaId)).GetValueOrDefault(dipendenteId) : null;

    /// <summary>I dipendenti di cui <paramref name="responsabileId"/> approva le richieste.</summary>
    public async Task<List<Guid>> CollaboratoriAsync(Guid responsabileId) =>
        await AziendaDi(responsabileId) is { } aziendaId
            ? (await ApprovatoriAsync(aziendaId)).Where(x => x.Value == responsabileId).Select(x => x.Key).ToList()
            : [];

    private Task<Guid?> AziendaDi(Guid dipendenteId) =>
        db.Dipendenti.Where(d => d.Id == dipendenteId).Select(d => (Guid?)d.AziendaId).FirstOrDefaultAsync();
}
