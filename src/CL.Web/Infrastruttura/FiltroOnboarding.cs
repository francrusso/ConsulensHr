using CL.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CL.Web.Infrastruttura;

/// <summary>
/// Finché il profilo dell'azienda non è completo, il referente può usare solo il percorso guidato (e l'account).
/// </summary>
public sealed class FiltroOnboarding : IActionFilter
{
    private static readonly HashSet<string> Consentiti = new(StringComparer.OrdinalIgnoreCase) { "Onboarding", "Account" };

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.HttpContext.User.HasClaim(c => c.Type == ClaimsPersonalizzati.OnboardingDaCompletare)) return;
        if (context.ActionDescriptor is ControllerActionDescriptor a && Consentiti.Contains(a.ControllerName)) return;
        context.Result = new RedirectToActionResult("Index", "Onboarding", null);
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
