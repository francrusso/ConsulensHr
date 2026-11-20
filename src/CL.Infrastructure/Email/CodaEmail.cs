using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CL.Infrastructure.Email;

public sealed record MessaggioEmail(string Destinatario, string Oggetto, string Html);

/// <summary>
/// Email da inviare in background (es. avvisi di pubblicazione dei cedolini): la richiesta web non aspetta l'SMTP.
/// In memoria: se l'app si ferma prima dell'invio il messaggio si perde, accettabile per semplici avvisi.
/// </summary>
public sealed class CodaEmail
{
    private readonly Channel<MessaggioEmail> _canale = Channel.CreateUnbounded<MessaggioEmail>(new UnboundedChannelOptions { SingleReader = true });

    public void Accoda(MessaggioEmail messaggio) => _canale.Writer.TryWrite(messaggio);

    internal ChannelReader<MessaggioEmail> Lettore => _canale.Reader;
}

internal sealed class InvioCodaEmail(CodaEmail coda, IInvioEmail email, ILogger<InvioCodaEmail> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var m in coda.Lettore.ReadAllAsync(stoppingToken))
            {
                if (!email.Configurato) continue; // senza SMTP gli avvisi non partono (il portale resta consultabile)
                if (!await email.InviaAsync(m.Destinatario, m.Oggetto, m.Html, stoppingToken))
                    logger.LogWarning("Avviso \"{Oggetto}\" per {Destinatario} non inviato", m.Oggetto, m.Destinatario);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
