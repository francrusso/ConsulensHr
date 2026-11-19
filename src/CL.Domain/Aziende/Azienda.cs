using CL.Domain.Common;
using CL.Domain.ValueObjects;

namespace CL.Domain.Aziende;

/// <summary>Azienda cliente dello studio di consulenza del lavoro.</summary>
public sealed class Azienda : Entity
{
    public string RagioneSociale { get; private set; } = null!;
    public PartitaIva PartitaIva { get; private set; } = null!;
    public string? CodiceFiscale { get; private set; }
    public string? Indirizzo { get; private set; }
    public string? Comune { get; private set; }
    public string? Email { get; private set; }
    /// <summary>Data del santo patrono del comune della sede (festività locale), se applicabile.</summary>
    public int? SantoPatronoMese { get; private set; }
    public int? SantoPatronoGiorno { get; private set; }
    public bool Attiva { get; private set; } = true;

    private Azienda() { } // EF Core

    public Azienda(string ragioneSociale, PartitaIva partitaIva)
    {
        RagioneSociale = Guard.NotEmpty(ragioneSociale, "Ragione sociale");
        PartitaIva = partitaIva;
    }

    public void AggiornaAnagrafica(string ragioneSociale, string? codiceFiscale, string? indirizzo, string? comune, string? email)
    {
        RagioneSociale = Guard.NotEmpty(ragioneSociale, "Ragione sociale");
        CodiceFiscale = codiceFiscale?.Trim();
        Indirizzo = indirizzo?.Trim();
        Comune = comune?.Trim();
        Email = email?.Trim();
    }

    public void ImpostaSantoPatrono(int mese, int giorno)
    {
        try { _ = new DateOnly(2024, mese, giorno); } // 2024 bisestile: ammette il 29/02
        catch (ArgumentOutOfRangeException) { throw new DomainException("Data del santo patrono non valida."); }
        SantoPatronoMese = mese;
        SantoPatronoGiorno = giorno;
    }

    public void RimuoviSantoPatrono()
    {
        SantoPatronoMese = null;
        SantoPatronoGiorno = null;
    }

    public void Disattiva() => Attiva = false;
    public void Riattiva() => Attiva = true;
}
