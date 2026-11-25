using CL.Domain.Common;
using CL.Domain.Notifiche;
using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using CL.Infrastructure.WhatsApp;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CL.Web.Infrastruttura;

/// <summary>
/// Il canale WhatsApp per l'utente corrente: disponibile sul server, numero collegato, numero suggerito e numero
/// Consulens da cui arrivano i messaggi.
/// </summary>
public sealed record StatoWhatsApp(bool Disponibile, string? Numero, string? Suggerito, string? Mittente)
{
    public bool Collegato => Numero is not null;
}

/// <summary>
/// Collega il numero WhatsApp dell'utente per le notifiche. Serve il consenso e un codice ricevuto su WhatsApp (token
/// telefonico di Identity, valido pochi minuti), così un numero sbagliato non riceve mai i dati del personale.
/// Il numero confermato è PhoneNumber dell'utente; la data del consenso resta per dimostrarlo.
/// </summary>
public sealed class CollegamentoWhatsApp(UserManager<ApplicationUser> users, AppDbContext db, IInvioWhatsApp whatsApp,
    IOptions<OpzioniWhatsApp> opzioni, IMemoryCache cache, UtenteCorrente utente, TimeProvider clock)
{
    public const int TentativiMassimi = 5;
    /// <summary>Attesa tra un codice e il successivo (ogni messaggio WhatsApp ha un costo).</summary>
    public static readonly TimeSpan AttesaNuovoCodice = TimeSpan.FromSeconds(60);

    public async Task<StatoWhatsApp> StatoAsync()
    {
        var u = await UtenteAsync();
        var numero = u is { PhoneNumberConfirmed: true, WhatsAppConsensoIl: not null } ? u.PhoneNumber : null;
        // Al dipendente si propone il cellulare dell'anagrafica.
        var suggerito = u?.DipendenteId is { } d
            ? await db.Dipendenti.AsNoTracking().IgnoreAutoIncludes().Where(x => x.Id == d).Select(x => x.Telefono).FirstOrDefaultAsync()
            : null;
        var mittente = opzioni.Value.NumeroMittente is { } m && !string.IsNullOrWhiteSpace(m) ? m : null;
        return new StatoWhatsApp(whatsApp.Configurato, numero, suggerito, mittente);
    }

    /// <summary>Manda il codice al numero (normalizzato). Restituisce il numero a cui chiedere il codice e l'eventuale errore.</summary>
    public async Task<(string? Numero, string? Errore)> InviaCodiceAsync(string? numero, bool consenso)
    {
        if (!whatsApp.Configurato) return (null, "Le notifiche su WhatsApp non sono ancora disponibili.");
        if (!consenso) return (null, "Per ricevere le notifiche su WhatsApp serve il tuo consenso.");
        string n;
        try { n = NumeroWhatsApp.Normalizza(numero); }
        catch (DomainException ex) { return (null, ex.Message); }
        if (await UtenteAsync() is not { } u) return (null, "Utente non trovato.");

        var attesa = $"wa-codice:{u.Id}";
        if (cache.TryGetValue(attesa, out _)) return (n, "Ti abbiamo appena inviato un codice: aspetta un minuto prima di chiederne un altro.");
        var codice = await users.GenerateChangePhoneNumberTokenAsync(u, n);
        if (!await whatsApp.InviaCodiceAsync(n, codice))
            return (null, "Non è stato possibile inviare il codice: controlla che il numero usi WhatsApp e riprova.");
        cache.Set(attesa, true, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = AttesaNuovoCodice, Size = 1 });
        cache.Remove($"wa-tentativi:{u.Id}");
        return (n, null);
    }

    /// <summary>Controlla il codice e collega il numero (con la data del consenso). Restituisce l'errore, o null.</summary>
    public async Task<string?> VerificaAsync(string? numero, string? codice)
    {
        if (await UtenteAsync() is not { } u) return "Utente non trovato.";
        string n;
        try { n = NumeroWhatsApp.Normalizza(numero); }
        catch (DomainException ex) { return ex.Message; }

        var chiave = $"wa-tentativi:{u.Id}";
        var tentativi = cache.Get<int?>(chiave) ?? 0;
        if (tentativi >= TentativiMassimi) return "Troppi tentativi: chiedi un nuovo codice.";
        if (!await users.VerifyChangePhoneNumberTokenAsync(u, (codice ?? "").Trim(), n))
        {
            cache.Set(chiave, tentativi + 1, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15), Size = 1 });
            return "Codice non valido o scaduto: controlla il messaggio o chiedine uno nuovo.";
        }
        cache.Remove(chiave);
        // Proprietà scritte direttamente (non ChangePhoneNumberAsync): non cambia il security stamp, le sessioni restano aperte.
        u.PhoneNumber = n;
        u.PhoneNumberConfirmed = true;
        u.WhatsAppConsensoIl = clock.GetUtcNow().UtcDateTime;
        var esito = await users.UpdateAsync(u);
        return esito.Succeeded ? null : string.Join(" ", esito.Errors.Select(e => e.Description));
    }

    /// <summary>Toglie numero e consenso: da subito non partono più notifiche su WhatsApp.</summary>
    public async Task ScollegaAsync()
    {
        if (await UtenteAsync() is not { } u) return;
        u.PhoneNumber = null;
        u.PhoneNumberConfirmed = false;
        u.WhatsAppConsensoIl = null;
        await users.UpdateAsync(u);
    }

    private async Task<ApplicationUser?> UtenteAsync() => utente.UserId is { } id ? await users.FindByIdAsync(id) : null;
}
