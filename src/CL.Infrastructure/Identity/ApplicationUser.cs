using Microsoft.AspNetCore.Identity;

namespace CL.Infrastructure.Identity;

/// <summary>
/// Utente della piattaforma. Il ruolo determina il perimetro:
/// Consulente → tutto il consulente; Azienda → solo <see cref="AziendaId"/>; Dipendente → solo <see cref="DipendenteId"/>.
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
    public string Nome { get; set; } = "";
    public string Cognome { get; set; } = "";
    public Guid? AziendaId { get; set; }
    public Guid? DipendenteId { get; set; }
    public bool Attivo { get; set; } = true;
}

public static class Ruoli
{
    public const string Consulente = "Consulente";
    public const string Azienda = "Azienda";
    public const string Dipendente = "Dipendente";

    public static readonly IReadOnlyList<string> Tutti = [Consulente, Azienda, Dipendente];
}
