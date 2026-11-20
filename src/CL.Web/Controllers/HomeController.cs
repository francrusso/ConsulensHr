using System.Diagnostics;
using CL.Domain.Assenze;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using CL.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

public sealed record PanoramicaViewModel(int Aziende, int DipendentiInForza, int RichiesteInAttesa);

public sealed class HomeController(AppDbContext db, UtenteCorrente utente, TimeProvider clock) : Controller
{
    public async Task<IActionResult> Index()
    {
        var oggi = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var dipendenti = utente.Filtra(db.Dipendenti);
        var ids = dipendenti.Select(d => d.Id);
        var inForza = Filtri.InForzaIl(oggi);

        // Un solo round-trip: i tre conteggi come sottoquery di un'unica SELECT.
        // Senza aziende non possono esistere dipendenti né richieste, quindi "nessuna riga" equivale a tutti zero.
        var c = await db.Aziende.Take(1).Select(_ => new
        {
            Aziende = db.Aziende.Count(a => a.Attiva),
            InForza = dipendenti.Count(inForza),
            InAttesa = db.RichiesteAssenza.Count(r => ids.Contains(r.DipendenteId) && r.Stato == StatoRichiesta.InAttesa),
        }).FirstOrDefaultAsync();

        return View(new PanoramicaViewModel(utente.IsConsulente ? c?.Aziende ?? 0 : 0, c?.InForza ?? 0, c?.InAttesa ?? 0));
    }

    [AllowAnonymous, ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
