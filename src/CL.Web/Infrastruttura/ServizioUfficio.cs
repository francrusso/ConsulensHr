using CL.Domain.Assenze;
using CL.Domain.Dipendenti;
using CL.Domain.Presenze;
using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

/// <summary>Come lavora la persona collegata: a distanza (segna i giorni in ufficio) o in presenza (chiede lo smart working).</summary>
public enum ModoLavoro { Distanza, Presenza }

/// <summary>
/// Chi lavora in smart working o in modalità ibrida segna i giorni in cui va in ufficio (senza approvazione);
/// chi lavora in presenza chiede alcuni giorni di smart working (da approvare).
/// </summary>
public sealed class ServizioUfficio(AppDbContext db, UtenteCorrente utente, TimeProvider clock)
{
    private bool _letto;
    private ModoLavoro? _modo;

    /// <summary>Modo di lavoro del dipendente collegato; null se non è un dipendente o non ha un contratto.</summary>
    public async Task<ModoLavoro?> ModoAsync()
    {
        if (_letto) return _modo;
        _letto = true;
        if (!utente.IsDipendente || utente.DipendenteId is not { } id) return null;
        var d = await db.Dipendenti.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        var oggi = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        if (d is null || Contratto(d, oggi) is null) return null;
        _modo = LavoraADistanza(d, oggi) ? ModoLavoro.Distanza : ModoLavoro.Presenza;
        return _modo;
    }

    /// <summary>Il dipendente collegato può segnare i giorni in ufficio (contratto ibrido o full-remote, o sede in smart working).</summary>
    public async Task<bool> PuoSegnareAsync() => await ModoAsync() == ModoLavoro.Distanza;

    private static Contratto? Contratto(Dipendente d, DateOnly oggi) =>
        d.ContrattoAttivo(oggi) ?? d.Contratti.Where(x => x.DataInizio > oggi).OrderBy(x => x.DataInizio).FirstOrDefault();

    public static bool LavoraADistanza(Dipendente d, DateOnly oggi) =>
        d.Sede == SedeLavoro.SmartWorking || Contratto(d, oggi)?.Modalita is ModalitaLavoro.Ibrido or ModalitaLavoro.FullRemote;

    /// <summary>
    /// Segna in ufficio (chi lavora a distanza) o chiede lo smart working (chi lavora in presenza) per i giorni lavorativi
    /// tra <paramref name="dal"/> e <paramref name="al"/>. Salta i giorni non lavorativi, quelli con un'assenza di tutta la
    /// giornata e quelli già segnati; lo smart working non supera i giorni concessi dal contratto. Non salva: lo fa il chiamante.
    /// </summary>
    public async Task<(int Aggiunti, string? Errore)> AggiungiAsync(Guid id, ModoLavoro modo, DateOnly dal, DateOnly al, DateTime adesso)
    {
        var oggi = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        if (al < dal) (dal, al) = (al, dal);
        if (al.DayNumber - dal.DayNumber > 62) return (0, "Scegli al massimo due mesi alla volta.");
        var minimo = modo == ModoLavoro.Presenza ? oggi : new DateOnly(oggi.Year, oggi.Month, 1);
        if (dal < minimo)
            return (0, modo == ModoLavoro.Presenza ? "Lo smart working si chiede per oggi o per i giorni futuri." : "I giorni in ufficio si segnano dal mese in corso.");

        var d = await db.Dipendenti.AsNoTracking().FirstAsync(x => x.Id == id);
        var azienda = await db.Aziende.AsNoTracking().FirstAsync(a => a.Id == d.AziendaId);
        var assenze = await db.RichiesteAssenza.AsNoTracking()
            .Where(r => r.DipendenteId == id && !r.AOre && (r.Stato == StatoRichiesta.Approvata || r.Stato == StatoRichiesta.InAttesa) && r.Dal <= al && r.Al >= dal)
            .ToListAsync();
        // Per il limite dello smart working servono anche i giorni della settimana/mese attorno al periodo.
        var smart = await db.GiorniSmartWorking.AsNoTracking()
            .Where(g => g.DipendenteId == id && g.Stato != StatoSmartWorking.Rifiutato && g.Giorno >= dal.AddDays(-31) && g.Giorno <= al.AddDays(31))
            .Select(g => g.Giorno).ToListAsync();
        var ufficio = await db.GiorniInUfficio.AsNoTracking().Where(g => g.DipendenteId == id && g.Giorno >= dal && g.Giorno <= al).Select(g => g.Giorno).ToListAsync();

        var aggiunti = 0;
        string? limite = null;
        for (var g = dal; g <= al; g = g.AddDays(1))
        {
            var giorno = g;
            var c = d.ContrattoAttivo(g);
            if (c is null || !CalendarioLavorativo.IsLavorativo(g, c.GiorniLavorativiSettimana, azienda.SantoPatronoMese, azienda.SantoPatronoGiorno)) continue;
            if (assenze.Any(r => r.Dal <= giorno && r.Al >= giorno)) continue;
            if (modo == ModoLavoro.Distanza)
            {
                if (ufficio.Contains(g)) continue;
                db.GiorniInUfficio.Add(new GiornoInUfficio(id, g, adesso));
            }
            else
            {
                if (smart.Contains(g)) continue;
                if (c.GiorniSmartWorking is { } concessi && c.PeriodoSmartWorking is { } periodo)
                {
                    var (da, a) = periodo == PeriodoSmartWorking.Settimana
                        ? (g.AddDays(-(((int)g.DayOfWeek + 6) % 7)), g.AddDays(6 - (((int)g.DayOfWeek + 6) % 7)))
                        : (new DateOnly(g.Year, g.Month, 1), new DateOnly(g.Year, g.Month, 1).AddMonths(1).AddDays(-1));
                    if (smart.Count(x => x >= da && x <= a) >= concessi)
                    {
                        limite = $"Oltre lo smart working concesso dal contratto ({c.SmartWorkingConcesso}).";
                        continue;
                    }
                }
                db.GiorniSmartWorking.Add(new GiornoSmartWorking(id, g, adesso));
                smart.Add(g);
            }
            aggiunti++;
        }
        if (aggiunti == 0)
            return (0, limite ?? "Nessun giorno da aggiungere: sono non lavorativi, già segnati o con un'assenza.");
        return (aggiunti, limite is null ? null : $"Alcuni giorni sono rimasti fuori: {limite.TrimEnd('.')}.");
    }
}
