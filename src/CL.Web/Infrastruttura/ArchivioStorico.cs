using System.Linq.Expressions;
using CL.Domain.Aziende;
using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

/// <summary>
/// L'archivio storico raccoglie i documenti degli anni precedenti al subentro (CU, 770, UniEmens, F24…): serve solo alle
/// aziende che arrivano da un altro consulente (tipo di avvio "Cambio consulente", "Attività già operativa" nel profilo).
/// </summary>
public sealed class ArchivioStorico(AppDbContext db, UtenteCorrente utente)
{
    public static readonly Expression<Func<Azienda, bool>> Interessata = a => a.TipoAvvio == TipoAvvio.CambioConsulente;

    private bool? _visibile;

    /// <summary>
    /// Voce di menù e pagine: il referente se la sua azienda arriva da un altro consulente, lo studio se ne segue almeno
    /// una (nel suo perimetro); mai il dipendente.
    /// </summary>
    public async Task<bool> VisibileAsync()
    {
        _visibile ??= utente.IsAzienda ? await db.Aziende.Where(a => a.Id == utente.AziendaId).AnyAsync(Interessata)
            : utente.IsConsulente && await db.Aziende.AnyAsync(Interessata);
        return _visibile.Value;
    }

    public Task<bool> PerAziendaAsync(Guid aziendaId) => db.Aziende.Where(a => a.Id == aziendaId).AnyAsync(Interessata);
}
