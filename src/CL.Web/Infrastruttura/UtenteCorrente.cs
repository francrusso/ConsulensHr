using System.Security.Claims;
using CL.Domain.Dipendenti;
using CL.Infrastructure.Identity;

namespace CL.Web.Infrastruttura;

public static class Policy
{
    public const string SoloConsulente = nameof(SoloConsulente);
    /// <summary>Consulente (tutte le aziende) o referente HR di un'azienda (solo la propria).</summary>
    public const string GestionePersonale = nameof(GestionePersonale);
}

/// <summary>Chi sta usando l'applicazione e cosa può vedere.</summary>
public sealed class UtenteCorrente(IHttpContextAccessor accessor)
{
    private ClaimsPrincipal User => accessor.HttpContext?.User ?? new ClaimsPrincipal();

    public string Email => User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "";
    public string Nome => User.FindFirstValue(ClaimsPersonalizzati.NomeVisualizzato) ?? Email;
    public bool IsConsulente => User.IsInRole(Ruoli.Consulente);
    /// <summary>Super amministratore: crea i consulenti e assegna le aziende; vede tutte le aziende.</summary>
    public bool IsAmministratore => User.IsInRole(Ruoli.Amministratore);
    public string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    public bool IsAzienda => User.IsInRole(Ruoli.Azienda);
    public bool IsDipendente => User.IsInRole(Ruoli.Dipendente);

    public Guid? AziendaId => Guid.TryParse(User.FindFirstValue(ClaimsPersonalizzati.AziendaId), out var g) ? g : null;
    public Guid? DipendenteId => Guid.TryParse(User.FindFirstValue(ClaimsPersonalizzati.DipendenteId), out var g) ? g : null;

    /// <summary>Restringe una query di dipendenti al perimetro dell'utente.</summary>
    public IQueryable<Dipendente> Filtra(IQueryable<Dipendente> query)
    {
        if (IsConsulente) return query;
        if (IsAzienda && AziendaId is { } a) return query.Where(d => d.AziendaId == a);
        if (IsDipendente && DipendenteId is { } d) return query.Where(x => x.Id == d);
        return query.Where(_ => false);
    }

    public bool PuoGestireAzienda(Guid aziendaId) => IsConsulente || (IsAzienda && AziendaId == aziendaId);
}
