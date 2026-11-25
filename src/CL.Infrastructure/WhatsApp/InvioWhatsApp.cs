using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CL.Infrastructure.WhatsApp;

/// <summary>
/// WhatsApp Cloud API di Meta (sezione "WhatsApp"): il numero Consulens registrato sulla piattaforma WhatsApp Business.
/// Phone Number ID e token (utente di sistema, permanente) vanno negli user-secrets o nelle variabili d'ambiente.
/// I messaggi che partono dal portale usano template approvati in WhatsApp Manager (vedi docs/sviluppo.md).
/// Senza Phone Number ID e token il canale non è disponibile e nel portale non si collega nessun numero.
/// </summary>
public sealed class OpzioniWhatsApp
{
    public string? PhoneNumberId { get; set; }
    public string? Token { get; set; }
    /// <summary>Il numero Consulens da cui arrivano i messaggi, mostrato nel portale (non serve per inviare).</summary>
    public string? NumeroMittente { get; set; }
    public string VersioneApi { get; set; } = "v23.0";
    /// <summary>Lingua dei template, come registrata in WhatsApp Manager.</summary>
    public string Lingua { get; set; } = "it";
    /// <summary>Template "utilità" delle notifiche: corpo con nome, titolo e testo, pulsante con link alla notifica.</summary>
    public string TemplateNotifica { get; set; } = "notifica_consulens_hr";
    /// <summary>Template "autenticazione" del codice che conferma il numero.</summary>
    public string TemplateCodice { get; set; } = "codice_verifica_consulens_hr";

    public bool Configurata => !string.IsNullOrWhiteSpace(PhoneNumberId) && !string.IsNullOrWhiteSpace(Token);
}

public interface IInvioWhatsApp
{
    bool Configurato { get; }

    /// <summary>Notifica con il template delle notifiche; <paramref name="idNotifica"/> completa il link del pulsante.</summary>
    Task<bool> InviaNotificaAsync(string numero, string nome, string titolo, string testo, string idNotifica, CancellationToken ct = default);

    /// <summary>Codice di verifica del numero (template di autenticazione, con pulsante "copia codice").</summary>
    Task<bool> InviaCodiceAsync(string numero, string codice, CancellationToken ct = default);
}

internal sealed partial class InvioWhatsAppCloud(HttpClient http, IOptions<OpzioniWhatsApp> opzioni, ILogger<InvioWhatsAppCloud> logger) : IInvioWhatsApp
{
    public const string IndirizzoServizio = "https://graph.facebook.com/";
    private readonly OpzioniWhatsApp _o = opzioni.Value;

    public bool Configurato => _o.Configurata;

    public Task<bool> InviaNotificaAsync(string numero, string nome, string titolo, string testo, string idNotifica, CancellationToken ct = default) =>
        InviaAsync(numero, _o.TemplateNotifica,
        [
            new { type = "body", parameters = new[] { Testo(nome, 60), Testo(titolo, 200), Testo(testo, 600) } },
            new { type = "button", sub_type = "url", index = "0", parameters = new[] { Testo(idNotifica, 60) } },
        ], ct);

    public Task<bool> InviaCodiceAsync(string numero, string codice, CancellationToken ct = default) =>
        InviaAsync(numero, _o.TemplateCodice,
        [
            new { type = "body", parameters = new[] { Testo(codice, 15) } },
            new { type = "button", sub_type = "url", index = "0", parameters = new[] { Testo(codice, 15) } },
        ], ct);

    private async Task<bool> InviaAsync(string numero, string template, object[] componenti, CancellationToken ct)
    {
        if (!_o.Configurata)
        {
            logger.LogWarning("WhatsApp non configurato: template {Template} non inviato", template);
            return false;
        }
        var corpo = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = numero.TrimStart('+'),
            type = "template",
            template = new { name = template, language = new { code = _o.Lingua }, components = componenti },
        };
        using var richiesta = new HttpRequestMessage(HttpMethod.Post, $"{_o.VersioneApi}/{_o.PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(corpo),
        };
        richiesta.Headers.Authorization = new("Bearer", _o.Token);
        try
        {
            using var risposta = await http.SendAsync(richiesta, ct);
            if (risposta.IsSuccessStatusCode) return true;
            // Il corpo dell'errore di Meta spiega il motivo (template non approvato, numero non su WhatsApp…), senza il destinatario.
            logger.LogWarning("WhatsApp: template {Template} non inviato ({Stato}): {Errore}",
                template, (int)risposta.StatusCode, await risposta.Content.ReadAsStringAsync(ct));
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "WhatsApp: template {Template} non inviato", template);
            return false;
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spazi();

    /// <summary>Parametro di un template: WhatsApp rifiuta a capo, tabulazioni e più spazi di fila, e ha un limite di lunghezza.</summary>
    private static object Testo(string s, int massimo)
    {
        var t = Spazi().Replace(s ?? "", " ").Trim();
        if (t.Length == 0) t = "-";
        return new { type = "text", text = t.Length <= massimo ? t : t[..(massimo - 1)] + "…" };
    }
}

/// <summary>Una notifica da mandare su WhatsApp (dati del template delle notifiche).</summary>
public sealed record NotificaWhatsApp(string Numero, string Nome, string Titolo, string Testo, string IdNotifica);

/// <summary>Notifiche WhatsApp da inviare in background, come la <see cref="Email.CodaEmail"/>: la pagina non aspetta Meta.</summary>
public sealed class CodaWhatsApp
{
    private readonly Channel<NotificaWhatsApp> _canale = Channel.CreateUnbounded<NotificaWhatsApp>(new UnboundedChannelOptions { SingleReader = true });

    public void Accoda(NotificaWhatsApp messaggio) => _canale.Writer.TryWrite(messaggio);

    internal ChannelReader<NotificaWhatsApp> Lettore => _canale.Reader;
}

internal sealed class InvioCodaWhatsApp(CodaWhatsApp coda, IServiceProvider servizi, ILogger<InvioCodaWhatsApp> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var m in coda.Lettore.ReadAllAsync(stoppingToken))
            {
                // Client tipizzato (transient): se ne prende uno nuovo per messaggio, come fa IHttpClientFactory.
                var whatsapp = servizi.GetRequiredService<IInvioWhatsApp>();
                if (!whatsapp.Configurato) continue;
                if (!await whatsapp.InviaNotificaAsync(m.Numero, m.Nome, m.Titolo, m.Testo, m.IdNotifica, stoppingToken))
                    logger.LogWarning("Notifica WhatsApp \"{Titolo}\" non inviata", m.Titolo);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
