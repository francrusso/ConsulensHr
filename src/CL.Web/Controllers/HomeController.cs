using System.Diagnostics;
using CL.Domain.BustePaga;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using CL.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

public sealed class PanoramicaDipendente
{
    public required IReadOnlyList<CL.Domain.Assenze.SaldoAssenza> Saldi { get; init; }
    public int RichiesteInAttesa { get; init; }
    public BustaRiga? UltimaBusta { get; init; }
}

public sealed class HomeController(AppDbContext db, UtenteCorrente utente, ServizioPanoramica panoramica, ServizioSaldi saldi, TimeProvider clock) : Controller
{
    public async Task<IActionResult> Index(Guid? aziendaId)
    {
        if (!utente.IsDipendente) return View(await panoramica.CalcolaAsync(aziendaId));

        // Dipendente: i propri saldi, le richieste in attesa e l'ultima busta paga.
        var oggi = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var io = utente.DipendenteId is { } id ? await db.Dipendenti.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id) : null;
        if (io is null) return View("IndexDipendente", new PanoramicaDipendente { Saldi = [] });
        return View("IndexDipendente", new PanoramicaDipendente
        {
            Saldi = await saldi.Calcola(io, oggi.Year, oggi.Month),
            RichiesteInAttesa = await db.RichiesteAssenza.CountAsync(r => r.DipendenteId == io.Id && r.Stato == CL.Domain.Assenze.StatoRichiesta.InAttesa),
            UltimaBusta = await db.BustePaga.AsNoTracking()
                .Where(b => b.DipendenteId == io.Id && b.Stato == StatoBustaPaga.Pubblicata)
                .OrderByDescending(b => b.Anno).ThenByDescending(b => b.Mese)
                .Select(b => new BustaRiga
                {
                    Id = b.Id, DipendenteId = b.DipendenteId, Dipendente = "", Azienda = "", Anno = b.Anno, Mese = b.Mese,
                    Tipo = b.Tipo, Netto = b.Netto, Stato = b.Stato, PubblicataIl = b.PubblicataIl, VisualizzataIl = b.VisualizzataIl,
                })
                .FirstOrDefaultAsync(),
        });
    }

    [AllowAnonymous, ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
