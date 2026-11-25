using CL.Infrastructure.Archivio;
using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

/// <summary>
/// Elimina per sempre un'azienda con tutti i suoi dati: personale (contratti, richieste, presenze, turni, saldi, profili),
/// buste paga, CU, documenti, archivio storico, team, chiusure, profilo e gli accessi di referenti e dipendenti (con le
/// loro notifiche). Si può solo per un'azienda già disattivata e scrivendo la ragione sociale come conferma.
/// Il database si aggiorna in un'unica transazione; i file nell'archivio si cancellano dopo, a eliminazione riuscita.
/// </summary>
public sealed class EliminazioneAzienda(AppDbContext db, IArchivioDocumenti archivio, ILogger<EliminazioneAzienda> logger)
{
    /// <summary>Restituisce l'errore da mostrare, o null se l'azienda è stata eliminata.</summary>
    public async Task<string?> EliminaAsync(Guid aziendaId, string? conferma)
    {
        var azienda = await db.Aziende.AsNoTracking().FirstOrDefaultAsync(a => a.Id == aziendaId);
        if (azienda is null) return "Azienda non trovata.";
        if (azienda.Attiva) return "Prima disattiva l'azienda: si possono eliminare solo le aziende disattivate.";
        if (!string.Equals((conferma ?? "").Trim(), azienda.RagioneSociale.Trim(), StringComparison.CurrentCultureIgnoreCase))
            return "Per confermare scrivi esattamente la ragione sociale dell'azienda.";

        var persone = await db.Dipendenti.AsNoTracking().IgnoreAutoIncludes().Where(d => d.AziendaId == aziendaId)
            .Select(d => new { d.Id, d.FotoChiave }).ToListAsync();
        var ids = persone.Select(p => p.Id).ToList();

        // I file dell'archivio, da togliere solo quando il database ha confermato l'eliminazione.
        var file = new List<string?> { azienda.LogoChiave };
        file.AddRange(persone.Select(p => p.FotoChiave));
        file.AddRange(await db.BustePaga.Where(x => x.AziendaId == aziendaId).Select(x => x.ChiaveArchivio).ToListAsync());
        file.AddRange(await db.CertificazioniUniche.Where(x => x.AziendaId == aziendaId).Select(x => x.ChiaveArchivio).ToListAsync());
        file.AddRange(await db.DocumentiPersonali.Where(x => x.AziendaId == aziendaId).Select(x => x.ChiaveArchivio).ToListAsync());
        file.AddRange(await db.DocumentiStorici.Where(x => x.AziendaId == aziendaId).Select(x => x.ChiaveArchivio).ToListAsync());
        foreach (var c in await db.Contratti.AsNoTracking().Where(c => ids.Contains(c.DipendenteId)).ToListAsync())
        {
            file.Add(c.DocumentoChiave);
            file.AddRange(c.Lettere.Select(l => l.Chiave));
        }

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            // Prima ciò che blocca l'eliminazione di persone e azienda (vincoli senza cascata).
            await db.BustePaga.Where(x => x.AziendaId == aziendaId).ExecuteDeleteAsync();
            await db.CertificazioniUniche.Where(x => x.AziendaId == aziendaId).ExecuteDeleteAsync();
            await db.DocumentiPersonali.Where(x => x.AziendaId == aziendaId).ExecuteDeleteAsync();
            await db.DocumentiStorici.Where(x => x.AziendaId == aziendaId).ExecuteDeleteAsync();
            await db.TrattenuteBenefit.Where(x => x.AziendaId == aziendaId).ExecuteDeleteAsync();
            await db.ComunicazioniPresenze.Where(x => x.AziendaId == aziendaId).ExecuteDeleteAsync(); // righe in cascata
            // Accessi di referenti e persone: ruoli, notifiche e preferenze se ne vanno in cascata. I consulenti restano.
            await db.Users.Where(u => u.AziendaId == aziendaId || (u.DipendenteId != null && ids.Contains(u.DipendenteId.Value)))
                .ExecuteDeleteAsync();
            // I team si legano tra loro (team superiore): si sciolgono i legami, poi vanno via con l'azienda.
            await db.Team.Where(t => t.AziendaId == aziendaId).ExecuteUpdateAsync(s => s.SetProperty(t => t.PadreId, (Guid?)null));
            // Le persone, con contratti, richieste, giorni, straordinari, turni, saldi e profili (in cascata).
            await db.Dipendenti.Where(d => d.AziendaId == aziendaId).ExecuteDeleteAsync();
            // L'azienda, con profilo, team, chiusure, turni salvati e assegnazioni ai consulenti (in cascata).
            await db.Aziende.Where(a => a.Id == aziendaId).ExecuteDeleteAsync();
            await tx.CommitAsync();
        });

        foreach (var chiave in file.OfType<string>().Distinct())
        {
            try { await archivio.EliminaAsync(chiave); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                logger.LogWarning(ex, "File {Chiave} dell'azienda eliminata non rimosso dall'archivio", chiave);
            }
        }
        logger.LogInformation("Azienda {Azienda} ({Id}) eliminata con {Persone} persone", azienda.RagioneSociale, aziendaId, ids.Count);
        return null;
    }
}
