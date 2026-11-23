using System.Diagnostics;
using CL.Web.Infrastruttura;
using CL.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CL.Web.Controllers;

public sealed class HomeController(UtenteCorrente utente, ServizioPanoramica panoramica) : Controller
{
    /// <summary>
    /// Panoramica per consulente e referente. Il dipendente non ha la panoramica: la sua prima pagina è il calendario;
    /// solo un accesso da dipendente non ancora collegato a una persona vede una pagina di benvenuto senza dati.
    /// </summary>
    public async Task<IActionResult> Index(Guid? aziendaId)
    {
        if (!utente.IsDipendente) return View(await panoramica.CalcolaAsync(aziendaId));
        if (utente.DipendenteId is not null) return RedirectToAction("Index", "Ufficio");
        return View("IndexDipendente");
    }

    [AllowAnonymous, ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
