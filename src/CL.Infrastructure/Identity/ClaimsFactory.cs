using System.Security.Claims;
using CL.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CL.Infrastructure.Identity;

public static class ClaimsPersonalizzati
{
    public const string AziendaId = "cl:azienda_id";
    public const string DipendenteId = "cl:dipendente_id";
    public const string NomeVisualizzato = "cl:nome";
    /// <summary>Presente finché l'azienda del referente non ha completato il profilo per lo studio.</summary>
    public const string OnboardingDaCompletare = "cl:onboarding";
}

/// <summary>Aggiunge al cookie di login il perimetro dell'utente (azienda/dipendente) e il nome da mostrare.</summary>
internal sealed class ClaimsFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options,
    AppDbContext db)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var id = await base.GenerateClaimsAsync(user);
        if (user.AziendaId is { } a) id.AddClaim(new Claim(ClaimsPersonalizzati.AziendaId, a.ToString()));
        if (user.DipendenteId is { } d) id.AddClaim(new Claim(ClaimsPersonalizzati.DipendenteId, d.ToString()));
        var nome = $"{user.Nome} {user.Cognome}".Trim();
        id.AddClaim(new Claim(ClaimsPersonalizzati.NomeVisualizzato, nome.Length > 0 ? nome : user.Email ?? ""));

        // Rigenerato a ogni login e a ogni verifica periodica del security stamp.
        if (user.AziendaId is { } az && id.HasClaim(id.RoleClaimType, Ruoli.Azienda)
            && !await db.ProfiliAzienda.AnyAsync(p => p.AziendaId == az && p.CompletatoIl != null))
            id.AddClaim(new Claim(ClaimsPersonalizzati.OnboardingDaCompletare, "1"));
        return id;
    }
}
