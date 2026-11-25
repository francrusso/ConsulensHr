namespace CL.Domain.Notifiche;

/// <summary>Sezione della piattaforma a cui appartiene una notifica: raggruppa le tipologie nella pagina delle impostazioni.</summary>
public enum CategoriaNotifica { Richieste, SmartWorkingStraordinari, Turni, Presenze, Documenti, Personale, Aziende, Scadenze }

/// <summary>Chi può ricevere una tipologia di notifica (più valori insieme).</summary>
[Flags]
public enum Destinatari
{
    Nessuno = 0,
    /// <summary>Consulenti dello studio (e super amministratore).</summary>
    Studio = 1,
    /// <summary>Referenti di un'azienda.</summary>
    Referente = 2,
    /// <summary>La persona del personale, per ciò che riguarda lei.</summary>
    Dipendente = 4,
    /// <summary>Un dipendente che approva le richieste di altri (dall'organigramma).</summary>
    Responsabile = 8,
}

/// <summary>
/// Tipologie di notifica. Ogni evento genera sempre la notifica in piattaforma; l'email parte se la persona non l'ha
/// spenta per quella tipologia (<see cref="PreferenzaNotifica"/>). Il nome è salvato come testo: al massimo 32 caratteri.
/// </summary>
public enum TipoNotifica
{
    AssenzaDaApprovare,
    AssenzaDecisa,
    CancellazioneDaDecidere,
    CancellazioneDecisa,
    RichiestaRevocata,
    ChiusuraAziendale,
    SmartWorkingDaApprovare,
    SmartWorkingDeciso,
    StraordinarioDaApprovare,
    StraordinarioDeciso,
    TurniPubblicati,
    CambioTurnoDaApprovare,
    CambioTurnoDeciso,
    PresenzeInviate,
    PresenzeDaInviare,
    PresenzeRiaperte,
    PresenzeElaborate,
    BustaPagaPubblicata,
    CuDisponibile,
    ContrattoCaricato,
    DocumentoPersonaleCaricato,
    DocumentoStoricoCaricato,
    DatiPersonaliAggiornati,
    NuovoContratto,
    Cessazione,
    ProfiloAziendaCompletato,
    AziendaAssegnata,
    ContrattoInScadenza,
    DocumentoInScadenza,
}

/// <summary>Come si presenta una tipologia nelle impostazioni: nome, spiegazione e chi la riceve.</summary>
public sealed record DefinizioneNotifica(TipoNotifica Tipo, CategoriaNotifica Categoria, string Nome, string Descrizione, Destinatari Destinatari);

/// <summary>Avvisi email che non si possono spegnere (riguardano l'accesso): compaiono nelle impostazioni come "sempre attivi".</summary>
public sealed record AvvisoSempreAttivo(string Nome, string Descrizione);

