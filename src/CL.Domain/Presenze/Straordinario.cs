using CL.Domain.Assenze;
using CL.Domain.Common;

namespace CL.Domain.Presenze;

/// <summary>
/// Ore di straordinario fatte (o da fare) in un giorno, segnalate dal dipendente o registrate dall'azienda.
/// Approvate dall'azienda, finiscono da sole nel riepilogo presenze del mese.
/// </summary>
public sealed class Straordinario : Entity
{
    public const decimal OreMassime = 12;

    public Guid DipendenteId { get; private set; }
    public DateOnly Giorno { get; private set; }
    public decimal Ore { get; private set; }
    public string? Note { get; private set; }
    public StatoRichiesta Stato { get; private set; }
    public DateTime RichiestoIl { get; private set; }
    public string? DecisoDa { get; private set; }
    public DateTime? DecisoIl { get; private set; }
    public string? MotivoRifiuto { get; private set; }

    private Straordinario() { } // EF Core

    public Straordinario(Guid dipendenteId, DateOnly giorno, decimal ore, string? note, DateTime adesso)
    {
        if (dipendenteId == Guid.Empty) throw new DomainException("Dipendente obbligatorio.");
        if (ore <= 0 || ore > OreMassime) throw new DomainException($"Le ore di straordinario vanno da 0,5 a {OreMassime:0} al giorno.");
        if (ore * 2 != Math.Floor(ore * 2)) throw new DomainException("Le ore di straordinario si indicano a mezz'ore (es. 1,5).");
        var n = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (n is { Length: > 500 }) throw new DomainException("Note troppo lunghe (massimo 500 caratteri).");
        DipendenteId = dipendenteId;
        Giorno = giorno;
        Ore = ore;
        Note = n;
        Stato = StatoRichiesta.InAttesa;
        RichiestoIl = adesso;
    }

    /// <summary>Straordinario festivo: domenica o festività (nazionale o santo patrono).</summary>
    public bool Festivo(int? patronoMese, int? patronoGiorno) =>
        Giorno.DayOfWeek == DayOfWeek.Sunday || CalendarioLavorativo.IsFestivo(Giorno, patronoMese, patronoGiorno);

    public void Approva(string approvatore, DateTime adesso)
    {
        if (Stato != StatoRichiesta.InAttesa) throw new DomainException("Solo le richieste in attesa possono essere approvate.");
        Stato = StatoRichiesta.Approvata;
        DecisoDa = Guard.NotEmpty(approvatore, "Approvatore");
        DecisoIl = adesso;
    }

    /// <summary>L'azienda revoca lo straordinario (anche approvato): esce dal riepilogo presenze.</summary>
    public void Revoca()
    {
        if (Stato is not (StatoRichiesta.InAttesa or StatoRichiesta.Approvata)) throw new DomainException("La richiesta non è più attiva.");
        Stato = StatoRichiesta.Annullata;
    }

    public void Rifiuta(string approvatore, string? motivo, DateTime adesso)
    {
        if (Stato != StatoRichiesta.InAttesa) throw new DomainException("Solo le richieste in attesa possono essere rifiutate.");
        Stato = StatoRichiesta.Rifiutata;
        DecisoDa = Guard.NotEmpty(approvatore, "Approvatore");
        MotivoRifiuto = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        DecisoIl = adesso;
    }
}
