using CL.Domain.Assenze;
using CL.Domain.Presenze;
using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

public sealed record GiornoUfficio(DateOnly Data, bool DelMese, bool Lavorativo, bool Segnato, string? Assenza, bool Modificabile)
{
    /// <summary>Per chi lavora in presenza: stato della richiesta di smart working del giorno.</summary>
    public StatoSmartWorking? Smart { get; init; }
    public string? MotivoRifiuto { get; init; }
    /// <summary>Ferie, permessi, malattia... in attesa o approvati che toccano il giorno.</summary>
    public List<RichiestaDelGiorno> Richieste { get; init; } = [];
    public List<string> Chiusure { get; init; } = [];
    /// <summary>Festività (nazionale o santo patrono) in un giorno feriale.</summary>
    public bool Festivo { get; init; }
    /// <summary>Si possono chiedere ferie o permessi (giorno lavorativo da oggi in poi, senza assenze di tutta la giornata).</summary>
    public bool Richiedibile { get; init; }
}

public sealed record RichiestaDelGiorno(TipoAssenza Tipo, StatoRichiesta Stato, bool AOre, decimal Ore, string? Note);

public sealed class UfficioViewModel
{
    public required int Anno { get; init; }
    public required int Mese { get; init; }
    public required List<GiornoUfficio> Giorni { get; init; }
    public required DateOnly Oggi { get; init; }
    /// <summary>Giorni in ufficio previsti dal contratto ibrido (null per lo smart working).</summary>
    public int? GiorniPrevisti { get; init; }
    /// <summary>Distanza: si segnano i giorni in ufficio. Presenza: si chiedono i giorni di smart working.</summary>
    public ModoLavoro? Modo { get; init; }
    /// <summary>Smart working concesso dal contratto a chi lavora in presenza (es. "2 giorni al mese").</summary>
    public string? SmartConcesso { get; init; }
    public CL.Domain.Dipendenti.PeriodoSmartWorking? PeriodoSmart { get; init; }
    public int? GiorniSmartConcessi { get; init; }
}

/// <summary>
/// "Giorni in ufficio": chi è in smart working o in lavoro ibrido segna i giorni in presenza. Nessuna approvazione:
/// basta un clic sul giorno per segnarlo o toglierlo. Chi lavora in presenza, con lo stesso calendario, chiede invece
/// alcuni giorni di smart working, che l'azienda approva. Tutto si vede nel calendario di presenze e assenze.
/// </summary>
[Authorize(Roles = Ruoli.Dipendente)]
public sealed class UfficioController(AppDbContext db, UtenteCorrente utente, ServizioUfficio ufficio, TimeProvider clock) : AppController
{
    private DateOnly Oggi => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<IActionResult> Index(int? anno, int? mese)
    {
        if (utente.DipendenteId is not { } id) return RedirectToAction("Index", "Home");
        var modo = await ufficio.ModoAsync(); // null senza contratto: il calendario si vede ma non si segna nulla
        var oggi = Oggi;
        var primo = new DateOnly(anno ?? oggi.Year, mese is >= 1 and <= 12 ? mese.Value : oggi.Month, 1);
        var ultimo = primo.AddMonths(1).AddDays(-1);
        var inizio = primo.AddDays(-(((int)primo.DayOfWeek + 6) % 7));
        var fine = ultimo.AddDays(6 - (((int)ultimo.DayOfWeek + 6) % 7));

        var d = await db.Dipendenti.AsNoTracking().FirstAsync(x => x.Id == id);
        var richieste = modo == ModoLavoro.Presenza
            ? await db.GiorniSmartWorking.AsNoTracking().Where(g => g.DipendenteId == id && g.Giorno >= inizio && g.Giorno <= fine).ToDictionaryAsync(g => g.Giorno)
            : [];
        var segnati = (await db.GiorniInUfficio.AsNoTracking().Where(g => g.DipendenteId == id && g.Giorno >= inizio && g.Giorno <= fine)
            .Select(g => g.Giorno).ToListAsync()).ToHashSet();
        var assenze = await db.RichiesteAssenza.AsNoTracking()
            .Where(r => r.DipendenteId == id && (r.Stato == StatoRichiesta.Approvata || r.Stato == StatoRichiesta.InAttesa) && r.Dal <= fine && r.Al >= inizio)
            .OrderBy(r => r.Dal).ToListAsync();
        var azienda = await db.Aziende.AsNoTracking().FirstAsync(a => a.Id == d.AziendaId);
        var chiusure = await db.ChiusureAziendali.AsNoTracking().Where(c => c.AziendaId == d.AziendaId && c.Dal <= fine && c.Al >= inizio).ToListAsync();
        var contratto = d.ContrattoAttivo(oggi) ?? d.Contratti.OrderByDescending(c => c.DataInizio).FirstOrDefault();
        var giorniSettimana = contratto?.GiorniLavorativiSettimana ?? 5;

        var giorni = new List<GiornoUfficio>();
        for (var g = inizio; g <= fine; g = g.AddDays(1))
        {
            var giorno = g;
            var lavorativo = CalendarioLavorativo.IsLavorativo(g, giorniSettimana, azienda.SantoPatronoMese, azienda.SantoPatronoGiorno);
            var delGiorno = assenze.Where(r => r.Dal <= giorno && r.Al >= giorno).ToList();
            // Assente tutto il giorno (approvato o in attesa): niente ufficio né smart working da segnare.
            var assenza = delGiorno.FirstOrDefault(r => !r.AOre)?.Tipo.Descrizione();
            // Lo smart working si chiede da oggi in poi; i giorni in ufficio si segnano anche a posteriori nel mese.
            var daQuando = modo == ModoLavoro.Presenza ? oggi : new DateOnly(oggi.Year, oggi.Month, 1);
            var modificabile = modo is not null && g.Month == primo.Month && lavorativo && assenza is null && d.InForzaIl(g) && g >= daQuando;
            var richiesta = richieste.GetValueOrDefault(g);
            giorni.Add(new GiornoUfficio(g, g.Month == primo.Month, lavorativo, segnati.Contains(g), assenza, modificabile)
            {
                Smart = richiesta?.Stato, MotivoRifiuto = richiesta?.MotivoRifiuto,
                Richieste = delGiorno.Select(r => new RichiestaDelGiorno(r.Tipo, r.Stato, r.AOre, r.AOre ? r.Ore : 0, r.Note)).ToList(),
                Chiusure = chiusure.Where(c => c.Dal <= giorno && c.Al >= giorno).Select(c => c.Descrizione).ToList(),
                Richiedibile = g.Month == primo.Month && lavorativo && assenza is null && g >= oggi && d.InForzaIl(g),
                Festivo = g.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
                          && CalendarioLavorativo.IsFestivo(g, azienda.SantoPatronoMese, azienda.SantoPatronoGiorno),
            });
        }
        return View(new UfficioViewModel
        {
            Anno = primo.Year, Mese = primo.Month, Giorni = giorni, Oggi = oggi, Modo = modo,
            SmartConcesso = contratto?.SmartWorkingConcesso, PeriodoSmart = contratto?.PeriodoSmartWorking, GiorniSmartConcessi = contratto?.GiorniSmartWorking,
            GiorniPrevisti = contratto?.Modalita == CL.Domain.Dipendenti.ModalitaLavoro.Ibrido ? contratto.GiorniInUfficio : null,
        });
    }

