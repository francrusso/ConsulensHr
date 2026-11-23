using System.Diagnostics;
using CL.Domain.BustePaga;
using CL.Domain.Dipendenti;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using CL.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

public sealed class PanoramicaDipendente
{
    public required IReadOnlyList<CL.Domain.Assenze.SaldoAssenza> Saldi { get; init; }
    public int RichiesteInAttesa { get; init; }
    public BustaRiga? UltimaBusta { get; init; }
    /// <summary>Per chi è responsabile di altri: richieste del team in attesa della sua decisione (null se non è responsabile).</summary>
    public int? RichiesteTeam { get; init; }
    /// <summary>Modalità di lavoro dal contratto (In presenza, Ibrido, Full-remote); null senza contratto.</summary>
    public string? Modalita { get; init; }
    /// <summary>Giorni in ufficio previsti, sede di lavoro e simili, in una riga.</summary>
    public string? DettaglioLavoro { get; init; }
    /// <summary>Chi lavora a distanza segna da sé i giorni in ufficio.</summary>
    public bool SegnaUfficio { get; init; }
}

public sealed class HomeController(AppDbContext db, UtenteCorrente utente, ServizioPanoramica panoramica, ServizioSaldi saldi,
    ServizioOrganigramma organigramma, TimeProvider clock) : Controller
{
    public async Task<IActionResult> Index(Guid? aziendaId)
    {
        if (!utente.IsDipendente) return View(await panoramica.CalcolaAsync(aziendaId));
        // Il dipendente non ha la panoramica: la sua prima pagina è il calendario.
        if (utente.DipendenteId is not null) return RedirectToAction("Index", "Ufficio");

        // Dipendente: i propri saldi, le richieste in attesa e l'ultima busta paga.
        var oggi = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var io = utente.DipendenteId is { } id ? await db.Dipendenti.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id) : null;
        if (io is null) return View("IndexDipendente", new PanoramicaDipendente { Saldi = [] });
        var collaboratori = await organigramma.CollaboratoriAsync(io.Id);

        // Dove lavora: modalità dal contratto in corso (o dal prossimo), sede indicata dall'azienda.
        var contratto = io.ContrattoAttivo(oggi) ?? io.Contratti.Where(c => c.DataInizio > oggi).OrderBy(c => c.DataInizio).FirstOrDefault();
        var profilo = await db.ProfiliAzienda.AsNoTracking().FirstOrDefaultAsync(p => p.AziendaId == io.AziendaId);
        static string Indirizzo(CL.Domain.Aziende.Indirizzo i) => $"{i.Via}, {i.Comune} ({i.Provincia})";
        string? sede = io.Sede switch
        {
            SedeLavoro.SmartWorking => "Smart working",
            SedeLavoro.SedeLegale => profilo?.SedeLegale is { } l ? "Sede legale · " + Indirizzo(l) : "Sede legale",
            SedeLavoro.SedeOperativa => profilo?.SedeOperativa is { } o ? "Sede operativa · " + Indirizzo(o) : "Sede operativa",
            _ => null,
        };
        var giorniUfficio = contratto?.Modalita == ModalitaLavoro.Ibrido && contratto.GiorniInUfficio is { } gu
            ? $"{gu} {(gu == 1 ? "giorno" : "giorni")} in ufficio a settimana" : null;
        var smartConcesso = contratto?.Modalita == ModalitaLavoro.InPresenza && contratto.SmartWorkingConcesso is { } sc ? "Smart working " + sc : null;
        var dettaglio = string.Join(" · ", new[] { giorniUfficio, smartConcesso, sede }.Where(x => x is not null));
        return View("IndexDipendente", new PanoramicaDipendente
        {
            Saldi = await saldi.Calcola(io, oggi.Year, oggi.Month),
            Modalita = contratto?.Modalita.Descrizione() ?? (io.Sede == SedeLavoro.SmartWorking ? "Smart working" : null),
            DettaglioLavoro = dettaglio.Length > 0 ? dettaglio : null,
            SegnaUfficio = ServizioUfficio.LavoraADistanza(io, oggi),
            RichiesteInAttesa = await db.RichiesteAssenza.CountAsync(r => r.DipendenteId == io.Id && r.Stato == CL.Domain.Assenze.StatoRichiesta.InAttesa),
            RichiesteTeam = collaboratori.Count == 0 ? null
                : await db.RichiesteAssenza.CountAsync(r => collaboratori.Contains(r.DipendenteId) && r.Stato == CL.Domain.Assenze.StatoRichiesta.InAttesa),
            UltimaBusta = await db.BustePaga.AsNoTracking()
                .Where(b => b.DipendenteId == io.Id && b.Stato == StatoBustaPaga.Pubblicata)
                .OrderByDescending(b => b.Anno).ThenByDescending(b => b.Mese)
                .Select(b => new BustaRiga
                {
                    Id = b.Id, DipendenteId = b.DipendenteId, Dipendente = "", Azienda = "", Anno = b.Anno, Mese = b.Mese,
                    Tipo = b.Tipo, Netto = b.Netto, Stato = b.Stato, PubblicataIl = b.PubblicataIl, VisualizzataIl = b.VisualizzataIl,
                })
                .FirstOrDefaultAsync(),
        });
    }

    [AllowAnonymous, ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
