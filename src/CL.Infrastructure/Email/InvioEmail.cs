using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CL.Infrastructure.Email;

/// <summary>
/// Configurazione SMTP (sezione "Email"). Le credenziali vanno negli user-secrets o nelle variabili d'ambiente, mai in appsettings.
/// Senza Host e Mittente l'invio è disattivato: chi invita vede il link a schermo e lo comunica da sé.
/// </summary>
public sealed class OpzioniEmail
{
    public string? Host { get; set; }
    public int Porta { get; set; } = 587;
    public bool Ssl { get; set; } = true;
    public string? Utente { get; set; }
    public string? Password { get; set; }
    public string? Mittente { get; set; }
    public string NomeMittente { get; set; } = "Consulens HR";

    /// <summary>Host e mittente presenti e, se il server chiede l'autenticazione, anche la password.</summary>
    public bool Configurata => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Mittente)
                               && (string.IsNullOrWhiteSpace(Utente) || !string.IsNullOrWhiteSpace(Password));
}

public interface IInvioEmail
{
    bool Configurato { get; }

    /// <summary>Invia un messaggio HTML; restituisce false se l'invio non è configurato o non riesce (l'errore viene registrato nel log).</summary>
    Task<bool> InviaAsync(string destinatario, string oggetto, string html, CancellationToken ct = default);
}

internal sealed class InvioEmailSmtp(IOptions<OpzioniEmail> opzioni, ILogger<InvioEmailSmtp> logger) : IInvioEmail
{
    private readonly OpzioniEmail _o = opzioni.Value;

    public bool Configurato => _o.Configurata;

    public async Task<bool> InviaAsync(string destinatario, string oggetto, string html, CancellationToken ct = default)
    {
        if (!_o.Configurata)
        {
            logger.LogWarning("Invio email non configurato: messaggio \"{Oggetto}\" per {Destinatario} non inviato", oggetto, destinatario);
            return false;
        }

        try
        {
            using var messaggio = new MailMessage
            {
                From = new MailAddress(_o.Mittente!, _o.NomeMittente),
                Subject = oggetto,
                Body = html,
                IsBodyHtml = true,
            };
            messaggio.To.Add(destinatario);

            using var smtp = new SmtpClient(_o.Host, _o.Porta) { EnableSsl = _o.Ssl };
            if (!string.IsNullOrWhiteSpace(_o.Utente))
                smtp.Credentials = new NetworkCredential(_o.Utente, _o.Password);

            await smtp.SendMailAsync(messaggio, ct);
            return true;
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
        {
            logger.LogError(ex, "Invio email \"{Oggetto}\" a {Destinatario} non riuscito", oggetto, destinatario);
            return false;
        }
    }
}