    /// <summary>Segna o toglie un giorno in ufficio (nessuna approvazione).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Segna(DateOnly giorno)
    {
        if (!await ufficio.PuoSegnareAsync() || utente.DipendenteId is not { } id) return Forbid();
        var esistente = await db.GiorniInUfficio.FirstOrDefaultAsync(g => g.DipendenteId == id && g.Giorno == giorno);
        if (esistente is not null) db.GiorniInUfficio.Remove(esistente);
        else if (await ufficio.AggiungiAsync(id, ModoLavoro.Distanza, giorno, giorno, clock.GetUtcNow().UtcDateTime) is { Aggiunti: 0, Errore: var errore })
        {
            Errore(errore ?? "Questo giorno non si può segnare.");
            return RedirectToAction(nameof(Index), new { anno = giorno.Year, mese = giorno.Month });
        }
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { anno = giorno.Year, mese = giorno.Month });
    }

    /// <summary>
    /// Chi lavora in presenza chiede un giorno di smart working, o ritira la richiesta (anche già decisa, se il giorno
    /// non è passato). Resta in attesa finché l'azienda non la approva.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Richiedi(DateOnly giorno, string? ritorno)
    {
        if (utente.DipendenteId is not { } id) return Forbid();
        var oggi = Oggi;
        IActionResult torna = Url.IsLocalUrl(ritorno) ? LocalRedirect(ritorno) : RedirectToAction(nameof(Index), new { anno = giorno.Year, mese = giorno.Month });
        // Ritirare una richiesta già fatta si può sempre (finché il giorno non è passato); chiederne una nuova solo a chi lavora in presenza.
        var giaChiesto = await db.GiorniSmartWorking.AnyAsync(g => g.DipendenteId == id && g.Giorno == giorno);
        if (!giaChiesto && await ufficio.ModoAsync() != ModoLavoro.Presenza) return Forbid();
        if (giorno < oggi)
        {
            Errore("Lo smart working si chiede per oggi o per i giorni futuri.");
            return torna;
        }
        var esistente = await db.GiorniSmartWorking.FirstOrDefaultAsync(g => g.DipendenteId == id && g.Giorno == giorno);
        if (esistente is not null)
        {
            db.GiorniSmartWorking.Remove(esistente);
            await db.SaveChangesAsync();
            Conferma($"Richiesta di smart working del {giorno:dd/MM/yyyy} ritirata.");
            return torna;
        }
        var (aggiunti, errore) = await ufficio.AggiungiAsync(id, ModoLavoro.Presenza, giorno, giorno, clock.GetUtcNow().UtcDateTime);
        if (aggiunti == 0)
        {
            Errore(errore ?? "Questo giorno non si può chiedere.");
            return torna;
        }
        await db.SaveChangesAsync();
        Conferma($"Smart working del {giorno:dd/MM/yyyy} chiesto: in attesa di approvazione dell'azienda.");
        return torna;
    }
}
