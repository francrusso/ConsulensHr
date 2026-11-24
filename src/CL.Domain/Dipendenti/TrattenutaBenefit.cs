using CL.Domain.Common;

namespace CL.Domain.Dipendenti;

/// <summary>Trattenute sullo stipendio e benefit da riportare in busta paga.</summary>
public enum TipoTrattenuta
{
    CessioneQuinto, Delegazione, Pignoramento, TrattenutaSindacale, FringeBenefit, AutoAziendale, Altro,
}

public static class TipoTrattenutaExtensions
{
    public static string Descrizione(this TipoTrattenuta t) => t switch
    {
        TipoTrattenuta.CessioneQuinto => "Cessione del quinto",
        TipoTrattenuta.Delegazione => "Delegazione di pagamento",
        TipoTrattenuta.Pignoramento => "Pignoramento",
        TipoTrattenuta.TrattenutaSindacale => "Trattenuta sindacale",
        TipoTrattenuta.FringeBenefit => "Fringe benefit",
        TipoTrattenuta.AutoAziendale => "Auto aziendale a uso promiscuo",
        _ => "Altro",
    };

    public static bool IsBenefit(this TipoTrattenuta t) => t is TipoTrattenuta.FringeBenefit or TipoTrattenuta.AutoAziendale;
}

/// <summary>Una trattenuta (cessione, pignoramento, sindacato…) o un benefit di una persona, con il periodo in cui vale.</summary>
public sealed class TrattenutaBenefit : Entity
{
    public Guid AziendaId { get; private set; }
    public Guid DipendenteId { get; private set; }
    public TipoTrattenuta Tipo { get; private set; }
    /// <summary>Finanziaria, creditore, sindacato o descrizione del benefit.</summary>
    public string? Descrizione { get; private set; }
    public decimal? ImportoMensile { get; private set; }
    /// <summary>Debito ancora da trattenere (cessioni e pignoramenti).</summary>
    public decimal? Residuo { get; private set; }
    public DateOnly Dal { get; private set; }
    public DateOnly? Al { get; private set; }

    private TrattenutaBenefit() { } // EF Core

    public TrattenutaBenefit(Guid aziendaId, Guid dipendenteId, TipoTrattenuta tipo, string? descrizione, decimal? importoMensile,
        decimal? residuo, DateOnly dal, DateOnly? al)
    {
        if (aziendaId == Guid.Empty || dipendenteId == Guid.Empty) throw new DomainException("Azienda e persona obbligatorie.");
        if (!Enum.IsDefined(tipo)) throw new DomainException("Tipo non valido.");
        if (tipo == TipoTrattenuta.Altro && string.IsNullOrWhiteSpace(descrizione)) throw new DomainException("Descrivi la voce.");
        if (importoMensile is < 0 || residuo is < 0) throw new DomainException("Gli importi non possono essere negativi.");
        if (al < dal) throw new DomainException("La fine è prima dell'inizio.");
        AziendaId = aziendaId;
        DipendenteId = dipendenteId;
        Tipo = tipo;
        Descrizione = Guard.Facoltativo(descrizione);
        ImportoMensile = importoMensile;
        Residuo = residuo;
        Dal = dal;
        Al = al;
    }

    public string Titolo => Tipo == TipoTrattenuta.Altro && Descrizione is { } d ? d : Tipo.Descrizione();
    public bool AttivaIl(DateOnly giorno) => giorno >= Dal && (Al is null || giorno <= Al);
}
