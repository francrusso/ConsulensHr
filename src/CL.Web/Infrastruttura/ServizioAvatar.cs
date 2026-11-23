using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

/// <summary>
/// Immagini della cornice della pagina.
/// Card dell'utente in alto a destra: la foto della persona collegata, se c'è; per l'azienda (account dell'azienda o
/// referente senza foto) il logo dell'azienda; altrimenti il segnaposto.
/// Marchio in alto a sinistra: per il dipendente quello della sua azienda (sembra un portale interno), per gli altri Consulens.
/// </summary>
public sealed class ServizioAvatar(AppDbContext db, UtenteCorrente utente)
{
    public enum Origine { Foto, Logo, Segnaposto }

    /// <summary>Azienda da mostrare al posto del marchio Consulens; senza logo se ne mostra il nome.</summary>
    public sealed record MarchioAzienda(Guid Id, string Nome, bool HaLogo);

    // Calcolata una volta per richiesta (il servizio è scoped).
    private (Origine, Guid?)? _calcolato;

    public async Task<(Origine Origine, Guid? Id)> ImmagineAsync() => _calcolato ??= await CalcolaAsync();

    /// <summary>Null quando va il marchio Consulens (tutti tranne il dipendente).</summary>
    public async Task<MarchioAzienda?> MarchioAsync()
    {
        if (!utente.IsDipendente || utente.AziendaId is not { } az) return null;
        return await db.Aziende.AsNoTracking().Where(a => a.Id == az)
            .Select(a => new MarchioAzienda(a.Id, a.RagioneSociale, a.LogoChiave != null))
            .FirstOrDefaultAsync();
    }

    private async Task<(Origine, Guid?)> CalcolaAsync()
    {
        if (utente.DipendenteId is { } dip
            && await db.Dipendenti.AsNoTracking().IgnoreAutoIncludes().AnyAsync(d => d.Id == dip && d.FotoChiave != null))
            return (Origine.Foto, dip);
        if (utente.IsAzienda && utente.AziendaId is { } az && await db.Aziende.AsNoTracking().AnyAsync(a => a.Id == az && a.LogoChiave != null))
            return (Origine.Logo, az);
        return (Origine.Segnaposto, null);
    }
}