/// <summary>Catalogo di tutte le notifiche della piattaforma, nell'ordine in cui compaiono nelle impostazioni.</summary>
public static class CatalogoNotifiche
{
    public static readonly IReadOnlyList<DefinizioneNotifica> Tutte =
    [
        new(TipoNotifica.AssenzaDaApprovare, CategoriaNotifica.Richieste, "Nuova richiesta da approvare",
            "Una persona chiede ferie, permessi o un'altra assenza che devi approvare.", Destinatari.Referente | Destinatari.Responsabile),
        new(TipoNotifica.AssenzaDecisa, CategoriaNotifica.Richieste, "Richiesta approvata o rifiutata",
            "L'esito delle tue richieste di ferie e permessi, con il motivo di un eventuale rifiuto.", Destinatari.Dipendente),
        new(TipoNotifica.CancellazioneDaDecidere, CategoriaNotifica.Richieste, "Cancellazione da decidere",
            "Una persona chiede di cancellare un'assenza già approvata.", Destinatari.Referente | Destinatari.Responsabile),
        new(TipoNotifica.CancellazioneDecisa, CategoriaNotifica.Richieste, "Cancellazione confermata o respinta",
            "L'esito della cancellazione che hai chiesto.", Destinatari.Dipendente),
        new(TipoNotifica.RichiestaRevocata, CategoriaNotifica.Richieste, "Richiesta revocata",
            "L'azienda ha annullato una tua richiesta già registrata.", Destinatari.Dipendente),
        new(TipoNotifica.ChiusuraAziendale, CategoriaNotifica.Richieste, "Chiusura aziendale",
            "Una chiusura aziendale (es. ferie estive) ti ha registrato giorni di ferie.", Destinatari.Dipendente),

        new(TipoNotifica.SmartWorkingDaApprovare, CategoriaNotifica.SmartWorkingStraordinari, "Smart working da approvare",
            "Una persona chiede uno o più giorni di smart working.", Destinatari.Referente),
        new(TipoNotifica.SmartWorkingDeciso, CategoriaNotifica.SmartWorkingStraordinari, "Smart working approvato o rifiutato",
            "L'esito delle tue richieste di smart working.", Destinatari.Dipendente),
        new(TipoNotifica.StraordinarioDaApprovare, CategoriaNotifica.SmartWorkingStraordinari, "Straordinario da approvare",
            "Una persona segnala ore di straordinario.", Destinatari.Referente),
        new(TipoNotifica.StraordinarioDeciso, CategoriaNotifica.SmartWorkingStraordinari, "Straordinario approvato o rifiutato",
            "L'esito degli straordinari che hai segnalato.", Destinatari.Dipendente),

        new(TipoNotifica.TurniPubblicati, CategoriaNotifica.Turni, "Turni pubblicati",
            "L'azienda ha pubblicato nuovi turni di lavoro per te.", Destinatari.Dipendente),
        new(TipoNotifica.CambioTurnoDaApprovare, CategoriaNotifica.Turni, "Cambio turno da approvare",
            "Una persona chiede di modificare o scambiare un turno.", Destinatari.Referente),
        new(TipoNotifica.CambioTurnoDeciso, CategoriaNotifica.Turni, "Cambio turno approvato o rifiutato",
            "L'esito di un cambio o di uno scambio di turno che ti riguarda.", Destinatari.Dipendente),

        new(TipoNotifica.PresenzeInviate, CategoriaNotifica.Presenze, "Presenze inviate",
            "Un'azienda ha inviato presenze e variazioni del mese.", Destinatari.Studio),
        new(TipoNotifica.PresenzeDaInviare, CategoriaNotifica.Presenze, "Promemoria presenze",
            "Il consulente ricorda di inviare presenze e variazioni del mese.", Destinatari.Referente),
        new(TipoNotifica.PresenzeRiaperte, CategoriaNotifica.Presenze, "Presenze da correggere",
            "Il consulente ha riaperto le presenze del mese per una correzione.", Destinatari.Referente),
        new(TipoNotifica.PresenzeElaborate, CategoriaNotifica.Presenze, "Presenze elaborate",
            "Lo studio ha elaborato le presenze del mese.", Destinatari.Referente),

        new(TipoNotifica.BustaPagaPubblicata, CategoriaNotifica.Documenti, "Nuova busta paga",
            "È disponibile un tuo cedolino.", Destinatari.Dipendente),
        new(TipoNotifica.CuDisponibile, CategoriaNotifica.Documenti, "Nuova Certificazione Unica",
            "È disponibile la tua CU.", Destinatari.Dipendente),
        new(TipoNotifica.ContrattoCaricato, CategoriaNotifica.Documenti, "Contratto o lettera di variazione",
            "È disponibile il PDF del tuo contratto o di una lettera (proroga, trasformazione…).", Destinatari.Dipendente),
        new(TipoNotifica.DocumentoPersonaleCaricato, CategoriaNotifica.Documenti, "Documento personale caricato",
            "Un nuovo documento personale nel portale: caricato dalla persona, o dall'azienda per te.", Destinatari.Studio | Destinatari.Referente | Destinatari.Dipendente),
        new(TipoNotifica.DocumentoStoricoCaricato, CategoriaNotifica.Documenti, "Archivio storico",
            "Un nuovo documento nell'archivio storico dell'azienda (F24, DM10, autoliquidazione…).", Destinatari.Studio | Destinatari.Referente),

        new(TipoNotifica.DatiPersonaliAggiornati, CategoriaNotifica.Personale, "Dati personali compilati o modificati",
            "Una persona ha completato o aggiornato i dati per le paghe.", Destinatari.Studio | Destinatari.Referente),
        new(TipoNotifica.NuovoContratto, CategoriaNotifica.Personale, "Nuovo contratto o incarico",
            "È stato registrato un contratto per una persona dell'azienda.", Destinatari.Studio | Destinatari.Referente),
        new(TipoNotifica.Cessazione, CategoriaNotifica.Personale, "Cessazione del rapporto",
            "È stata registrata la cessazione di un rapporto di lavoro.", Destinatari.Studio | Destinatari.Referente),

        new(TipoNotifica.ProfiloAziendaCompletato, CategoriaNotifica.Aziende, "Profilo azienda completato",
            "Un'azienda ha completato il percorso guidato del profilo.", Destinatari.Studio),
        new(TipoNotifica.AziendaAssegnata, CategoriaNotifica.Aziende, "Azienda assegnata",
            "Il super amministratore ti ha assegnato un'azienda da seguire.", Destinatari.Studio),

        new(TipoNotifica.ContrattoInScadenza, CategoriaNotifica.Scadenze, "Contratto in scadenza",
            "Un contratto a termine finisce entro 30 giorni e non c'è ancora un contratto successivo.", Destinatari.Studio | Destinatari.Referente),
        new(TipoNotifica.DocumentoInScadenza, CategoriaNotifica.Scadenze, "Documento in scadenza",
            "Un documento personale (identità, permesso di soggiorno…) scade entro 30 giorni.", Destinatari.Studio | Destinatari.Referente | Destinatari.Dipendente),
    ];

