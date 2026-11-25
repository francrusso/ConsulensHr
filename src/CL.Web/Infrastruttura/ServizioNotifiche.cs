using System.Linq.Expressions;
using System.Net;
using CL.Domain.Notifiche;
using CL.Infrastructure.Email;
using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

/// <summary>
/// Chi riceve una notifica: un utente con accesso attivo. L'email parte solo se l'ha confermata (cioè ha scelto la
/// password dal link d'invito); la notifica in piattaforma la trova comunque al primo accesso.
/// </summary>
public sealed record Destinatario(string UtenteId, string? Email, string Nome, bool EmailConfermata, Guid? DipendenteId);

/// <summary>Una notifica per una persona. <see cref="Chiave"/> identifica l'evento per non ripetere un promemoria.</summary>
public sealed record MessaggioNotifica(Destinatario A, string Titolo, string Testo, string? Link, string? Chiave = null);

/// <summary>A chi mandare le notifiche: consulenti, referenti e persone del personale con accesso attivo.</summary>
public static class QueryDestinatari
{
    private static readonly Expression<Func<ApplicationUser, Destinatario>> Riga =
        u => new Destinatario(u.Id, u.Email, u.Nome, u.EmailConfirmed, u.DipendenteId);

    /// <summary>
    /// I consulenti a cui è assegnata l'azienda. Se non è assegnata a nessuno, i super amministratori (vedono tutte le
    /// aziende), così l'avviso non va perso.
    /// </summary>
    public static async Task<List<Destinatario>> StudioAsync(this AppDbContext db, Guid aziendaId)
    {
        var consulenti = db.IdConRuolo(Ruoli.Consulente);
        var assegnati = db.AssegnazioniAziende.Where(a => a.AziendaId == aziendaId).Select(a => a.UserId);
        var studio = await db.Users.Where(u => u.Attivo && consulenti.Contains(u.Id) && assegnati.Contains(u.Id)).Select(Riga).ToListAsync();
        if (studio.Count > 0) return studio;
        var amministratori = db.IdConRuolo(Ruoli.Amministratore);
        return await db.Users.Where(u => u.Attivo && amministratori.Contains(u.Id)).Select(Riga).ToListAsync();
    }

    /// <summary>I referenti dell'azienda.</summary>
    public static Task<List<Destinatario>> ReferentiAsync(this AppDbContext db, Guid aziendaId)
    {
        var referenti = db.IdConRuolo(Ruoli.Azienda);
        return db.Users.Where(u => u.Attivo && u.AziendaId == aziendaId && referenti.Contains(u.Id)).Select(Riga).ToListAsync();
    }

    /// <summary>Le persone del personale indicate, per chi ha un accesso al portale.</summary>
    public static Task<List<Destinatario>> PersoneAsync(this AppDbContext db, IEnumerable<Guid> dipendenti)
    {
        var ids = dipendenti.Distinct().ToList();
        return db.Users.Where(u => u.Attivo && u.DipendenteId != null && ids.Contains(u.DipendenteId.Value)).Select(Riga).ToListAsync();
    }

    public static Task<List<Destinatario>> UtentiAsync(this AppDbContext db, IEnumerable<string> utenti)
    {
        var ids = utenti.Distinct().ToList();
        return db.Users.Where(u => u.Attivo && ids.Contains(u.Id)).Select(Riga).ToListAsync();
    }
}

