using CL.Domain.Assenze;
using CL.Domain.Notifiche;
using CL.Domain.Presenze;
using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

/// <summary>
/// Notifiche sulle richieste: ferie e permessi (a chi decide: il responsabile dall'organigramma, altrimenti i referenti),
/// smart working, straordinari e cambi turno (ai referenti, che li decidono), e i loro esiti alla persona.
/// Vedi <see cref="ServizioNotifiche"/> per come vengono consegnate.
/// </summary>
public sealed class NotificheRichieste(AppDbContext db, ServizioNotifiche notifiche, ServizioOrganigramma organigramma)
{
    private sealed record Persona(string Nome, Guid AziendaId);

    // --- Ferie, permessi e altre assenze ------------------------------------------------------------------------------

    /// <summary>Nuova richiesta in attesa: avvisa chi la deve decidere. Le richieste già approvate non mandano nulla.</summary>
    public async Task NuovaAssenzaAsync(RichiestaAssenza r)
    {
        if (r.Stato != StatoRichiesta.InAttesa) return;
        var p = await PersonaAsync(r.DipendenteId);
        var cosa = Cosa(r);
        await notifiche.InviaAsync(TipoNotifica.AssenzaDaApprovare, await ChiDecideAsync(r.DipendenteId, p.AziendaId),
            $"Richiesta di {cosa} da approvare: {p.Nome}",
            Righe($"{p.Nome} ha chiesto {cosa} {Periodo(r)} ({r.Ore:0.##} ore).", Facoltativa("Note", r.Note),
                "Puoi approvarla o rifiutarla dalle richieste."),
            "/Assenze");
    }

    /// <summary>Esito della richiesta alla persona (con il motivo, se rifiutata).</summary>
    public async Task EsitoAssenzaAsync(RichiestaAssenza r)
    {
        if (r.Stato is not (StatoRichiesta.Approvata or StatoRichiesta.Rifiutata)) return;
        var approvata = r.Stato == StatoRichiesta.Approvata;
        var cosa = Cosa(r);
        await notifiche.InviaAsync(TipoNotifica.AssenzaDecisa, await db.PersoneAsync([r.DipendenteId]),
            approvata ? $"Richiesta di {cosa} approvata" : $"Richiesta di {cosa} non approvata",
            Righe($"La tua richiesta di {cosa} {Periodo(r)} è stata {(approvata ? "approvata" : "rifiutata")}.",
                approvata ? null : Facoltativa("Motivo", r.MotivoRifiuto)),
            "/Assenze");
    }

    /// <summary>La persona chiede di cancellare un'assenza già approvata: avvisa chi la decide.</summary>
    public async Task CancellazioneChiestaAsync(RichiestaAssenza r)
    {
        var p = await PersonaAsync(r.DipendenteId);
        await notifiche.InviaAsync(TipoNotifica.CancellazioneDaDecidere, await ChiDecideAsync(r.DipendenteId, p.AziendaId),
            $"Cancellazione da decidere: {p.Nome}",
            Righe($"{p.Nome} chiede di cancellare {Cosa(r)} {Periodo(r)}, già approvata.", Facoltativa("Motivo", r.MotivoAnnullamento),
                "Puoi confermare o respingere la cancellazione dalle richieste."),
            "/Assenze");
    }

    public async Task CancellazioneDecisaAsync(RichiestaAssenza r, bool confermata) =>
        await notifiche.InviaAsync(TipoNotifica.CancellazioneDecisa, await db.PersoneAsync([r.DipendenteId]),
            confermata ? "Cancellazione confermata" : "Cancellazione respinta",
            confermata
                ? $"La cancellazione di {Cosa(r)} {Periodo(r)} è stata confermata: la richiesta è annullata."
                : $"La cancellazione di {Cosa(r)} {Periodo(r)} è stata respinta: la richiesta resta approvata.",
            "/Assenze");

    /// <summary>L'azienda ha revocato una richiesta (assenza, smart working o straordinario): avvisa la persona.</summary>
    /// <param name="cosa">Cosa è stato revocato, es. "smart working del 12/12/2026".</param>
    public async Task RevocaAsync(Guid dipendenteId, string cosa) =>
        await notifiche.InviaAsync(TipoNotifica.RichiestaRevocata, await db.PersoneAsync([dipendenteId]),
            "Richiesta revocata dall'azienda",
            $"L'azienda ha revocato la tua richiesta di {cosa}: non conta più nei saldi né nelle presenze.",
            "/Assenze");

