using CL.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CL.Web.Infrastruttura;

/// <summary>
/// Finché il profilo non è completo, il referente può usare solo il percorso guidato dell'azienda (e l'account),
/// il dipendente solo quello dei propri dati. Il claim lo mette ClaimsFactory al login e sparisce quando il percorso
/// si conclude e la sessione viene rinnovata.
/// </summary>
public sealed class FiltroOnboarding : IActionFilter
{
    // Indirizzi: i suggerimenti di comune e via servono proprio ai passi delle sedi e della residenza.
    // Notifiche: la campanella resta consultabile (le pagine a cui porta restano chiuse fino a fine percorso).
    private static readonly HashSet<string> Consentiti = new(StringComparer.OrdinalIgnoreCase) { "Onboarding", "DatiPersonali", "Account", "Indirizzi", "Notifiche" };

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var claim = context.HttpContext.User.FindFirst(ClaimsPersonalizzati.OnboardingDaCompletare);
        if (claim is null) return;
        if (context.ActionDescriptor is ControllerActionDescriptor a && Consentiti.Contains(a.ControllerName)) return;
        context.Result = new RedirectToActionResult("Index",
            claim.Value == ClaimsPersonalizzati.OnboardingDipendente ? "DatiPersonali" : "Onboarding", null);
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
