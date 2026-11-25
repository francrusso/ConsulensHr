using System.Linq.Expressions;
using CL.Domain.Dipendenti;

namespace CL.Web.Infrastruttura;

/// <summary>Condizioni riusabili, tradotte in SQL da EF (stessa regola di <see cref="Contratto.AttivoIl"/>).</summary>
public static class Filtri
{
    /// <summary>
    /// Contratto in vigore in almeno un giorno tra <paramref name="inizio"/> e <paramref name="fine"/>, estremi inclusi
    /// (senza data di fine è aperto). Su dati già in memoria si usa compilata: <c>ContrattoAttivoNelPeriodo(...).Compile()</c>.
    /// </summary>
    public static Expression<Func<Contratto, bool>> ContrattoAttivoNelPeriodo(DateOnly inizio, DateOnly fine) =>
        c => c.DataInizio <= fine && (c.DataFine == null || c.DataFine >= inizio);

    public static Expression<Func<Contratto, bool>> ContrattoAttivoIl(DateOnly giorno) => ContrattoAttivoNelPeriodo(giorno, giorno);

    /// <summary>Intervallo dell'anno solare: a differenza di <c>Dal.Year == anno</c> sfrutta l'indice su (DipendenteId, Dal, Al).</summary>
    public static (DateOnly Inizio, DateOnly Fine) Anno(int anno) => (new DateOnly(anno, 1, 1), new DateOnly(anno, 12, 31));
}
