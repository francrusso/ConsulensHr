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

    public static decimal MaturatoNelMese(Dipendente dipendente, TipoAssenza tipo, int anno, int mese)
    {
        var inizio = new DateOnly(anno, mese, 1);
        var fine = inizio.AddMonths(1).AddDays(-1);

        // Il contratto di riferimento è quello con più giorni nel mese.
        var (contratto, _) = dipendente.Contratti
            .Select(c => (c, giorni: GiorniAttivi(c, inizio, fine)))
            .OrderByDescending(x => x.giorni)
            .FirstOrDefault();

        if (contratto is null) return 0m;
        var giorniTotali = dipendente.Contratti.Sum(c => GiorniAttivi(c, inizio, fine));
        return giorniTotali >= GiorniMinimiPerMaturazione ? contratto.SpettanzaAnnua(tipo) / 12m : 0m;
    }

    private static int GiorniAttivi(Contratto c, DateOnly inizio, DateOnly fine)
    {
        var da = c.DataInizio > inizio ? c.DataInizio : inizio;
        var a = c.DataFine is { } f && f < fine ? f : fine;
        return a < da ? 0 : a.DayNumber - da.DayNumber + 1;
    }
}
