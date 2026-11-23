using CL.Domain.Common;

namespace CL.Domain.Assenze;

/// <summary>
/// Chiusura dell'azienda (es. ferie estive, ponte di Natale): per chi è in forza vale come ferie,
/// registrate e approvate in automatico e collegate alla chiusura.
/// </summary>
public sealed class ChiusuraAziendale : Entity
{
    public Guid AziendaId { get; private set; }
    public DateOnly Dal { get; private set; }
    public DateOnly Al { get; private set; }
    public string Descrizione { get; private set; } = null!;
    public DateTime CreataIl { get; private set; }
    public string CreataDa { get; private set; } = null!;

    private ChiusuraAziendale() { } // EF Core

    public ChiusuraAziendale(Guid aziendaId, DateOnly dal, DateOnly al, string? descrizione, string da, DateTime adesso)
    {
        if (aziendaId == Guid.Empty) throw new DomainException("Azienda obbligatoria.");
        if (al < dal) throw new DomainException("La data di fine precede la data di inizio.");
        if (dal.Year != al.Year) throw new DomainException("Una chiusura a cavallo d'anno va divisa in due (una per anno).");
        if (al.DayNumber - dal.DayNumber > 62) throw new DomainException("Una chiusura può durare al massimo due mesi.");
        var d = Guard.Facoltativo(descrizione) ?? "Chiusura aziendale";
        if (d.Length > 150) throw new DomainException("Descrizione troppo lunga (massimo 150 caratteri).");
        AziendaId = aziendaId;
        Dal = dal;
        Al = al;
        Descrizione = d;
        CreataDa = Guard.NotEmpty(da, "Autore");
        CreataIl = adesso;
    }
}
