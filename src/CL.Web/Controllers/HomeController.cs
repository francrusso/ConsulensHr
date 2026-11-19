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

        var vm = new PanoramicaViewModel(
            utente.IsConsulente ? await db.Aziende.CountAsync(a => a.Attiva) : 0,
            await dipendenti.CountAsync(d => d.Contratti.Any(c => c.DataInizio <= oggi && (c.DataFine == null || c.DataFine >= oggi))),
            await db.RichiesteAssenza.CountAsync(r => ids.Contains(r.DipendenteId) && r.Stato == StatoRichiesta.InAttesa));
        return View(vm);
    }

    [AllowAnonymous, ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
