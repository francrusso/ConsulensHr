using CL.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace CL.Web.Infrastruttura;

/// <summary>Base dei controller: regole del dominio violate e messaggi mostrati in cima alla pagina (vedi _Messaggi).</summary>
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

    /// <summary>
    /// Come <see cref="Prova"/>, per le azioni che rispondono con un redirect: la regola violata diventa
    /// il messaggio di errore della pagina successiva.
    /// </summary>
    protected bool ProvaOAvvisa(Action azione)
    {
        try
        {
            azione();
            return true;
        }
        catch (DomainException ex)
        {
            Errore(ex.Message);
            return false;
        }
    }

    protected void Conferma(string messaggio) => TempData["Messaggio"] = messaggio;
    protected void Errore(string messaggio) => TempData["Errore"] = messaggio;
}
