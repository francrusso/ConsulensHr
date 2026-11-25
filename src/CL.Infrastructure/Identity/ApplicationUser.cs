using Microsoft.AspNetCore.Identity;

namespace CL.Infrastructure.Identity;

/// <summary>
/// Utente della piattaforma. Il ruolo determina il perimetro: Amministratore → tutto; Consulente → le aziende
/// assegnate (<see cref="Persistence.AssegnazioneAzienda"/>); Azienda → solo <see cref="AziendaId"/>; Dipendente → solo <see cref="DipendenteId"/>.
/// Un utente disattivato (<see cref="Attivo"/> = false) non accede più.
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
    public string Nome { get; set; } = "";
    public string Cognome { get; set; } = "";
    public Guid? AziendaId { get; set; }
    public Guid? DipendenteId { get; set; }
    public bool Attivo { get; set; } = true;

    /// <summary>
    /// Consenso a ricevere notifiche su WhatsApp (data in cui ha confermato il numero). Il numero è <see cref="IdentityUser.PhoneNumber"/>,
    /// valido solo se <see cref="IdentityUser.PhoneNumberConfirmed"/>: lo conferma il codice ricevuto su WhatsApp.
    /// </summary>
    public DateTime? WhatsAppConsensoIl { get; set; }
}

public static class Ruoli
{
    /// <summary>Super amministratore: crea i consulenti e assegna loro le aziende; vede tutto (ha anche il ruolo consulente).</summary>
    public const string Amministratore = "Amministratore";
    public const string Consulente = "Consulente";
    public const string Azienda = "Azienda";
    public const string Dipendente = "Dipendente";

    public static readonly IReadOnlyList<string> Tutti = [Amministratore, Consulente, Azienda, Dipendente];
}
