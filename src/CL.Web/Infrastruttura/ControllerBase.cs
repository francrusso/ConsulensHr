using CL.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace CL.Web.Infrastruttura;

public abstract class AppController : Controller
{
    /// <summary>Esegue un'operazione di dominio riportando le regole violate come errori del form.</summary>
    protected bool Prova(Action azione, string campo = "")
    {
        try
        {
            azione();
            return true;
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(campo, ex.Message);
            return false;
        }
    }

    protected void Conferma(string messaggio) => TempData["Messaggio"] = messaggio;
    protected void Errore(string messaggio) => TempData["Errore"] = messaggio;
}
