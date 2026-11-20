using CL.Domain.Common;
using CL.Domain.Dipendenti;
using CL.Domain.Organizzazione;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

public sealed record MembroTeam(Guid Id, string Nome, string? Posizione, Guid? TeamId, Guid? ResponsabileDirettoId, Guid? ApprovatoreId, bool InForza);

public sealed class OrganigrammaViewModel
{
    public required Guid AziendaId { get; init; }
    public required string Azienda { get; init; }
    public required List<Team> Team { get; init; }
    public required List<MembroTeam> Dipendenti { get; init; }
    public bool PerConto { get; init; }

    public string NomeDi(Guid? dipendenteId) => dipendenteId is { } id ? Dipendenti.FirstOrDefault(d => d.Id == id)?.Nome ?? "—" : "—";

    /// <summary>Team in ordine di albero (padre, poi figli) con il livello di rientro.</summary>
    public IEnumerable<(Team Team, int Livello)> Albero()
    {
        IEnumerable<(Team, int)> Visita(Guid? padre, int livello, HashSet<Guid> visti) =>
            Team.Where(t => t.PadreId == padre && visti.Add(t.Id))
                .SelectMany(t => new[] { (t, livello) }.Concat(Visita(t.Id, livello + 1, visti)));
        var visti = new HashSet<Guid>();
        var risultato = Visita(null, 0, visti).ToList();
        // Team con padre non trovato (non dovrebbe succedere): in fondo, al primo livello.
        risultato.AddRange(Team.Where(t => !visti.Contains(t.Id)).Select(t => (t, 0)));
        return risultato;
    }
}

/// <summary>
/// Organigramma dell'azienda: team con gerarchia e responsabile, posizione e responsabile diretto dei dipendenti.
/// Lo gestiscono il referente (la propria azienda) e lo studio (per conto di qualsiasi azienda).
/// </summary>
[Authorize(Policy = Policy.GestionePersonale)]
public sealed class TeamController(AppDbContext db, UtenteCorrente utente, ServizioOrganigramma organigramma, TimeProvider clock) : AppController
{
    public async Task<IActionResult> Index(Guid? aziendaId)
    {
        var id = Azienda(aziendaId);
        if (id is null) return RedirectToAction("Index", "Aziende");
        var azienda = await db.Aziende.AsNoTracking().Where(a => a.Id == id).Select(a => a.RagioneSociale).FirstOrDefaultAsync();
        if (azienda is null) return NotFound();

        var oggi = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var team = await organigramma.TeamAsync(id.Value);
        var approvatori = await organigramma.ApprovatoriAsync(id.Value);
        var dipendenti = (await db.Dipendenti.AsNoTracking().Where(d => d.AziendaId == id).OrderBy(d => d.Cognome).ThenBy(d => d.Nome).ToListAsync())
            .Where(d => d.InForzaIl(oggi) || d.TeamId is not null)
            .Select(d => new MembroTeam(d.Id, d.NomeCompleto, d.Posizione, d.TeamId, d.ResponsabileId, approvatori.GetValueOrDefault(d.Id), d.InForzaIl(oggi)))
            .ToList();

        return View(new OrganigrammaViewModel
        {
            AziendaId = id.Value, Azienda = azienda, Team = team, Dipendenti = dipendenti, PerConto = utente.IsConsulente,
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Salva(Guid aziendaId, Guid? id, string? nome, Guid? padreId, Guid? responsabileId)
    {
        var az = Azienda(aziendaId);
        if (az is null || !await db.Aziende.AnyAsync(a => a.Id == az)) return NotFound();
        var tutti = await db.Team.Where(t => t.AziendaId == az).ToListAsync();

        Team? team;
        if (id is { } tid)
        {
            team = tutti.FirstOrDefault(t => t.Id == tid);
            if (team is null) return NotFound();
        }
        else db.Team.Add(team = new Team(az.Value, string.IsNullOrWhiteSpace(nome) ? "?" : nome));

        var responsabile = responsabileId is { } rid ? await db.Dipendenti.FirstOrDefaultAsync(d => d.Id == rid && d.AziendaId == az) : null;
        try
        {
            team.Rinomina(nome ?? "");
            if (tutti.Any(t => t.Id != team.Id && string.Equals(t.Nome, team.Nome, StringComparison.OrdinalIgnoreCase)))
                throw new DomainException($"Esiste già un team \"{team.Nome}\".");
            team.ImpostaPadre(padreId is { } pid ? tutti.FirstOrDefault(t => t.Id == pid) : null, tutti);
            team.ImpostaResponsabile(responsabile);
        }
        catch (DomainException ex)
        {
            Errore(ex.Message);
            return Torna(az.Value);
        }
        await db.SaveChangesAsync();
        Conferma(id is null ? $"Team \"{team.Nome}\" creato." : "Team aggiornato.");
        return Torna(az.Value);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Elimina(Guid aziendaId, Guid id)
    {
        var az = Azienda(aziendaId);
        var team = await db.Team.FirstOrDefaultAsync(t => t.Id == id && t.AziendaId == az);
        if (team is null) return NotFound();
        if (await db.Team.AnyAsync(t => t.PadreId == id))
            Errore("Il team contiene altri team: spostali o eliminali prima.");
        else if (await db.Dipendenti.AnyAsync(d => d.TeamId == id))
            Errore("Il team ha dei componenti: assegnali a un altro team prima di eliminarlo.");
        else
        {
            db.Team.Remove(team);
            await db.SaveChangesAsync();
            Conferma($"Team \"{team.Nome}\" eliminato.");
        }
        return Torna(team.AziendaId);
    }

    /// <summary>Team, posizione e responsabile diretto di un dipendente.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Assegna(Guid dipendenteId, Guid? teamId, string? posizione, Guid? responsabileId, string? ritorno)
    {
        var dip = await utente.Filtra(db.Dipendenti).FirstOrDefaultAsync(d => d.Id == dipendenteId);
        if (dip is null) return NotFound();
        var team = teamId is { } t ? await db.Team.FirstOrDefaultAsync(x => x.Id == t && x.AziendaId == dip.AziendaId) : null;
        var resp = responsabileId is { } r ? await db.Dipendenti.FirstOrDefaultAsync(x => x.Id == r && x.AziendaId == dip.AziendaId) : null;
        if (Prova(() => dip.ImpostaOrganizzazione(team, posizione, resp)))
        {
            await db.SaveChangesAsync();
            Conferma($"Organizzazione di {dip.NomeCompleto} aggiornata.");
        }
        else Errore(ModelState[""]!.Errors[0].ErrorMessage);
        return Url.IsLocalUrl(ritorno) ? LocalRedirect(ritorno) : Torna(dip.AziendaId);
    }

    private Guid? Azienda(Guid? richiesta) => utente.IsAzienda ? utente.AziendaId : richiesta;

    private IActionResult Torna(Guid aziendaId) =>
        RedirectToAction(nameof(Index), new { aziendaId = utente.IsConsulente ? aziendaId : (Guid?)null });
}
