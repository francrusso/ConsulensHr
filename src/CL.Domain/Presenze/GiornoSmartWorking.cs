using CL.Domain.Common;

namespace CL.Domain.Presenze;

public enum StatoSmartWorking { InAttesa, Approvato, Rifiutato }

public static class StatoSmartWorkingExtensions
{
    public static string Descrizione(this StatoSmartWorking s) => s switch
    {
        StatoSmartWorking.InAttesa => "In attesa",
        StatoSmartWorking.Approvato => "Approvato",
        StatoSmartWorking.Rifiutato => "Rifiutato",
        _ => s.ToString(),
    };
}

/// <summary>
/// Giorno di smart working chiesto da chi lavora in presenza. A differenza dei giorni in ufficio di chi lavora
/// a distanza, va approvato dall'azienda.
/// </summary>
public sealed class GiornoSmartWorking : Entity
{
    public Guid DipendenteId { get; private set; }
    public DateOnly Giorno { get; private set; }
    public StatoSmartWorking Stato { get; private set; }
    public DateTime RichiestoIl { get; private set; }
    public string? DecisoDa { get; private set; }
    public DateTime? DecisoIl { get; private set; }
    public string? MotivoRifiuto { get; private set; }

    private GiornoSmartWorking() { } // EF Core

    public GiornoSmartWorking(Guid dipendenteId, DateOnly giorno, DateTime adesso)
    {
        if (dipendenteId == Guid.Empty) throw new DomainException("Dipendente obbligatorio.");
        DipendenteId = dipendenteId;
        Giorno = giorno;
        Stato = StatoSmartWorking.InAttesa;
        RichiestoIl = adesso;
    }

    public void Approva(string approvatore, DateTime adesso)
    {
        if (Stato != StatoSmartWorking.InAttesa) throw new DomainException("Solo le richieste in attesa possono essere approvate.");
        Stato = StatoSmartWorking.Approvato;
        DecisoDa = Guard.NotEmpty(approvatore, "Approvatore");
        DecisoIl = adesso;
    }

    public void Rifiuta(string approvatore, string? motivo, DateTime adesso)
    {
        if (Stato != StatoSmartWorking.InAttesa) throw new DomainException("Solo le richieste in attesa possono essere rifiutate.");
        Stato = StatoSmartWorking.Rifiutato;
        DecisoDa = Guard.NotEmpty(approvatore, "Approvatore");
        MotivoRifiuto = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        DecisoIl = adesso;
    }
}
