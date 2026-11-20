using System.Linq.Expressions;
using CL.Domain.Dipendenti;

namespace CL.Web.Infrastruttura;

/// <summary>Condizioni riusabili, tradotte in SQL da EF (stessa regola di <see cref="Contratto.AttivoIl"/>).</summary>
public static class Filtri
{
    public static Expression<Func<Contratto, bool>> ContrattoAttivoIl(DateOnly giorno) =>
        c => c.DataInizio <= giorno && (c.DataFine == null || c.DataFine >= giorno);

    public static Expression<Func<Dipendente, bool>> InForzaIl(DateOnly giorno)
    {
        var attivo = ContrattoAttivoIl(giorno);
        return d => d.Contratti.AsQueryable().Any(attivo);
    }

    /// <summary>Intervallo dell'anno solare: a differenza di <c>Dal.Year == anno</c> sfrutta l'indice su (DipendenteId, Dal, Al).</summary>
    public static (DateOnly Inizio, DateOnly Fine) Anno(int anno) => (new DateOnly(anno, 1, 1), new DateOnly(anno, 12, 31));
}