    public static string Descrizione(RichiestaAssenza r) => $"{Cosa(r)} {Periodo(r)}";

    /// <summary>Chiusura aziendale: avvisa chi ha ricevuto le ferie.</summary>
    public async Task ChiusuraAsync(ChiusuraAziendale c, IReadOnlyCollection<Guid> conFerie)
    {
        if (conFerie.Count == 0) return;
        var periodo = c.Dal == c.Al ? $"il {c.Dal:dd/MM/yyyy}" : $"dal {c.Dal:dd/MM/yyyy} al {c.Al:dd/MM/yyyy}";
        await notifiche.InviaAsync(TipoNotifica.ChiusuraAziendale, await db.PersoneAsync(conFerie),
            $"Chiusura aziendale: {c.Descrizione}",
            $"L'azienda è chiusa {periodo} ({c.Descrizione}): ti sono state registrate le ferie per quei giorni.",
            "/Assenze");
    }

    // --- Smart working e straordinari ---------------------------------------------------------------------------------

    /// <summary>Giorni di smart working da approvare: avvisa i referenti.</summary>
    public async Task SmartWorkingChiestoAsync(Guid dipendenteId, IReadOnlyCollection<DateOnly> giorni)
    {
        if (giorni.Count == 0) return;
        var p = await PersonaAsync(dipendenteId);
        var (primo, ultimo) = (giorni.Min(), giorni.Max());
        var quando = giorni.Count == 1 ? $"per il {primo:dd/MM/yyyy}" : $"per {giorni.Count} giorni, dal {primo:dd/MM/yyyy} al {ultimo:dd/MM/yyyy}";
        await notifiche.InviaAsync(TipoNotifica.SmartWorkingDaApprovare, await db.ReferentiAsync(p.AziendaId),
            $"Smart working da approvare: {p.Nome}",
            Righe($"{p.Nome} chiede lo smart working {quando}.", "Puoi approvarlo o rifiutarlo dalle richieste."),
            "/Assenze");
    }

    public async Task SmartWorkingDecisoAsync(GiornoSmartWorking g)
    {
        if (g.Stato == StatoSmartWorking.InAttesa) return;
        var approvato = g.Stato == StatoSmartWorking.Approvato;
        await notifiche.InviaAsync(TipoNotifica.SmartWorkingDeciso, await db.PersoneAsync([g.DipendenteId]),
            approvato ? "Smart working approvato" : "Smart working non approvato",
            Righe($"La tua richiesta di smart working per il {g.Giorno:dd/MM/yyyy} è stata {(approvato ? "approvata" : "rifiutata")}.",
                approvato ? null : Facoltativa("Motivo", g.MotivoRifiuto)),
            $"/Ufficio?anno={g.Giorno.Year}&mese={g.Giorno.Month}");
    }

    /// <summary>Straordinario segnalato dalla persona e da approvare: avvisa i referenti.</summary>
    public async Task StraordinarioChiestoAsync(Straordinario s)
    {
        if (s.Stato != StatoRichiesta.InAttesa) return;
        var p = await PersonaAsync(s.DipendenteId);
        await notifiche.InviaAsync(TipoNotifica.StraordinarioDaApprovare, await db.ReferentiAsync(p.AziendaId),
            $"Straordinario da approvare: {p.Nome}",
            Righe($"{p.Nome} segnala {s.Ore:0.##} ore di straordinario il {s.Giorno:dd/MM/yyyy}.", Facoltativa("Note", s.Note),
                "Puoi approvarlo o rifiutarlo dalle richieste."),
            "/Assenze");
    }

    public async Task StraordinarioDecisoAsync(Straordinario s)
    {
        if (s.Stato is not (StatoRichiesta.Approvata or StatoRichiesta.Rifiutata)) return;
        var approvato = s.Stato == StatoRichiesta.Approvata;
        await notifiche.InviaAsync(TipoNotifica.StraordinarioDeciso, await db.PersoneAsync([s.DipendenteId]),
            approvato ? "Straordinario approvato" : "Straordinario non approvato",
            Righe($"Lo straordinario di {s.Ore:0.##} ore del {s.Giorno:dd/MM/yyyy} è stato {(approvato ? "approvato" : "rifiutato")}.",
                approvato ? null : Facoltativa("Motivo", s.MotivoRifiuto)),
            "/Assenze");
    }

