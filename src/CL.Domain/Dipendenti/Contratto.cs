using CL.Domain.Common;

namespace CL.Domain.Dipendenti;

public static class TipoContrattoExtensions
{
    public static string Descrizione(this TipoContratto tipo) => tipo switch
    {
        TipoContratto.TempoIndeterminato => "Tempo indeterminato",
        TipoContratto.TempoDeterminato => "Tempo determinato",
        TipoContratto.Apprendistato => "Apprendistato",
        TipoContratto.Intermittente => "Intermittente",
        TipoContratto.Somministrazione => "Somministrazione",
        TipoContratto.Collaborazione => "Collaborazione (co.co.co.)",
        TipoContratto.LavoroAutonomo => "Partita IVA / lavoro autonomo",
        TipoContratto.PrestazioneOccasionale => "Prestazione occasionale",
        TipoContratto.Tirocinio => "Tirocinio / stage",
        TipoContratto.Altro => "Altro rapporto non dipendente",
        _ => tipo.ToString(),
    };

    /// <summary>Lavoro dipendente (subordinato): matura ferie e permessi e compare nel foglio presenze.</summary>
    public static bool IsSubordinato(this TipoContratto tipo) => tipo is TipoContratto.TempoIndeterminato or TipoContratto.TempoDeterminato
        or TipoContratto.Apprendistato or TipoContratto.Intermittente or TipoContratto.Somministrazione;

    /// <summary>Tipi dei professionisti esterni (collaborazioni, partite IVA, occasionali, tirocini, altro).</summary>
    public static readonly IReadOnlyList<TipoContratto> Esterni =
        [TipoContratto.Collaborazione, TipoContratto.LavoroAutonomo, TipoContratto.PrestazioneOccasionale, TipoContratto.Tirocinio, TipoContratto.Altro];
}

/// <summary>Dove si svolge il lavoro: in sede, in parte da remoto (con i giorni in ufficio) o del tutto da remoto.</summary>
public enum ModalitaLavoro { InPresenza, Ibrido, FullRemote }

/// <summary>Periodo a cui si riferiscono i giorni di smart working concessi a chi lavora in presenza.</summary>
public enum PeriodoSmartWorking { Settimana, Mese }

public static class ModalitaLavoroExtensions
{
    /// <summary>Es. "1 giorno a settimana", "2 giorni al mese".</summary>
    public static string Descrizione(int giorni, PeriodoSmartWorking periodo) =>
        $"{giorni} {(giorni == 1 ? "giorno" : "giorni")} {(periodo == PeriodoSmartWorking.Settimana ? "a settimana" : "al mese")}";

    public static string Descrizione(this ModalitaLavoro m) => m switch
    {
        ModalitaLavoro.Ibrido => "Ibrido",
        ModalitaLavoro.FullRemote => "Full-remote",
        _ => "In presenza",
    };
}

public enum TipoContratto
{
    TempoIndeterminato,
    TempoDeterminato,
    Apprendistato,
    Intermittente,
    Somministrazione,
    // Professionisti esterni (non lavoro dipendente)
    Collaborazione,
    LavoroAutonomo,
    PrestazioneOccasionale,
    Tirocinio,
    Altro,
}

/// <summary>
/// Rapporto di lavoro con le condizioni economiche e normative.
/// Le spettanze annue di ferie e ROL sono in ore riferite al full-time, come da CCNL;
/// per i part-time vengono riproporzionate con <see cref="SpettanzaAnnua"/>.
/// </summary>
public sealed class Contratto : Entity
{
    public Guid DipendenteId { get; private set; }
    public TipoContratto Tipo { get; private set; }
    public string Ccnl { get; private set; } = null!;
    public string Livello { get; private set; } = null!;
    public string? Mansione { get; private set; }
    public DateOnly DataInizio { get; private set; }
    public DateOnly? DataFine { get; private set; }
    /// <summary>Data della cessazione anticipata (dimissioni, licenziamento…); null se il contratto è arrivato a scadenza o è in corso.</summary>
    public DateOnly? DataCessazione { get; private set; }

    /// <summary>Orario settimanale del full-time da CCNL (es. 40).</summary>
    public decimal OreSettimanaliFullTime { get; private set; }
    /// <summary>Percentuale part-time (100 = full-time).</summary>
    public decimal PercentualePartTime { get; private set; }
    /// <summary>Giorni lavorativi a settimana (5 o 6).</summary>
    public int GiorniLavorativiSettimana { get; private set; }

    public decimal RetribuzioneLordaAnnua { get; private set; }
    public int MensilitaAnnue { get; private set; }

    public decimal OreFerieAnnue { get; private set; }
    public decimal OreRolAnnue { get; private set; }

