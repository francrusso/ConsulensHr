using System.Security.Claims;
using CL.Domain.Dipendenti;
using CL.Infrastructure.Identity;

namespace CL.Web.Infrastruttura;

/// <summary>Nomi delle politiche di autorizzazione (definite in Program.cs).</summary>
public static class Policy
{
    /// <summary>Solo lo studio: consulenti e super amministratore.</summary>
    public const string SoloConsulente = nameof(SoloConsulente);
    /// <summary>Consulente (le aziende del suo perimetro) o referente HR di un'azienda (solo la propria).</summary>
    public const string GestionePersonale = nameof(GestionePersonale);
}

/// <summary>Chi sta usando l'applicazione e cosa può vedere (letto dai claim del cookie di login).</summary>
public sealed class UtenteCorrente(IHttpContextAccessor accessor)
{
    private ClaimsPrincipal User => accessor.HttpContext?.User ?? new ClaimsPrincipal();

    public string Email => User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "";
    public string Nome => User.FindFirstValue(ClaimsPersonalizzati.NomeVisualizzato) ?? Email;
    /// <summary>Vero anche per il super amministratore, che ha entrambi i ruoli.</summary>
    public bool IsConsulente => User.IsInRole(Ruoli.Consulente);
    /// <summary>Super amministratore: crea i consulenti e assegna le aziende; vede tutte le aziende.</summary>
    public bool IsAmministratore => User.IsInRole(Ruoli.Amministratore);
    public string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    public bool IsAzienda => User.IsInRole(Ruoli.Azienda);
    public bool IsDipendente => User.IsInRole(Ruoli.Dipendente);

    public Guid? AziendaId => Guid.TryParse(User.FindFirstValue(ClaimsPersonalizzati.AziendaId), out var g) ? g : null;
    public Guid? DipendenteId => Guid.TryParse(User.FindFirstValue(ClaimsPersonalizzati.DipendenteId), out var g) ? g : null;

    /// <summary>
    /// Restringe una query di dipendenti al perimetro dell'utente. Per il consulente non serve: le aziende non
    /// assegnate le nascondono già i filtri globali del database (vedi Program.cs).
    /// </summary>
    public IQueryable<Dipendente> Filtra(IQueryable<Dipendente> query)
    {
        if (IsConsulente) return query;
        if (IsAzienda && AziendaId is { } a) return query.Where(d => d.AziendaId == a);
        if (IsDipendente && DipendenteId is { } d) return query.Where(x => x.Id == d);
        return query.Where(_ => false);
    }

    public bool PuoGestireAzienda(Guid aziendaId) => IsConsulente || (IsAzienda && AziendaId == aziendaId);
}
