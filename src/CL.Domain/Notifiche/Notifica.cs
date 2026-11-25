using CL.Domain.Common;

namespace CL.Domain.Notifiche;

/// <summary>
/// Una notifica in piattaforma per un utente (campanella in alto e pagina Notifiche). Le notifiche in piattaforma sono
/// sempre attive: l'utente sceglie solo se ricevere anche l'email. Il link porta alla pagina dell'evento.
/// </summary>
public sealed class Notifica : Entity
{
    public const int LunghezzaTitolo = 200, LunghezzaTesto = 2000, LunghezzaLink = 500, LunghezzaChiave = 200;

    /// <summary>Id dell'utente (ASP.NET Identity) che riceve la notifica.</summary>
    public string UtenteId { get; private set; } = null!;
    public TipoNotifica Tipo { get; private set; }
    public string Titolo { get; private set; } = null!;
    /// <summary>Testo in chiaro; più paragrafi separati da un a capo.</summary>
    public string Testo { get; private set; } = null!;
    /// <summary>Percorso interno al portale (inizia con "/"), o null.</summary>
    public string? Link { get; private set; }
    /// <summary>Identifica l'evento per non ripetere lo stesso promemoria (es. la scadenza di un contratto).</summary>
    public string? Chiave { get; private set; }
    public DateTime CreataIl { get; private set; }
    public DateTime? LettaIl { get; private set; }

    public bool Letta => LettaIl is not null;

    private Notifica() { } // EF Core

    public Notifica(string utenteId, TipoNotifica tipo, string titolo, string testo, string? link, DateTime adesso, string? chiave = null)
    {
        UtenteId = Guard.NotEmpty(utenteId, "Destinatario");
        Tipo = tipo;
        Titolo = Taglia(Guard.NotEmpty(titolo, "Titolo"), LunghezzaTitolo);
        Testo = Taglia(testo?.Trim() ?? "", LunghezzaTesto);
        link = Guard.Facoltativo(link);
        // Solo percorsi del portale: mai un indirizzo esterno (il link finisce anche nelle email).
        if (link is not null && (!link.StartsWith('/') || link.StartsWith("//") || link.Contains('\\') || link.Length > LunghezzaLink))
            throw new DomainException("Il link di una notifica deve essere un percorso del portale.");
        Link = link;
        Chiave = Guard.Facoltativo(chiave) is { } c ? Taglia(c, LunghezzaChiave) : null;
        CreataIl = adesso;
    }

    /// <summary>La prima lettura resta: rileggerla non cambia la data.</summary>
    public void SegnaLetta(DateTime adesso) => LettaIl ??= adesso;

    private static string Taglia(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}

/// <summary>
/// Scelta di un utente sull'email di una tipologia. Senza riga vale il predefinito: email attiva.
/// Il canale WhatsApp, quando arriverà, avrà qui la sua scelta accanto all'email.
/// </summary>
public sealed class PreferenzaNotifica : Entity
{
    public const bool EmailPredefinita = true;

    public string UtenteId { get; private set; } = null!;
    public TipoNotifica Tipo { get; private set; }
    public bool Email { get; private set; }

    private PreferenzaNotifica() { } // EF Core

    public PreferenzaNotifica(string utenteId, TipoNotifica tipo, bool email)
    {
        UtenteId = Guard.NotEmpty(utenteId, "Utente");
        Tipo = tipo;
        Email = email;
    }

    public void ImpostaEmail(bool attiva) => Email = attiva;
}