    public static string Descrizione(Straordinario s) => $"straordinario del {s.Giorno:dd/MM/yyyy} ({s.Ore:0.##} ore)";

    // --- Cambi turno --------------------------------------------------------------------------------------------------

    /// <summary>Richiesta di modifica o scambio di un turno: avvisa i referenti.</summary>
    public async Task CambioTurnoChiestoAsync(RichiestaCambioTurno r)
    {
        var p = await PersonaAsync(r.DipendenteId);
        var cosa = r.CollegaId is { } collega
            ? $"scambiare il turno del {r.InizioOriginale:dd/MM/yyyy HH:mm} con {(await PersonaAsync(collega)).Nome}"
            : $"spostare il turno del {r.InizioOriginale:dd/MM/yyyy HH:mm} a {r.NuovoInizio:dd/MM/yyyy HH:mm}–{r.NuovaFine:HH:mm}";
        await notifiche.InviaAsync(TipoNotifica.CambioTurnoDaApprovare, await db.ReferentiAsync(p.AziendaId),
            $"Cambio turno da approvare: {p.Nome}",
            Righe($"{p.Nome} chiede di {cosa}.", Facoltativa("Note", r.Note), "Puoi approvarla o rifiutarla dalle richieste."),
            "/Assenze");
    }

    /// <summary>Esito al richiedente e, per uno scambio approvato, al collega coinvolto.</summary>
    public async Task CambioTurnoDecisoAsync(RichiestaCambioTurno r)
    {
        if (r.Stato is not (StatoRichiesta.Approvata or StatoRichiesta.Rifiutata)) return;
        var approvata = r.Stato == StatoRichiesta.Approvata;
        var persone = await db.PersoneAsync(r.CollegaId is { } c && approvata ? [r.DipendenteId, c] : [r.DipendenteId]);
        var richiedente = (await PersonaAsync(r.DipendenteId)).Nome;
        var messaggi = persone.Select(d => d.DipendenteId == r.DipendenteId
            ? new MessaggioNotifica(d, approvata ? "Cambio turno approvato" : "Cambio turno non approvato",
                Righe($"La tua richiesta per il turno del {r.InizioOriginale:dd/MM/yyyy HH:mm} è stata {(approvata ? "approvata: il calendario è aggiornato" : "rifiutata")}.",
                    approvata ? null : Facoltativa("Motivo", r.MotivoRifiuto)),
                "/Turni")
            : new MessaggioNotifica(d, "Turno scambiato",
                $"L'azienda ha approvato lo scambio di turno con {richiedente}: il tuo calendario turni è aggiornato.", "/Turni"));
        await notifiche.InviaAsync(TipoNotifica.CambioTurnoDeciso, messaggi);
    }

    // --- Utilità ------------------------------------------------------------------------------------------------------

    /// <summary>Chi decide le assenze della persona: il responsabile (se ha un accesso attivo), altrimenti i referenti.</summary>
    private async Task<List<Destinatario>> ChiDecideAsync(Guid dipendenteId, Guid aziendaId)
    {
        if (await organigramma.ApprovatoreAsync(dipendenteId) is { } responsabile
            && await db.PersoneAsync([responsabile]) is { Count: > 0 } r)
            return r;
        return await db.ReferentiAsync(aziendaId);
    }

    private Task<Persona> PersonaAsync(Guid dipendenteId) =>
        db.Dipendenti.AsNoTracking().IgnoreAutoIncludes().Where(d => d.Id == dipendenteId)
            .Select(d => new Persona(d.Nome + " " + d.Cognome, d.AziendaId)).FirstAsync();

    private static string Cosa(RichiestaAssenza r) => r.Tipo.Descrizione().ToLower();

    private static string Periodo(RichiestaAssenza r) =>
        r.Dal == r.Al ? $"per il {r.Dal:dd/MM/yyyy}" : $"dal {r.Dal:dd/MM/yyyy} al {r.Al:dd/MM/yyyy}";

    private static string? Facoltativa(string etichetta, string? valore) =>
        string.IsNullOrWhiteSpace(valore) ? null : $"{etichetta}: {valore.Trim()}";

    /// <summary>Paragrafi del testo della notifica (le righe vuote si saltano).</summary>
    internal static string Righe(params string?[] righe) => string.Join('\n', righe.Where(r => !string.IsNullOrWhiteSpace(r)));
}
