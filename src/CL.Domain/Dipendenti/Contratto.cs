using CL.Domain.Common;

namespace CL.Domain.Dipendenti;

public static class TipoContrattoExtensions
{
    public static string Descrizione(this TipoContratto tipo) => tipo switch
    {
        TipoContratto.TempoIndeterminato => "Tempo indeterminato",
        TipoContratto.TempoDeterminato => "Tempo determinato",
        TipoContratto.Apprendistato => "Apprendistato",
        TipoContratto.Intermittente => "Intermittente",
        TipoContratto.Somministrazione => "Somministrazione",
        _ => tipo.ToString(),
    };
}

public enum TipoContratto
{
    TempoIndeterminato,
    TempoDeterminato,
    Apprendistato,
    Intermittente,
    Somministrazione,
}

/// <summary>
/// Rapporto di lavoro con le condizioni economiche e normative.
/// Le spettanze annue di ferie/ROL/ex-festività sono in ore riferite al full-time, come da CCNL;
/// per i part-time vengono riproporzionate con <see cref="SpettanzaAnnua"/>.
/// </summary>
public sealed class Contratto : Entity
{
    public Guid DipendenteId { get; private set; }
    public TipoContratto Tipo { get; private set; }
    public string Ccnl { get; private set; } = null!;
    public string Livello { get; private set; } = null!;
    public string? Mansione { get; private set; }
    public DateOnly DataInizio { get; private set; }
    public DateOnly? DataFine { get; private set; }

    /// <summary>Orario settimanale del full-time da CCNL (es. 40).</summary>
    public decimal OreSettimanaliFullTime { get; private set; }
    /// <summary>Percentuale part-time (100 = full-time).</summary>
    public decimal PercentualePartTime { get; private set; }
    /// <summary>Giorni lavorativi a settimana (5 o 6).</summary>
    public int GiorniLavorativiSettimana { get; private set; }

    public decimal RetribuzioneLordaAnnua { get; private set; }
    public int MensilitaAnnue { get; private set; }

    public decimal OreFerieAnnue { get; private set; }
    public decimal OreRolAnnue { get; private set; }
    public decimal OreExFestivitaAnnue { get; private set; }

    private Contratto() { } // EF Core

    internal Contratto(Guid dipendenteId, TipoContratto tipo, string ccnl, string livello,
        DateOnly dataInizio, DateOnly? dataFine, decimal oreSettimanaliFullTime, decimal percentualePartTime,
        int giorniLavorativiSettimana, decimal retribuzioneLordaAnnua, int mensilitaAnnue,
        decimal oreFerieAnnue, decimal oreRolAnnue, decimal oreExFestivitaAnnue, string? mansione)
    {
        if (tipo == TipoContratto.TempoDeterminato && dataFine is null)
            throw new DomainException("Un contratto a tempo determinato richiede una data di fine.");
        if (dataFine is { } f && f < dataInizio)
            throw new DomainException("La data di fine contratto precede la data di inizio.");
        if (oreSettimanaliFullTime is <= 0 or > 48)
            throw new DomainException("L'orario settimanale deve essere compreso tra 0 e 48 ore.");
        if (percentualePartTime is <= 0 or > 100)
            throw new DomainException("La percentuale part-time deve essere compresa tra 1 e 100.");
        if (giorniLavorativiSettimana is not (5 or 6))
            throw new DomainException("I giorni lavorativi settimanali devono essere 5 o 6.");
        if (mensilitaAnnue is < 12 or > 16)
            throw new DomainException("Le mensilità annue devono essere comprese tra 12 e 16.");

        DipendenteId = dipendenteId;
        Tipo = tipo;
        Ccnl = Guard.NotEmpty(ccnl, "CCNL");
        Livello = Guard.NotEmpty(livello, "Livello");
        Mansione = mansione?.Trim();
        DataInizio = dataInizio;
        DataFine = dataFine;
        OreSettimanaliFullTime = oreSettimanaliFullTime;
        PercentualePartTime = percentualePartTime;
        GiorniLavorativiSettimana = giorniLavorativiSettimana;
        RetribuzioneLordaAnnua = Guard.NonNegative(retribuzioneLordaAnnua, "Retribuzione lorda annua");
        MensilitaAnnue = mensilitaAnnue;
        OreFerieAnnue = Guard.NonNegative(oreFerieAnnue, "Ore ferie annue");
        OreRolAnnue = Guard.NonNegative(oreRolAnnue, "Ore ROL annue");
        OreExFestivitaAnnue = Guard.NonNegative(oreExFestivitaAnnue, "Ore ex-festività annue");
    }

    public decimal OreSettimanali => Math.Round(OreSettimanaliFullTime * PercentualePartTime / 100m, 2);

    /// <summary>Ore di una giornata lavorativa ordinaria (part-time orizzontale).</summary>
    public decimal OreGiornaliere => Math.Round(OreSettimanali / GiorniLavorativiSettimana, 2);

    public decimal RetribuzioneMensileLorda => Math.Round(RetribuzioneLordaAnnua / MensilitaAnnue, 2);

    /// <summary>Spettanza annua effettiva in ore, riproporzionata al part-time.</summary>
    public decimal SpettanzaAnnua(Assenze.TipoAssenza tipo) => Math.Round(tipo switch
    {
        Assenze.TipoAssenza.Ferie => OreFerieAnnue,
        Assenze.TipoAssenza.Rol => OreRolAnnue,
        Assenze.TipoAssenza.ExFestivita => OreExFestivitaAnnue,
        _ => 0m,
    } * PercentualePartTime / 100m, 2);

    public bool AttivoIl(DateOnly giorno) => giorno >= DataInizio && (DataFine is null || giorno <= DataFine);

    internal void Chiudi(DateOnly dataFine)
    {
        if (dataFine < DataInizio)
            throw new DomainException("La data di chiusura precede l'inizio del contratto.");
        DataFine = dataFine;
    }
}