    public ModalitaLavoro Modalita { get; private set; }
    /// <summary>Giorni in ufficio a settimana, solo per la modalità ibrida.</summary>
    public int? GiorniInUfficio { get; private set; }
    /// <summary>Giorni di smart working che chi lavora in presenza può chiedere (per settimana o per mese); null = nessuno.</summary>
    public int? GiorniSmartWorking { get; private set; }
    public PeriodoSmartWorking? PeriodoSmartWorking { get; private set; }

    /// <summary>Es. "2 giorni al mese"; null se non concesso.</summary>
    public string? SmartWorkingConcesso => GiorniSmartWorking is { } g && PeriodoSmartWorking is { } p ? ModalitaLavoroExtensions.Descrizione(g, p) : null;

    /// <summary>Contratto firmato (PDF) nell'archivio privato.</summary>
    public string? DocumentoChiave { get; private set; }
    /// <summary>Nome del file come l'ha caricato l'utente, per il download.</summary>
    public string? DocumentoNome { get; private set; }
    public DateTime? DocumentoCaricatoIl { get; private set; }

    private Contratto() { } // EF Core

    internal Contratto(Guid dipendenteId, TipoContratto tipo, string ccnl, string livello,
        DateOnly dataInizio, DateOnly? dataFine, decimal oreSettimanaliFullTime, decimal percentualePartTime,
        int giorniLavorativiSettimana, decimal retribuzioneLordaAnnua, int mensilitaAnnue,
        decimal oreFerieAnnue, decimal oreRolAnnue, string? mansione,
        ModalitaLavoro modalita = ModalitaLavoro.InPresenza, int? giorniInUfficio = null)
    {
        DipendenteId = dipendenteId;
        Imposta(tipo, ccnl, livello, dataInizio, dataFine, oreSettimanaliFullTime, percentualePartTime, giorniLavorativiSettimana,
            retribuzioneLordaAnnua, mensilitaAnnue, oreFerieAnnue, oreRolAnnue, mansione, modalita, giorniInUfficio);
    }

    /// <summary>Condizioni del contratto (alla registrazione o in una correzione successiva), con gli stessi controlli.</summary>
    internal void Imposta(TipoContratto tipo, string ccnl, string livello,
        DateOnly dataInizio, DateOnly? dataFine, decimal oreSettimanaliFullTime, decimal percentualePartTime,
        int giorniLavorativiSettimana, decimal retribuzioneLordaAnnua, int mensilitaAnnue,
        decimal oreFerieAnnue, decimal oreRolAnnue, string? mansione,
        ModalitaLavoro modalita = ModalitaLavoro.InPresenza, int? giorniInUfficio = null)
    {
        if (modalita == ModalitaLavoro.Ibrido)
        {
            if (giorniInUfficio is not { } g) throw new DomainException("Per il lavoro ibrido indica i giorni in ufficio a settimana.");
            if (g < 1 || g >= giorniLavorativiSettimana)
                throw new DomainException($"I giorni in ufficio devono essere tra 1 e {giorniLavorativiSettimana - 1} (altrimenti è in presenza).");
        }
        if (tipo == TipoContratto.TempoDeterminato && dataFine is null)
            throw new DomainException("Un contratto a tempo determinato richiede una data di fine.");
        if (dataFine is { } f && f < dataInizio)
            throw new DomainException("La data di fine contratto precede la data di inizio.");
        if (oreSettimanaliFullTime is <= 0 or > 48)
            throw new DomainException("L'orario settimanale deve essere compreso tra 0 e 48 ore.");
        if (percentualePartTime is <= 0 or > 100)
            throw new DomainException("La percentuale part-time deve essere compresa tra 1 e 100.");
        if (giorniLavorativiSettimana is not (5 or 6))
            throw new DomainException("I giorni lavorativi settimanali devono essere 5 o 6.");
        if (mensilitaAnnue is < 12 or > 16)
            throw new DomainException("Le mensilità annue devono essere comprese tra 12 e 16.");
        // Tutti i controlli prima di cambiare qualcosa: una correzione rifiutata lascia il contratto com'era.
        if (tipo.IsSubordinato()) { Guard.NotEmpty(ccnl, "CCNL"); Guard.NotEmpty(livello, "Livello"); }
        Guard.NonNegative(retribuzioneLordaAnnua, "Retribuzione lorda annua");
        Guard.NonNegative(oreFerieAnnue, "Ore ferie annue");
        Guard.NonNegative(oreRolAnnue, "Ore ROL annue");

        Tipo = tipo;
        if (tipo.IsSubordinato())
        {
            Ccnl = Guard.NotEmpty(ccnl, "CCNL");
            Livello = Guard.NotEmpty(livello, "Livello");
        }
        else
        {
            // Rapporti esterni: CCNL e livello facoltativi, niente ferie né ROL da maturare.
            Ccnl = ccnl?.Trim() ?? "";
            Livello = livello?.Trim() ?? "";
            (oreFerieAnnue, oreRolAnnue) = (0, 0);
        }
        Mansione = mansione?.Trim();
        DataInizio = dataInizio;
        DataFine = dataFine;
        OreSettimanaliFullTime = oreSettimanaliFullTime;
        PercentualePartTime = percentualePartTime;
        GiorniLavorativiSettimana = giorniLavorativiSettimana;
        RetribuzioneLordaAnnua = Guard.NonNegative(retribuzioneLordaAnnua, "Retribuzione lorda annua");
        MensilitaAnnue = mensilitaAnnue;
        OreFerieAnnue = Guard.NonNegative(oreFerieAnnue, "Ore ferie annue");
        OreRolAnnue = Guard.NonNegative(oreRolAnnue, "Ore ROL annue");
        Modalita = modalita;
        GiorniInUfficio = modalita == ModalitaLavoro.Ibrido ? giorniInUfficio : null;
        if (modalita != ModalitaLavoro.InPresenza) (GiorniSmartWorking, PeriodoSmartWorking) = (null, null);
        // Una data di fine cambiata a mano non è più quella della cessazione registrata.
        if (DataCessazione is not null && DataCessazione != dataFine) DataCessazione = null;
    }