/// <summary>
/// Consegna le notifiche: sempre in piattaforma (campanella e pagina Notifiche), anche per email a chi non l'ha spenta
/// per quella tipologia (pagina Impostazioni notifiche). Chi compie l'azione non riceve la notifica della propria azione.
/// Le email partono in background dalla <see cref="CodaEmail"/>.
/// </summary>
public sealed class ServizioNotifiche(AppDbContext db, CodaEmail coda, ServizioAccessi accessi, UtenteCorrente utente,
    ServizioOrganigramma organigramma, TimeProvider clock)
{
    /// <summary>Quante notifiche mostra la campanella.</summary>
    public const int Anteprima = 5;

    /// <summary>Lo stesso messaggio a più persone (vedi l'altro overload).</summary>
    /// <param name="testo">Testo in chiaro; più paragrafi separati da un a capo.</param>
    /// <param name="link">Percorso del portale in cui vedere l'evento (es. "/Assenze").</param>
    public Task<int> InviaAsync(TipoNotifica tipo, IEnumerable<Destinatario> destinatari, string titolo, string testo,
        string? link, string? chiave = null) =>
        InviaAsync(tipo, destinatari.Select(d => new MessaggioNotifica(d, titolo, testo, link, chiave)));

    /// <summary>
    /// Crea le notifiche (un solo salvataggio) e accoda le email. Un messaggio con <see cref="MessaggioNotifica.Chiave"/>
    /// già ricevuto dalla stessa persona (es. il promemoria di una scadenza) non si ripete.
    /// Restituisce quante persone sono state avvisate.
    /// </summary>
    public async Task<int> InviaAsync(TipoNotifica tipo, IEnumerable<MessaggioNotifica> messaggi)
    {
        var io = utente.UserId;
        var lista = messaggi.Where(m => m.A.UtenteId != io).DistinctBy(m => (m.A.UtenteId, m.Titolo, m.Testo, m.Chiave)).ToList();
        var chiavi = lista.Select(m => m.Chiave).OfType<string>().Distinct().ToList();
        if (chiavi.Count > 0)
        {
            var ids = lista.Select(m => m.A.UtenteId).Distinct().ToList();
            var giaAvvisati = (await db.Notifiche
                    .Where(n => n.Chiave != null && chiavi.Contains(n.Chiave) && ids.Contains(n.UtenteId))
                    .Select(n => new { n.UtenteId, n.Chiave }).ToListAsync())
                .Select(n => (n.UtenteId, n.Chiave)).ToHashSet();
            lista.RemoveAll(m => m.Chiave is not null && giaAvvisati.Contains((m.A.UtenteId, m.Chiave)));
        }
        if (lista.Count == 0) return 0;

        var adesso = clock.GetUtcNow().UtcDateTime;
        foreach (var m in lista) db.Notifiche.Add(new Notifica(m.A.UtenteId, tipo, m.Titolo, m.Testo, m.Link, adesso, m.Chiave));
        await db.SaveChangesAsync();

        // Email a chi non l'ha spenta per questa tipologia (senza scelta salvata vale il predefinito: attiva).
        var destinatari = lista.Select(m => m.A.UtenteId).Distinct().ToList();
        var senzaEmail = (await db.PreferenzeNotifiche.AsNoTracking()
            .Where(p => p.Tipo == tipo && !p.Email && destinatari.Contains(p.UtenteId))
            .Select(p => p.UtenteId).ToListAsync()).ToHashSet();
        var impostazioni = accessi.UrlAssoluto("/Notifiche/Impostazioni");
        foreach (var m in lista.Where(m => m.A.EmailConfermata && !string.IsNullOrWhiteSpace(m.A.Email) && !senzaEmail.Contains(m.A.UtenteId)))
            coda.Accoda(new MessaggioEmail(m.A.Email!, m.Titolo,
                Html(m.A, tipo, m.Testo, m.Link is null ? null : accessi.UrlAssoluto(m.Link), impostazioni)));
        return destinatari.Count;
    }

    /// <summary>Corpo dell'email: saluto, testo della notifica, link al portale e, in fondo, come spegnere queste email.</summary>
    private static string Html(Destinatario d, TipoNotifica tipo, string testo, string? url, string? impostazioni)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        var paragrafi = string.Concat(testo.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => $"<p>{E(p)}</p>"));
        var saluto = string.IsNullOrWhiteSpace(d.Nome) ? "" : $"<p>Ciao {E(d.Nome)},</p>";
        var link = url is null ? "" : $"""<p>Apri nel portale: <a href="{E(url)}">{E(url)}</a></p>""";
        var piede = impostazioni is null ? "" :
            $"""<p style="color:#8a8580;font-size:12px">Ricevi questa email per le notifiche «{E(CatalogoNotifiche.Di(tipo).Nome)}». Puoi spegnerle da Impostazioni notifiche: <a href="{E(impostazioni)}">{E(impostazioni)}</a></p>""";
        return $"{saluto}{paragrafi}{link}<p>Consulens HR</p>{piede}";
    }

    // --- Notifiche dell'utente corrente -------------------------------------------------------------------------------

    /// <summary>Le notifiche dell'utente che sta usando il portale (nessuna se non ha fatto l'accesso).</summary>
    public IQueryable<Notifica> Mie()
    {
        var io = utente.UserId;
        return io is null ? db.Notifiche.Where(_ => false) : db.Notifiche.Where(n => n.UtenteId == io);
    }

    public Task<int> DaLeggereAsync() => Mie().CountAsync(n => n.LettaIl == null);

    public Task<List<Notifica>> UltimeAsync(int quante = Anteprima) =>
        Mie().AsNoTracking().OrderByDescending(n => n.CreataIl).Take(quante).ToListAsync();

    /// <summary>Segna come letta una notifica (restituisce null se non è dell'utente) e ne restituisce il link.</summary>
    public async Task<Notifica?> ApriAsync(Guid id)
    {
        var n = await Mie().FirstOrDefaultAsync(x => x.Id == id);
        if (n is null) return null;
        if (!n.Letta)
        {
            n.SegnaLetta(clock.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync();
        }
        return n;
    }

    public async Task<int> SegnaTutteLetteAsync()
    {
        var adesso = clock.GetUtcNow().UtcDateTime;
        var daLeggere = await Mie().Where(n => n.LettaIl == null).ToListAsync();
        foreach (var n in daLeggere) n.SegnaLetta(adesso);
        await db.SaveChangesAsync();
        return daLeggere.Count;
    }

    // --- Impostazioni -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Che notifiche può ricevere l'utente corrente: studio, referente, persona del personale (anche un referente può
    /// esserlo) e responsabile, se approva le richieste di qualcuno.
    /// </summary>
    public async Task<Destinatari> RuoliAsync()
    {
        var ruoli = Destinatari.Nessuno;
        if (utente.IsConsulente) ruoli |= Destinatari.Studio;
        if (utente.IsAzienda) ruoli |= Destinatari.Referente;
        if (utente.DipendenteId is { } d)
        {
            ruoli |= Destinatari.Dipendente;
            if (utente.IsDipendente && (await organigramma.CollaboratoriAsync(d)).Count > 0) ruoli |= Destinatari.Responsabile;
        }
        return ruoli;
    }

    /// <summary>Email attiva o no per ogni tipologia, per l'utente corrente.</summary>
    public async Task<Dictionary<TipoNotifica, bool>> EmailAsync()
    {
        var scelte = await db.PreferenzeNotifiche.AsNoTracking().Where(p => p.UtenteId == utente.UserId)
            .ToDictionaryAsync(p => p.Tipo, p => p.Email);
        return CatalogoNotifiche.Tutte.ToDictionary(d => d.Tipo, d => scelte.GetValueOrDefault(d.Tipo, PreferenzaNotifica.EmailPredefinita));
    }

    /// <summary>Accende o spegne l'email delle tipologie indicate, per l'utente corrente.</summary>
    public async Task ImpostaEmailAsync(IReadOnlyCollection<TipoNotifica> tipi, bool attiva)
    {
        if (utente.UserId is not { } io || tipi.Count == 0) return;
        var esistenti = await db.PreferenzeNotifiche.Where(p => p.UtenteId == io && tipi.Contains(p.Tipo)).ToDictionaryAsync(p => p.Tipo);
        foreach (var tipo in tipi.Distinct())
            if (esistenti.TryGetValue(tipo, out var p)) p.ImpostaEmail(attiva);
            else db.PreferenzeNotifiche.Add(new PreferenzaNotifica(io, tipo, attiva));
        await db.SaveChangesAsync();
    }
}

/// <summary>Icona (Remix Icon) di ogni sezione, nella campanella, nell'elenco e nelle impostazioni.</summary>
public static class IconeNotifiche
{
    public static string Di(CategoriaNotifica c) => c switch
    {
        CategoriaNotifica.Richieste => "ri-calendar-check-line",
        CategoriaNotifica.SmartWorkingStraordinari => "ri-home-wifi-line",
        CategoriaNotifica.Turni => "ri-time-line",
        CategoriaNotifica.Presenze => "ri-file-list-3-line",
        CategoriaNotifica.Documenti => "ri-folder-3-line",
        CategoriaNotifica.Personale => "ri-team-line",
        CategoriaNotifica.Aziende => "ri-building-line",
        _ => "ri-alarm-warning-line",
    };
}
