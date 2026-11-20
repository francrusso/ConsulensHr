using System.Net;
using System.Text;
using CL.Infrastructure.Email;
using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

/// <summary>Esito di un invito: il link va mostrato a chi invita solo se l'email non è partita.</summary>
public sealed record EsitoInvito(bool Riuscito, string? Errore, string? Link, bool EmailInviata)
{
    public static EsitoInvito Fallito(string errore) => new(false, errore, null, false);
}

public enum StatoAccesso { InvitoInAttesa, Attivo, Disattivato }

/// <summary>Riga di elenco accessi. Proprietà init (non costruttore) così EF può filtrare e ordinare dopo la proiezione.</summary>
public sealed record UtenteRiga
{
    public required string Id { get; init; }
    public required string Email { get; init; }
    public required string Nome { get; init; }
    public required string Ruolo { get; init; }
    public Guid? AziendaId { get; init; }
    public string? Azienda { get; init; }
    public Guid? DipendenteId { get; init; }
    public StatoAccesso Stato { get; init; }
}

/// <summary>
/// Crea gli accessi al portale (consulenti, referenti azienda, dipendenti) e gestisce inviti e attivazione.
/// L'utente invitato non ha password: la sceglie dal link, che vale come conferma dell'email.
/// </summary>
public sealed class ServizioAccessi(
    UserManager<ApplicationUser> users,
    AppDbContext db,
    IInvioEmail email,
    LinkGenerator link,
    IHttpContextAccessor http,
    IConfiguration config)
{
    public static StatoAccesso Stato(ApplicationUser u) =>
        !u.Attivo ? StatoAccesso.Disattivato : u.EmailConfirmed ? StatoAccesso.Attivo : StatoAccesso.InvitoInAttesa;

    /// <summary>Utenti con ruolo e azienda, in una sola query.</summary>
    public IQueryable<UtenteRiga> Elenco() =>
        from u in db.Users
        join ur in db.UserRoles on u.Id equals ur.UserId
        join r in db.Roles on ur.RoleId equals r.Id
        from a in db.Aziende.Where(a => a.Id == u.AziendaId).DefaultIfEmpty()
        select new UtenteRiga
        {
            Id = u.Id, Email = u.Email!, Nome = (u.Cognome + " " + u.Nome).Trim(), Ruolo = r.Name!,
            AziendaId = u.AziendaId, Azienda = a.RagioneSociale, DipendenteId = u.DipendenteId,
            Stato = !u.Attivo ? StatoAccesso.Disattivato : u.EmailConfirmed ? StatoAccesso.Attivo : StatoAccesso.InvitoInAttesa,
        };

    public async Task<EsitoInvito> InvitaAsync(string indirizzo, string nome, string cognome, string ruolo,
        Guid? aziendaId = null, Guid? dipendenteId = null)
    {
        indirizzo = indirizzo.Trim();
        if (await users.FindByEmailAsync(indirizzo) is not null)
            return EsitoInvito.Fallito($"Esiste già un accesso con l'email {indirizzo}.");

        var utente = new ApplicationUser
        {
            UserName = indirizzo, Email = indirizzo, Nome = nome.Trim(), Cognome = cognome.Trim(),
            AziendaId = aziendaId, DipendenteId = dipendenteId,
        };

        await using var tx = await db.Database.BeginTransactionAsync();
        var creato = await users.CreateAsync(utente); // senza password: la sceglierà dal link
        if (!creato.Succeeded) return EsitoInvito.Fallito(string.Join(" ", creato.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(utente, ruolo);

        if (dipendenteId is { } d && await db.Dipendenti.FindAsync(d) is { } dip)
        {
            dip.CollegaUtente(utente.Id);
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();

        return await InviaLinkAsync(utente, invito: true);
    }

    /// <summary>Nuovo link di invito (se l'utente non ha ancora scelto la password) o di reimpostazione.</summary>
    public Task<EsitoInvito> InviaLinkAsync(ApplicationUser utente) => InviaLinkAsync(utente, invito: !utente.EmailConfirmed);

    private async Task<EsitoInvito> InviaLinkAsync(ApplicationUser utente, bool invito)
    {
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(utente)));
        var url = ComponiUrl(link.GetPathByAction("ImpostaPassword", "Account", new { uid = utente.Id, token })
                             ?? throw new InvalidOperationException("Impossibile comporre il link."));

        var nome = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(utente.Nome) ? utente.Email : utente.Nome);
        var href = WebUtility.HtmlEncode(url);
        var (oggetto, testo) = invito
            ? ("Il tuo accesso a Consulens HR",
               "lo studio ti ha creato un accesso a Consulens HR, dove puoi consultare i tuoi dati, le ferie e i permessi.<br>Per attivarlo scegli la tua password")
            : ("Reimposta la password di Consulens HR",
               "abbiamo ricevuto una richiesta di reimpostazione della password. Se non l'hai fatta tu puoi ignorare questa email.<br>Per scegliere una nuova password");

        var html = $"""
            <p>Ciao {nome},</p>
            <p>{testo} da questo link (valido 3 giorni):</p>
            <p><a href="{href}">{href}</a></p>
            <p>Consulens HR</p>
            """;
        var inviata = await email.InviaAsync(utente.Email!, oggetto, html);
        return new EsitoInvito(true, null, url, inviata);
    }

    /// <summary>
    /// In produzione "App:UrlPubblico" fissa l'indirizzo dei link: non ci si fida dell'header Host della richiesta,
    /// che chi chiede il recupero password potrebbe falsificare per far puntare il link a un altro sito.
    /// </summary>
    public string ComponiUrl(string percorso)
    {
        if (config["App:UrlPubblico"] is { Length: > 0 } baseUrl) return baseUrl.TrimEnd('/') + percorso;
        var req = http.HttpContext?.Request ?? throw new InvalidOperationException("Serve una richiesta HTTP per comporre il link.");
        return $"{req.Scheme}://{req.Host}{req.PathBase}{percorso}";
    }

    public async Task<bool> ImpostaAttivoAsync(ApplicationUser utente, bool attivo)
    {
        utente.Attivo = attivo;
        var esito = await users.UpdateAsync(utente);
        // Nuovo security stamp: le sessioni aperte dell'utente disattivato decadono alla prossima verifica.
        if (esito.Succeeded && !attivo) await users.UpdateSecurityStampAsync(utente);
        return esito.Succeeded;
    }
}