    public decimal OreSettimanali => Math.Round(OreSettimanaliFullTime * PercentualePartTime / 100m, 2);

    /// <summary>Ore di una giornata lavorativa ordinaria (part-time orizzontale).</summary>
    public decimal OreGiornaliere => Math.Round(OreSettimanali / GiorniLavorativiSettimana, 2);

    public decimal RetribuzioneMensileLorda => Math.Round(RetribuzioneLordaAnnua / MensilitaAnnue, 2);

    /// <summary>Spettanza annua effettiva in ore, riproporzionata al part-time.</summary>
    public decimal SpettanzaAnnua(Assenze.TipoAssenza tipo) => Math.Round(tipo switch
    {
        Assenze.TipoAssenza.Ferie => OreFerieAnnue,
        Assenze.TipoAssenza.Rol => OreRolAnnue,
        _ => 0m,
    } * PercentualePartTime / 100m, 2);

public void ImpostaDocumento(string chiave, string nomeFile, DateTime adesso)
    {
        DocumentoChiave = Guard.NotEmpty(chiave, "Documento");
        var nome = Path.GetFileName(nomeFile ?? "").Trim();
        DocumentoNome = string.IsNullOrEmpty(nome) ? "contratto.pdf" : nome.Length > 200 ? nome[..200] : nome;
        DocumentoCaricatoIl = adesso;
    }

    /// <summary>Smart working concesso a chi lavora in presenza (es. 2 giorni al mese); vuoto o 0 = nessuno.</summary>
    public void ImpostaSmartWorking(int? giorni, PeriodoSmartWorking periodo)
    {
        if (Modalita != ModalitaLavoro.InPresenza || giorni is null or 0) { (GiorniSmartWorking, PeriodoSmartWorking) = (null, null); return; }
        var massimo = periodo == Dipendenti.PeriodoSmartWorking.Settimana ? 5 : 23;
        if (giorni < 0 || giorni > massimo)
            throw new DomainException($"Giorni di smart working: da 1 a {massimo} {(periodo == Dipendenti.PeriodoSmartWorking.Settimana ? "a settimana" : "al mese")}.");
        (GiorniSmartWorking, PeriodoSmartWorking) = (giorni, periodo);
    }

    /// <summary>Mansione svolta (es. "Designer"); vuota = non indicata.</summary>
    public void ImpostaMansione(string? mansione)
    {
        var m = string.IsNullOrWhiteSpace(mansione) ? null : mansione.Trim();
        if (m is { Length: > 150 }) throw new DomainException("Mansione troppo lunga (massimo 150 caratteri).");
        Mansione = m;
    }

    public void RimuoviDocumento() => (DocumentoChiave, DocumentoNome, DocumentoCaricatoIl) = (null, null, null);

        public bool AttivoIl(DateOnly giorno) => giorno >= DataInizio && (DataFine is null || giorno <= DataFine);

    internal void Chiudi(DateOnly dataFine)
    {
        if (dataFine < DataInizio)
            throw new DomainException("La data di chiusura precede l'inizio del contratto.");
        DataFine = dataFine;
        DataCessazione = dataFine;
    }
}
