using CL.Domain.Common;

namespace CL.Domain.Presenze;

/// <summary>
/// Giorno in cui una persona in smart working o in lavoro ibrido va in ufficio. Lo segna la persona stessa,
/// senza approvazione: serve all'azienda per sapere chi c'è in sede.
/// </summary>
public sealed class GiornoInUfficio : Entity
{
    public Guid DipendenteId { get; private set; }
    public DateOnly Giorno { get; private set; }
    public DateTime SegnatoIl { get; private set; }

    private GiornoInUfficio() { } // EF Core

    public GiornoInUfficio(Guid dipendenteId, DateOnly giorno, DateTime adesso)
    {
        if (dipendenteId == Guid.Empty) throw new DomainException("Dipendente obbligatorio.");
        DipendenteId = dipendenteId;
        Giorno = giorno;
        SegnatoIl = adesso;
    }
}
