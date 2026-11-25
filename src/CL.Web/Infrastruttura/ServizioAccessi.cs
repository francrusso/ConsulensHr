using System.ComponentModel.DataAnnotations;
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

public static class QueryUtenti
{
    /// <summary>Id degli utenti con il ruolo indicato, da usare dentro altre query (diventa una sottoquery SQL).</summary>
    public static IQueryable<string> IdConRuolo(this AppDbContext db, string ruolo) =>
        from ur in db.UserRoles
        join r in db.Roles on ur.RoleId equals r.Id
        where r.Name == ruolo
        select ur.UserId;
}

/// <summary>
/// Crea gli accessi al portale (consulenti, referenti azienda, dipendenti) e gestisce inviti e attivazione.
/// L'utente invitato non ha password: la sceglie dal link (token di reimpostazione password di Identity, valido 3 giorni),
/// che vale anche come conferma dell'email. Lo stesso link serve a chi ha dimenticato la password.
/// </summary>
public sealed class ServizioAccessi(
    UserManager<ApplicationUser> users,
    AppDbContext db,
    IInvioEmail email,
    LinkGenerator link,
    IHttpContextAccessor http,
    IConfiguration config)
{
    /// <summary>Email confermata = ha scelto la password dal link; finché non lo fa l'invito è in attesa.</summary>
    public static StatoAccesso Stato(ApplicationUser u) =>
        !u.Attivo ? StatoAccesso.Disattivato : u.EmailConfirmed ? StatoAccesso.Attivo : StatoAccesso.InvitoInAttesa;

    /// <summary>Utenti con ruolo e azienda, in una sola query.</summary>
    public IQueryable<UtenteRiga> Elenco()
    {
        var amministratori = db.IdConRuolo(Ruoli.Amministratore);
        return from u in db.Users
               join ur in db.UserRoles on u.Id equals ur.UserId
               join r in db.Roles on ur.RoleId equals r.Id
               // Il super amministratore ha anche il ruolo consulente: una sola riga, quella da amministratore.
               where r.Name != Ruoli.Consulente || !amministratori.Contains(u.Id)
               from a in db.Aziende.Where(a => a.Id == u.AziendaId).DefaultIfEmpty()
               select new UtenteRiga
               {
                   Id = u.Id, Email = u.Email!, Nome = (u.Cognome + " " + u.Nome).Trim(), Ruolo = r.Name!,
                   AziendaId = u.AziendaId, Azienda = a.RagioneSociale, DipendenteId = u.DipendenteId,
                   // Come Stato(), scritto per esteso perché EF lo traduca in SQL.
                   Stato = !u.Attivo ? StatoAccesso.Disattivato : u.EmailConfirmed ? StatoAccesso.Attivo : StatoAccesso.InvitoInAttesa,
               };
    }

    public async Task<EsitoInvito> InvitaAsync(string indirizzo, string nome, string cognome, string ruolo,
        Guid? aziendaId = null, Guid? dipendenteId = null)
    {
        indirizzo = indirizzo.Trim();
        if (await users.FindByEmailAsync(indirizzo) is not null)
            return EsitoInvito.Fallito($"Esiste già un accesso con l'email {indirizzo}.");

        // Con i nuovi tentativi automatici la transazione va dentro la strategia di esecuzione: se la connessione cade
        // a metà si riparte da capo (utente nuovo), senza lasciare accessi creati a metà.
        ApplicationUser utente = null!;
        string? errore = null;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            utente = new ApplicationUser
            {
                UserName = indirizzo, Email = indirizzo, Nome = nome.Trim(), Cognome = cognome.Trim(),
                AziendaId = aziendaId, DipendenteId = dipendenteId,
            };
            await using var tx = await db.Database.BeginTransactionAsync();
            var creato = await users.CreateAsync(utente); // senza password: la sceglierà dal link
            if (!creato.Succeeded) { errore = string.Join(" ", creato.Errors.Select(e => e.Description)); return; }
            await users.AddToRoleAsync(utente, ruolo);
            if (dipendenteId is { } d && await db.Dipendenti.FindAsync(d) is { } dip)
            {
                dip.CollegaUtente(utente.Id);
                await db.SaveChangesAsync();
            }
            await tx.CommitAsync();
        });
        if (errore is not null) return EsitoInvito.Fallito(errore);

        return await InviaLinkAsync(utente, invito: true);
    }

    /// <summary>Nuovo link di invito (se l'utente non ha ancora scelto la password) o di reimpostazione.</summary>
    public Task<EsitoInvito> InviaLinkAsync(ApplicationUser utente) => InviaLinkAsync(utente, invito: !utente.EmailConfirmed);

    private async Task<EsitoInvito> InviaLinkAsync(ApplicationUser utente, bool invito)
    {
        // Il token di Identity contiene caratteri non adatti a un URL: viaggia codificato in Base64 URL-safe.
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(utente)));
        var url = ComponiUrl(link.GetPathByAction("ImpostaPassword", "Account", new { uid = utente.Id, token })
                             ?? throw new InvalidOperationException("Impossibile comporre il link."));

        var href = WebUtility.HtmlEncode(url);
        var (oggetto, testo) = invito
            ? ("Il tuo accesso a Consulens HR",
               "il consulente ti ha creato un accesso a Consulens HR, dove puoi consultare i tuoi dati, le ferie e i permessi.<br>Per attivarlo scegli la tua password")
            : ("Reimposta la password di Consulens HR",
               "abbiamo ricevuto una richiesta di reimpostazione della password. Se non l'hai fatta tu puoi ignorare questa email.<br>Per scegliere una nuova password");

        var html = $"""
            <p>Ciao {Saluto(utente)},</p>
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

    /// <summary>Come <see cref="ComponiUrl"/>, ma null quando non si può comporre (servizio in background senza "App:UrlPubblico").</summary>
    public string? UrlAssoluto(string percorso) =>
        config["App:UrlPubblico"] is { Length: > 0 } || http.HttpContext is not null ? ComponiUrl(percorso) : null;

    /// <summary>
    /// Nuova email di accesso (è anche il nome utente). Le sessioni aperte decadono; la persona riceve un avviso al nuovo indirizzo.
    /// Se l'anagrafica del dipendente usava la vecchia email, si aggiorna anche quella.
    /// </summary>
    public async Task<string?> CambiaEmailAsync(ApplicationUser utente, string nuova)
    {
        nuova = (nuova ?? "").Trim();
        if (nuova.Length == 0 || !new EmailAddressAttribute().IsValid(nuova)) return "Email non valida.";
        if (string.Equals(utente.Email, nuova, StringComparison.OrdinalIgnoreCase)) return null;
        if (await users.FindByEmailAsync(nuova) is not null) return $"Esiste già un accesso con l'email {nuova}.";

        // Proprietà scritte direttamente (non SetEmailAsync) così l'email resta confermata: l'indirizzo lo indica
        // chi gestisce il personale e la persona non deve riattivare l'accesso.
        var vecchia = utente.Email;
        utente.Email = nuova;
        utente.UserName = nuova;
        var esito = await users.UpdateAsync(utente);
        if (!esito.Succeeded) return string.Join(" ", esito.Errors.Select(e => e.Description));
        await users.UpdateSecurityStampAsync(utente);

        if (utente.DipendenteId is { } d && await db.Dipendenti.FindAsync(d) is { } dip
            && string.Equals(dip.Email, vecchia, StringComparison.OrdinalIgnoreCase))
        {
            dip.AggiornaRecapiti(nuova, dip.Telefono);
            await db.SaveChangesAsync();
        }
        await email.InviaAsync(nuova, "La tua email di accesso a Consulens HR è cambiata",
            $"<p>Ciao {Saluto(utente)},</p><p>da ora per entrare in Consulens HR usa questo indirizzo: {WebUtility.HtmlEncode(nuova)}. La password resta la stessa.</p><p>Consulens HR</p>");
        return null;
    }

    public async Task<bool> ImpostaAttivoAsync(ApplicationUser utente, bool attivo)
    {
        utente.Attivo = attivo;
        var esito = await users.UpdateAsync(utente);
        // Nuovo security stamp: le sessioni aperte dell'utente disattivato decadono alla prossima verifica.
        if (esito.Succeeded && !attivo) await users.UpdateSecurityStampAsync(utente);
        return esito.Succeeded;
    }

    /// <summary>Nome con cui si apre l'email (o l'indirizzo, se manca), già codificato per l'HTML.</summary>
    private static string Saluto(ApplicationUser u) => WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(u.Nome) ? u.Email ?? "" : u.Nome);
}
