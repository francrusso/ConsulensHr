using CL.Domain.Common;
using CL.Domain.Dipendenti;

namespace CL.Domain.Assenze;

/// <summary>Residuo dell'anno precedente (o saldo di partenza importato dal gestionale paghe).</summary>
public sealed class SaldoIniziale : Entity
{
    public Guid DipendenteId { get; private set; }
    public int Anno { get; private set; }
    public TipoAssenza Tipo { get; private set; }
    public decimal Ore { get; private set; }

    private SaldoIniziale() { } // EF Core

    public SaldoIniziale(Guid dipendenteId, int anno, TipoAssenza tipo, decimal ore)
    {
        if (!tipo.HaSaldo()) throw new DomainException($"{tipo.Descrizione()} non prevede un saldo.");
        DipendenteId = dipendenteId;
        Anno = anno;
        Tipo = tipo;
        Ore = ore; // può essere negativo (ferie godute in anticipo)
    }

    public void Rettifica(decimal ore) => Ore = ore;
}

public sealed record SaldoAssenza(
    TipoAssenza Tipo,
    int Anno,
    decimal ResiduoPrecedente,
    decimal Maturato,
    decimal Goduto,
    decimal Prenotato)
{
    /// <summary>Saldo contabile: residuo + maturato − goduto.</summary>
    public decimal Residuo => ResiduoPrecedente + Maturato - Goduto;

    /// <summary>Quanto si può ancora chiedere, al netto delle richieste in attesa.</summary>
    public decimal Disponibile => Residuo - Prenotato;
}

/// <summary>
/// Maturazione mensile in dodicesimi: il mese matura se il rapporto è attivo
/// per almeno 15 giorni di calendario (regola diffusa nei CCNL).
/// </summary>
public static class CalcoloSaldi
{
    public const int GiorniMinimiPerMaturazione = 15;

    /// <summary>
    /// Saldo di un anno fino al mese indicato (incluso). Le richieste contano nell'anno in cui iniziano
    /// (non possono stare a cavallo d'anno): approvate = godute, anche se future; in attesa = prenotate.
    /// Annullate e rifiutate non contano. Il residuo precedente è il <see cref="SaldoIniziale"/> dello stesso anno e tipo.
    /// </summary>
    public static SaldoAssenza Calcola(Dipendente dipendente, TipoAssenza tipo, int anno, int finoAlMese,
        IEnumerable<RichiestaAssenza> richieste, SaldoIniziale? iniziale = null)
    {
        if (!tipo.HaSaldo()) throw new DomainException($"{tipo.Descrizione()} non prevede un saldo.");
        if (finoAlMese is < 1 or > 12) throw new DomainException("Mese non valido.");

        var maturato = 0m;
        for (var mese = 1; mese <= finoAlMese; mese++)
            maturato += MaturatoNelMese(dipendente, tipo, anno, mese);

        var dellAnno = richieste
            .Where(r => r.DipendenteId == dipendente.Id && r.Tipo == tipo && r.Dal.Year == anno)
            .ToList();

        return new SaldoAssenza(
            tipo,
            anno,
            iniziale is { } i && i.Anno == anno && i.Tipo == tipo ? i.Ore : 0m,
            Math.Round(maturato, 2),
            dellAnno.Where(r => r.Stato == StatoRichiesta.Approvata).Sum(r => r.Ore),
            dellAnno.Where(r => r.Stato == StatoRichiesta.InAttesa).Sum(r => r.Ore));
    }

    /// <summary>
    /// Un dodicesimo della spettanza annua (già riproporzionata al part-time) se nel mese i contratti della persona,
    /// sommati, coprono almeno 15 giorni; vale la spettanza del contratto con più giorni nel mese.
    /// </summary>
    public static decimal MaturatoNelMese(Dipendente dipendente, TipoAssenza tipo, int anno, int mese)
    {
        var inizio = new DateOnly(anno, mese, 1);
        var fine = inizio.AddMonths(1).AddDays(-1);

        var giorni = dipendente.Contratti.Select(c => (Contratto: c, Giorni: GiorniAttivi(c, inizio, fine))).ToList();
        if (giorni.Sum(x => x.Giorni) < GiorniMinimiPerMaturazione) return 0m;
        return giorni.MaxBy(x => x.Giorni).Contratto.SpettanzaAnnua(tipo) / 12m;
    }

    private static int GiorniAttivi(Contratto c, DateOnly inizio, DateOnly fine)
    {
        var da = c.DataInizio > inizio ? c.DataInizio : inizio;
        var a = c.DataFine is { } f && f < fine ? f : fine;
        return a < da ? 0 : a.DayNumber - da.DayNumber + 1;
    }
}