    public static readonly IReadOnlyList<AvvisoSempreAttivo> SempreAttivi =
    [
        new("Invito e link per scegliere la password", "Arriva quando ti viene creato un accesso al portale."),
        new("Recupero della password", "Arriva quando chiedi di reimpostare la password."),
        new("Cambio dell'email di accesso", "Arriva al nuovo indirizzo quando cambia l'email con cui accedi."),
    ];

    private static readonly Dictionary<TipoNotifica, DefinizioneNotifica> PerTipo = Tutte.ToDictionary(d => d.Tipo);

    public static DefinizioneNotifica Di(TipoNotifica tipo) => PerTipo[tipo];

    /// <summary>Le tipologie che può ricevere chi ha almeno uno dei ruoli indicati.</summary>
    public static IEnumerable<DefinizioneNotifica> Per(Destinatari chi) => Tutte.Where(d => (d.Destinatari & chi) != 0);
}

public static class CategoriaNotificaExtensions
{
    public static string Descrizione(this CategoriaNotifica c) => c switch
    {
        CategoriaNotifica.Richieste => "Ferie e permessi",
        CategoriaNotifica.SmartWorkingStraordinari => "Smart working e straordinari",
        CategoriaNotifica.Turni => "Turni di lavoro",
        CategoriaNotifica.Presenze => "Presenze del mese",
        CategoriaNotifica.Documenti => "Documenti",
        CategoriaNotifica.Personale => "Personale",
        CategoriaNotifica.Aziende => "Aziende",
        _ => "Scadenze",
    };

    public static CategoriaNotifica Categoria(this TipoNotifica t) => CatalogoNotifiche.Di(t).Categoria;
}
