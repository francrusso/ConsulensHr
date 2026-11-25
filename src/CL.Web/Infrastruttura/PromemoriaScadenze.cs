using CL.Domain.Dipendenti;
using CL.Domain.Documenti;
using CL.Domain.Notifiche;
using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

/// <summary>
/// Promemoria delle scadenze, a studio e referenti dell'azienda: contratti a termine che finiscono entro 30 giorni senza
/// un contratto successivo (né una cessazione già registrata) e documenti personali che scadono entro 30 giorni (anche
/// alla persona). Ogni scadenza si notifica una volta sola: se la data cambia (es. una proroga) arriva un nuovo promemoria.
/// </summary>
public sealed class PromemoriaScadenze(AppDbContext db, ServizioNotifiche notifiche, TimeProvider clock)
{
    public const int GiorniPreavviso = 30;

    private readonly Dictionary<Guid, List<Destinatario>> _gestori = [];

    /// <summary>Manda i promemoria non ancora inviati; restituisce quante persone sono state avvisate.</summary>
    public async Task<int> ControllaAsync()
    {
        var oggi = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var soglia = oggi.AddDays(GiorniPreavviso);
        return await ContrattiAsync(oggi, soglia) + await DocumentiAsync(oggi, soglia);
    }

    private async Task<int> ContrattiAsync(DateOnly oggi, DateOnly soglia)
    {
        var dipendenti = await db.Dipendenti.AsNoTracking()
            .Where(d => d.Contratti.Any(c => c.DataFine != null && c.DataFine >= oggi && c.DataFine <= soglia && c.DataCessazione == null))
            .ToListAsync();
        var messaggi = new List<MessaggioNotifica>();
        foreach (var d in dipendenti)
            foreach (var c in d.Contratti.Where(c => c.DataFine is { } f && f >= oggi && f <= soglia && c.DataCessazione is null
                                                     && !d.Contratti.Any(x => x.DataInizio > f)))
            {
                var fine = c.DataFine!.Value;
                var giorni = fine.DayNumber - oggi.DayNumber;
                var quando = giorni switch { 0 => "oggi", 1 => "domani", _ => $"tra {giorni} giorni" };
                messaggi.AddRange((await GestoriAsync(d.AziendaId)).Select(g => new MessaggioNotifica(g,
                    $"Contratto in scadenza: {d.NomeCompleto}",
                    $"Il contratto ({c.Tipo.Descrizione().ToLower()}) di {d.NomeCompleto} finisce il {fine:dd/MM/yyyy}, {quando}: " +
                    "valuta proroga, trasformazione o cessazione.",
                    $"/Dipendenti/Dettaglio/{d.Id}?sezione=contratto",
                    $"contratto-scadenza:{c.Id}:{fine:yyyyMMdd}")));
            }
        return await notifiche.InviaAsync(TipoNotifica.ContrattoInScadenza, messaggi);
    }

    private async Task<int> DocumentiAsync(DateOnly oggi, DateOnly soglia)
    {
        // Solo per chi è ancora in forza (o sta per iniziare): il documento di chi ha cessato non serve più.
        var documenti = await (
            from x in db.DocumentiPersonali.AsNoTracking()
            join d in db.Dipendenti.AsNoTracking().IgnoreAutoIncludes() on x.DipendenteId equals d.Id
            where x.Scadenza != null && x.Scadenza >= oggi && x.Scadenza <= soglia
                  && db.Contratti.Any(c => c.DipendenteId == d.Id && (c.DataFine == null || c.DataFine >= oggi))
            select new { Documento = x, d.Id, d.Nome, d.Cognome, d.AziendaId }).ToListAsync();
        var persone = (await db.PersoneAsync(documenti.Select(x => x.Id))).ToLookup(p => p.DipendenteId);
        var messaggi = new List<MessaggioNotifica>();
        foreach (var x in documenti)
        {
            var (doc, nome) = (x.Documento, $"{x.Cognome} {x.Nome}");
            var scadenza = doc.Scadenza!.Value;
            var chiave = $"documento-scadenza:{doc.Id}:{scadenza:yyyyMMdd}";
            messaggi.AddRange(persone[x.Id].Select(p => new MessaggioNotifica(p, $"Documento in scadenza: {doc.Titolo}",
                $"Il tuo documento «{doc.Titolo}» scade il {scadenza:dd/MM/yyyy}: carica quello rinnovato tra i tuoi documenti.",
                "/Documenti/Personali", chiave)));
            messaggi.AddRange((await GestoriAsync(x.AziendaId)).Select(g => new MessaggioNotifica(g, $"Documento in scadenza: {nome}",
                $"{doc.Titolo} di {nome} scade il {scadenza:dd/MM/yyyy}.", "/Documenti/Personali?stato=in-scadenza", chiave)));
        }
        return await notifiche.InviaAsync(TipoNotifica.DocumentoInScadenza, messaggi);
    }

    /// <summary>Studio e referenti di un'azienda (una query per azienda per controllo).</summary>
    private async Task<List<Destinatario>> GestoriAsync(Guid aziendaId)
    {
        if (!_gestori.TryGetValue(aziendaId, out var gestori))
            _gestori[aziendaId] = gestori = [.. await db.StudioAsync(aziendaId), .. await db.ReferentiAsync(aziendaId)];
        return gestori;
    }
}

/// <summary>Controlla le scadenze poco dopo l'avvio e poi ogni 6 ore (i promemoria già mandati non si ripetono).</summary>
internal sealed class ControlloScadenze(IServiceScopeFactory scopes, ILogger<ControlloScadenze> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // lascia finire avvio, migrazioni e utenti iniziali
            using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
            do
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<PromemoriaScadenze>().ControllaAsync();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Controllo delle scadenze non riuscito");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
