using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

/// <summary>
/// Immagine della card dell'utente in alto a destra: la foto della persona collegata, se c'è; per l'azienda
/// (account dell'azienda o referente senza foto) il logo dell'azienda; altrimenti il segnaposto.
/// </summary>
public sealed class ServizioAvatar(AppDbContext db, UtenteCorrente utente)
{
    public enum Origine { Foto, Logo, Segnaposto }

    private (Origine, Guid?)? _calcolato;

    public async Task<(Origine Origine, Guid? Id)> ImmagineAsync()
    {
        if (_calcolato is { } c) return c;
        if (utente.DipendenteId is { } dip
            && await db.Dipendenti.AsNoTracking().IgnoreAutoIncludes().AnyAsync(d => d.Id == dip && d.FotoChiave != null))
            return (_calcolato = (Origine.Foto, dip)).Value;
        if (utente.IsAzienda && utente.AziendaId is { } az && await db.Aziende.AsNoTracking().AnyAsync(a => a.Id == az && a.LogoChiave != null))
            return (_calcolato = (Origine.Logo, az)).Value;
        return (_calcolato = (Origine.Segnaposto, null)).Value;
    }
}
