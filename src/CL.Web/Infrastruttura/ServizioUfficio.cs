using CL.Domain.Dipendenti;
using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

/// <summary>Chi lavora in smart working o in modalità ibrida segna i giorni in cui va in ufficio.</summary>
public sealed class ServizioUfficio(AppDbContext db, UtenteCorrente utente, TimeProvider clock)
{
    private bool? _puo;

    /// <summary>Il dipendente collegato può segnare i giorni in ufficio (contratto ibrido o full-remote, o sede in smart working).</summary>
    public async Task<bool> PuoSegnareAsync()
    {
        if (_puo is { } p) return p;
        if (!utente.IsDipendente || utente.DipendenteId is not { } id) return (_puo = false).Value;
        var d = await db.Dipendenti.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        _puo = d is not null && LavoraADistanza(d, DateOnly.FromDateTime(clock.GetLocalNow().DateTime));
        return _puo.Value;
    }

    public static bool LavoraADistanza(Dipendente d, DateOnly oggi)
    {
        var c = d.ContrattoAttivo(oggi) ?? d.Contratti.Where(x => x.DataInizio > oggi).OrderBy(x => x.DataInizio).FirstOrDefault();
        return d.Sede == SedeLavoro.SmartWorking || c?.Modalita is ModalitaLavoro.Ibrido or ModalitaLavoro.FullRemote;
    }
}
