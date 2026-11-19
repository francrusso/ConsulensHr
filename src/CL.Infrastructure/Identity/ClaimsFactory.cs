using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CL.Infrastructure.Identity;

public static class ClaimsPersonalizzati
{
    public const string AziendaId = "cl:azienda_id";
    public const string DipendenteId = "cl:dipendente_id";
    public const string NomeVisualizzato = "cl:nome";
}

/// <summary>Aggiunge al cookie di login il perimetro dell'utente (azienda/dipendente) e il nome da mostrare.</summary>
internal sealed class ClaimsFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var id = await base.GenerateClaimsAsync(user);
        if (user.AziendaId is { } a) id.AddClaim(new Claim(ClaimsPersonalizzati.AziendaId, a.ToString()));
        if (user.DipendenteId is { } d) id.AddClaim(new Claim(ClaimsPersonalizzati.DipendenteId, d.ToString()));
        var nome = $"{user.Nome} {user.Cognome}".Trim();
        id.AddClaim(new Claim(ClaimsPersonalizzati.NomeVisualizzato, nome.Length > 0 ? nome : user.Email ?? ""));
        return id;
    }
}
