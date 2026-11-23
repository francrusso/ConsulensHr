using CL.Domain.Dipendenti;
using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

public sealed class MioContrattoViewModel
{
    public required Dipendente Dipendente { get; init; }
    public required string Azienda { get; init; }
    public required List<Contratto> Contratti { get; init; }
    public string? Team { get; init; }
    public string? Responsabile { get; init; }
    public string? Sede { get; init; }
    public required DateOnly Oggi { get; init; }
}

/// <summary>"Il mio contratto": il dipendente vede tutto del proprio rapporto di lavoro (sola lettura) e apre il PDF firmato.</summary>
[Authorize(Roles = Ruoli.Dipendente)]
public sealed class MioContrattoController(AppDbContext db, UtenteCorrente utente, ServizioOrganigramma organigramma, TimeProvider clock) : AppController
{
    public async Task<IActionResult> Index()
    {
        if (utente.DipendenteId is not { } id) return RedirectToAction("Index", "Home");
        var d = await db.Dipendenti.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (d is null) return NotFound();
        var profilo = await db.ProfiliAzienda.AsNoTracking().FirstOrDefaultAsync(p => p.AziendaId == d.AziendaId);
        static string Ind(CL.Domain.Aziende.Indirizzo i) => $"{i.Via}, {i.Comune} ({i.Provincia})";
        var sede = d.Sede switch
        {
            SedeLavoro.SmartWorking => "Smart working",
            SedeLavoro.SedeLegale => profilo?.SedeLegale is { } l ? "Sede legale · " + Ind(l) : "Sede legale",
            SedeLavoro.SedeOperativa => profilo?.SedeOperativa is { } o ? "Sede operativa · " + Ind(o) : "Sede operativa",
            _ => null,
        };
        // Responsabile: quello diretto, altrimenti chi approva le sue richieste secondo l'organigramma.
        var responsabile = d.ResponsabileId ?? await organigramma.ApprovatoreAsync(d.Id);
        return View(new MioContrattoViewModel
        {
            Dipendente = d,
            Azienda = await db.Aziende.Where(a => a.Id == d.AziendaId).Select(a => a.RagioneSociale).FirstAsync(),
            Contratti = d.Contratti.OrderByDescending(c => c.DataInizio).ToList(),
            Team = d.TeamId is { } t ? await db.Team.AsNoTracking().Where(x => x.Id == t).Select(x => x.Nome).FirstOrDefaultAsync() : null,
            Responsabile = responsabile is { } r
                ? await db.Dipendenti.AsNoTracking().IgnoreAutoIncludes().Where(x => x.Id == r).Select(x => x.Cognome + " " + x.Nome).FirstOrDefaultAsync()
                : null,
            Sede = sede,
            Oggi = DateOnly.FromDateTime(clock.GetLocalNow().DateTime),
        });
    }
}
