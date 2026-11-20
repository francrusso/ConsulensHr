using CL.Domain.Assenze;
using CL.Domain.Presenze;
using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

public sealed record GiornoUfficio(DateOnly Data, bool DelMese, bool Lavorativo, bool Segnato, string? Assenza, bool Modificabile);

public sealed class UfficioViewModel
{
    public required int Anno { get; init; }
    public required int Mese { get; init; }
    public required List<GiornoUfficio> Giorni { get; init; }
    public required DateOnly Oggi { get; init; }
    /// <summary>Giorni in ufficio previsti dal contratto ibrido (null per lo smart working).</summary>
    public int? GiorniPrevisti { get; init; }
}

/// <summary>
/// "Giorni in ufficio": chi è in smart working o in lavoro ibrido segna i giorni in presenza. Nessuna approvazione:
/// basta un clic sul giorno per segnarlo o toglierlo. L'azienda li vede nel calendario di ferie e permessi.
/// </summary>
[Authorize(Roles = Ruoli.Dipendente)]
public sealed class UfficioController(AppDbContext db, UtenteCorrente utente, ServizioUfficio ufficio, TimeProvider clock) : AppController
{
    private DateOnly Oggi => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<IActionResult> Index(int? anno, int? mese)
    {
        if (!await ufficio.PuoSegnareAsync() || utente.DipendenteId is not { } id) return RedirectToAction("Index", "Home");
        var oggi = Oggi;
        var primo = new DateOnly(anno ?? oggi.Year, mese is >= 1 and <= 12 ? mese.Value : oggi.Month, 1);
        var ultimo = primo.AddMonths(1).AddDays(-1);
        var inizio = primo.AddDays(-(((int)primo.DayOfWeek + 6) % 7));
        var fine = ultimo.AddDays(6 - (((int)ultimo.DayOfWeek + 6) % 7));

        var d = await db.Dipendenti.AsNoTracking().FirstAsync(x => x.Id == id);
        var segnati = (await db.GiorniInUfficio.AsNoTracking().Where(g => g.DipendenteId == id && g.Giorno >= inizio && g.Giorno <= fine)
            .Select(g => g.Giorno).ToListAsync()).ToHashSet();
        var assenze = await db.RichiesteAssenza.AsNoTracking()
            .Where(r => r.DipendenteId == id && r.Stato == StatoRichiesta.Approvata && !r.AOre && r.Dal <= fine && r.Al >= inizio).ToListAsync();
        var contratto = d.ContrattoAttivo(oggi) ?? d.Contratti.OrderByDescending(c => c.DataInizio).FirstOrDefault();
        var giorniSettimana = contratto?.GiorniLavorativiSettimana ?? 5;

        var giorni = new List<GiornoUfficio>();
        for (var g = inizio; g <= fine; g = g.AddDays(1))
        {
            var giorno = g;
            var lavorativo = CalendarioLavorativo.IsLavorativo(g, giorniSettimana);
            var assenza = assenze.FirstOrDefault(r => r.Dal <= giorno && r.Al >= giorno)?.Tipo.Descrizione();
            var modificabile = g.Month == primo.Month && lavorativo && assenza is null && d.InForzaIl(g) && g >= new DateOnly(oggi.Year, oggi.Month, 1);
            giorni.Add(new GiornoUfficio(g, g.Month == primo.Month, lavorativo, segnati.Contains(g), assenza, modificabile));
        }
        return View(new UfficioViewModel
        {
            Anno = primo.Year, Mese = primo.Month, Giorni = giorni, Oggi = oggi,
            GiorniPrevisti = contratto?.Modalita == CL.Domain.Dipendenti.ModalitaLavoro.Ibrido ? contratto.GiorniInUfficio : null,
        });
    }

    /// <summary>Segna o toglie un giorno in ufficio (nessuna approvazione).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Segna(DateOnly giorno)
    {
        if (!await ufficio.PuoSegnareAsync() || utente.DipendenteId is not { } id) return Forbid();
        var oggi = Oggi;
        var d = await db.Dipendenti.AsNoTracking().FirstAsync(x => x.Id == id);
        var esistente = await db.GiorniInUfficio.FirstOrDefaultAsync(g => g.DipendenteId == id && g.Giorno == giorno);
        if (esistente is not null) db.GiorniInUfficio.Remove(esistente);
        else
        {
            var assente = await db.RichiesteAssenza.AnyAsync(r => r.DipendenteId == id && r.Stato == StatoRichiesta.Approvata && !r.AOre
                                                                && r.Dal <= giorno && r.Al >= giorno);
            var contratto = d.ContrattoAttivo(giorno);
            if (giorno < new DateOnly(oggi.Year, oggi.Month, 1) || contratto is null || assente
                || !CalendarioLavorativo.IsLavorativo(giorno, contratto.GiorniLavorativiSettimana))
            {
                Errore("Questo giorno non si può segnare (non lavorativo, già assente o in un mese passato).");
                return RedirectToAction(nameof(Index), new { anno = giorno.Year, mese = giorno.Month });
            }
            db.GiorniInUfficio.Add(new GiornoInUfficio(id, giorno, clock.GetUtcNow().UtcDateTime));
        }
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { anno = giorno.Year, mese = giorno.Month });
    }
}
