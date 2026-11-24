using CL.Domain.Common;
using CL.Domain.ValueObjects;

namespace CL.Domain.Dipendenti;

/// <summary>Progressivi alla data del subentro: ratei di 13ª e 14ª maturati, TFR rimasto in azienda e versato ai fondi.</summary>
public sealed record ProgressiviSubentro(DateOnly Al, decimal? Ratei13, decimal? Ratei14, decimal? TfrAzienda, decimal? TfrFondi);

/// <summary>Genere dichiarato dalla persona (facoltativo).</summary>
public enum Genere { Donna, Uomo, NonBinario, NonIndicato }

/// <summary>Sede di lavoro abituale: una delle sedi censite dall'azienda oppure smart working.</summary>
/// <summary>Dove lavora la persona. "SedeOperativa" è dei profili vecchi: ora si usa un'unità locale.</summary>
public enum SedeLavoro { SedeLegale, SedeOperativa, SmartWorking, UnitaLocale }

/// <summary>Pronomi dichiarati dalla persona (facoltativi).</summary>
public enum Pronomi { Femminili, Maschili, Neutri, Tutti, NonIndicati }

public static class IdentitaExtensions
{
    public static string Descrizione(this Genere g) => g switch
    {
        Genere.Donna => "Donna",
        Genere.Uomo => "Uomo",
        Genere.NonBinario => "Non binario",
        _ => "Preferisco non indicarlo",
    };

    public static string Descrizione(this Pronomi p) => p switch
    {
        Pronomi.Femminili => "Femminili (lei)",
        Pronomi.Maschili => "Maschili (lui)",
        Pronomi.Neutri => "Neutri",
        Pronomi.Tutti => "Vanno bene tutti",
        _ => "Preferisco non indicarli",
    };
}

/// <summary>Anagrafica del lavoratore e storico dei contratti.</summary>
public sealed class Dipendente : Entity
{
    private readonly List<Contratto> _contratti = [];

    public Guid AziendaId { get; private set; }
    public string Nome { get; private set; } = null!;
    public string Cognome { get; private set; } = null!;
    public CodiceFiscale CodiceFiscale { get; private set; } = null!;
    public DateOnly DataNascita { get; private set; }
    public string? Email { get; private set; }
    public string? Telefono { get; private set; }
    /// <summary>Via e numero civico della residenza.</summary>
    public string? Indirizzo { get; private set; }
    public string? Cap { get; private set; }
    public string? Comune { get; private set; }
    /// <summary>Sigla della provincia (es. MI).</summary>
    public string? Provincia { get; private set; }
    public Iban? Iban { get; private set; }
    public string? Matricola { get; private set; }
    /// <summary>Utente Identity collegato (portale dipendente), se abilitato.</summary>
    public string? UserId { get; private set; }

    /// <summary>Team di appartenenza nell'organigramma dell'azienda.</summary>
    public Guid? TeamId { get; private set; }
    /// <summary>Posizione/ruolo nell'organizzazione (es. "Capo reparto"); distinto dalla mansione contrattuale.</summary>
    public string? Posizione { get; private set; }
    /// <summary>Responsabile diretto: se indicato, approva lui ferie e permessi (prevale sul responsabile del team).</summary>
    public Guid? ResponsabileId { get; private set; }
    /// <summary>Foto nell'archivio privato (chiave generata dall'applicazione), se caricata.</summary>
    public string? FotoChiave { get; private set; }
    /// <summary>Genere e pronomi dichiarati dalla persona: mai dedotti dal codice fiscale.</summary>
    public Genere? Genere { get; private set; }
    public SedeLavoro? Sede { get; private set; }
    public Pronomi? Pronomi { get; private set; }

    /// <summary>
    /// Segnaposto da mostrare senza foto: "donna", "uomo" o "neutro". Contano prima i pronomi
    /// (femminili / maschili; neutri o "tutti" → neutro), poi, se non indicati, il genere; senza dati il neutro.
    /// </summary>
    public string Segnaposto => SegnapostoPer(Genere, Pronomi);

    public static string SegnapostoPer(Genere? genere, Pronomi? pronomi) => pronomi switch
    {
        Dipendenti.Pronomi.Femminili => "donna",
        Dipendenti.Pronomi.Maschili => "uomo",
        Dipendenti.Pronomi.Neutri or Dipendenti.Pronomi.Tutti => "neutro",
        _ => genere switch
        {
            Dipendenti.Genere.Donna => "donna",
            Dipendenti.Genere.Uomo => "uomo",
            _ => "neutro",
        },
    };

    public IReadOnlyCollection<Contratto> Contratti => _contratti.AsReadOnly();

    public string NomeCompleto => $"{Cognome} {Nome}";

    private Dipendente() { } // EF Core

    public Dipendente(Guid aziendaId, string nome, string cognome, CodiceFiscale codiceFiscale, DateOnly dataNascita)
    {
        if (aziendaId == Guid.Empty) throw new DomainException("Azienda obbligatoria.");
        AziendaId = aziendaId;
        Nome = Guard.NotEmpty(nome, "Nome");
        Cognome = Guard.NotEmpty(cognome, "Cognome");
        CodiceFiscale = codiceFiscale;
        DataNascita = dataNascita;
    }

    /// <summary>Correzione di errori di inserimento nei dati anagrafici di base.</summary>
    public void CorreggiAnagrafica(string nome, string cognome, CodiceFiscale codiceFiscale, DateOnly dataNascita)
    {
        if (_contratti.Any(c => c.DataInizio < dataNascita.AddYears(16)))
            throw new DomainException("Con questa data di nascita un contratto risulterebbe stipulato prima dei 16 anni.");
        Nome = Guard.NotEmpty(nome, "Nome");
        Cognome = Guard.NotEmpty(cognome, "Cognome");
        CodiceFiscale = codiceFiscale;
        DataNascita = dataNascita;
    }

    public void AggiornaRecapiti(string? email, string? telefono)
    {
        Email = email?.Trim();
        Telefono = telefono?.Trim();
    }

    /// <summary>Residenza: via e civico, CAP, comune e sigla di provincia, tutti insieme (o nessuno).</summary>
    public void ImpostaResidenza(Aziende.Indirizzo? residenza)
    {
        var r = residenza?.Validato("di residenza");
        (Indirizzo, Cap, Comune, Provincia) = (r?.Via, r?.Cap, r?.Comune, r?.Provincia);
    }

    public void ImpostaIban(Iban? iban) => Iban = iban;
    public void ImpostaSede(SedeLavoro? sede) => ImpostaSede(sede, null);

    // Progressivi al cambio consulente: ratei e TFR maturati fino alla data del subentro, dall'ultimo cedolino del consulente precedente.
    public DateOnly? ProgressiviAl { get; private set; }
    public decimal? Ratei13 { get; private set; }
    public decimal? Ratei14 { get; private set; }
    public decimal? TfrAzienda { get; private set; }
    public decimal? TfrFondi { get; private set; }

    /// <summary>Imposta (o con null toglie) i progressivi al subentro.</summary>
    public void ImpostaProgressivi(ProgressiviSubentro? p)
    {
        if (p is not null)
        {
            if (p.Ratei13 is < 0 or > 12 || p.Ratei14 is < 0 or > 12) throw new DomainException("I ratei vanno da 0 a 12.");
            if (p.TfrAzienda < 0 || p.TfrFondi < 0) throw new DomainException("Il TFR non può essere negativo.");
        }
        (ProgressiviAl, Ratei13, Ratei14, TfrAzienda, TfrFondi) = (p?.Al, p?.Ratei13, p?.Ratei14, p?.TfrAzienda, p?.TfrFondi);
    }

    public ProgressiviSubentro? Progressivi => ProgressiviAl is { } al ? new(al, Ratei13, Ratei14, TfrAzienda, TfrFondi) : null;

    /// <summary>Unità locale dell'azienda in cui lavora (con <see cref="SedeLavoro.UnitaLocale"/>).</summary>
    public Guid? UnitaLocaleId { get; private set; }
    public void ImpostaSede(SedeLavoro? sede, Guid? unitaLocaleId)
    {
        if (sede == SedeLavoro.UnitaLocale && unitaLocaleId is null) throw new DomainException("Indica l'unità locale.");
        Sede = sede;
        UnitaLocaleId = sede == SedeLavoro.UnitaLocale ? unitaLocaleId : null;
    }

    /// <summary>La persona (dipendente o professionista) a cui il consulente chiede presenze, variazioni e documenti ogni mese.</summary>
    public bool ReferentePaghe { get; private set; }
    public void ImpostaReferentePaghe(bool si) => ReferentePaghe = si;
    public void ImpostaMatricola(string? matricola) => Matricola = matricola?.Trim();
    public void CollegaUtente(string? userId) => UserId = userId;
    public void ImpostaFoto(string chiave) => FotoChiave = Guard.NotEmpty(chiave, "Foto");
    public void RimuoviFoto() => FotoChiave = null;
    public void ImpostaIdentita(Genere? genere, Pronomi? pronomi) => (Genere, Pronomi) = (genere, pronomi);

    public void ImpostaOrganizzazione(Organizzazione.Team? team, string? posizione, Dipendente? responsabile)
    {
        if (team is not null && team.AziendaId != AziendaId) throw new DomainException("Il team deve essere della stessa azienda.");
        if (responsabile is not null && responsabile.AziendaId != AziendaId)
            throw new DomainException("Il responsabile deve essere un dipendente della stessa azienda.");
        if (responsabile?.Id == Id) throw new DomainException("Un dipendente non può essere responsabile di sé stesso.");
        var p = Guard.Facoltativo(posizione);
        if (p?.Length > 100) throw new DomainException("Posizione troppo lunga (massimo 100 caratteri).");
        TeamId = team?.Id;
        Posizione = p;
        ResponsabileId = responsabile?.Id;
    }

    public Contratto Assumi(TipoContratto tipo, string ccnl, string livello, DateOnly dataInizio, DateOnly? dataFine,
        decimal retribuzioneLordaAnnua, int mensilitaAnnue = 13, decimal oreSettimanaliFullTime = 40,
        decimal percentualePartTime = 100, int giorniLavorativiSettimana = 5,
        decimal oreFerieAnnue = 160, decimal oreRolAnnue = 72, string? mansione = null,
        ModalitaLavoro modalita = ModalitaLavoro.InPresenza, int? giorniInUfficio = null)
    {
        VerificaPeriodo(dataInizio, dataFine, escluso: null, "Esiste già un contratto attivo nel periodo indicato.");
        var contratto = new Contratto(Id, tipo, ccnl, livello, dataInizio, dataFine, oreSettimanaliFullTime,
            percentualePartTime, giorniLavorativiSettimana, retribuzioneLordaAnnua, mensilitaAnnue,
            oreFerieAnnue, oreRolAnnue, mansione, modalita, giorniInUfficio);
        _contratti.Add(contratto);
        return contratto;
    }

    /// <summary>Correzione delle condizioni di un contratto già registrato (stessi controlli dell'assunzione).</summary>
    public void ModificaContratto(Guid contrattoId, TipoContratto tipo, string ccnl, string livello, DateOnly dataInizio, DateOnly? dataFine,
        decimal retribuzioneLordaAnnua, int mensilitaAnnue, decimal oreSettimanaliFullTime, decimal percentualePartTime,
        int giorniLavorativiSettimana, decimal oreFerieAnnue, decimal oreRolAnnue, string? mansione,
        ModalitaLavoro modalita = ModalitaLavoro.InPresenza, int? giorniInUfficio = null)
    {
        var contratto = _contratti.FirstOrDefault(c => c.Id == contrattoId) ?? throw new DomainException("Contratto non trovato.");
        VerificaPeriodo(dataInizio, dataFine, escluso: contrattoId, "Il periodo si sovrappone a un altro contratto della persona.");
        contratto.Imposta(tipo, ccnl, livello, dataInizio, dataFine, oreSettimanaliFullTime, percentualePartTime, giorniLavorativiSettimana,
            retribuzioneLordaAnnua, mensilitaAnnue, oreFerieAnnue, oreRolAnnue, mansione, modalita, giorniInUfficio);
    }

    /// <summary>
    /// Almeno 16 anni all'inizio e nessuna sovrapposizione con gli altri contratti della persona
    /// (<paramref name="escluso"/> è quello che si sta correggendo; senza data di fine un contratto è aperto).
    /// </summary>
    private void VerificaPeriodo(DateOnly dataInizio, DateOnly? dataFine, Guid? escluso, string seSovrapposto)
    {
        if (dataInizio < DataNascita.AddYears(16))
            throw new DomainException("Il lavoratore deve avere almeno 16 anni alla data di assunzione.");
        var fine = dataFine ?? DateOnly.MaxValue;
        if (_contratti.Any(c => c.Id != escluso && c.DataInizio <= fine && dataInizio <= (c.DataFine ?? DateOnly.MaxValue)))
            throw new DomainException(seSovrapposto);
    }

    public void Cessa(DateOnly dataCessazione)
    {
        var attivo = ContrattoAttivo(dataCessazione)
            ?? throw new DomainException("Nessun contratto attivo alla data di cessazione.");
        attivo.Chiudi(dataCessazione);
    }

    public Contratto? ContrattoAttivo(DateOnly giorno) => _contratti.FirstOrDefault(c => c.AttivoIl(giorno));

    public bool InForzaIl(DateOnly giorno) => ContrattoAttivo(giorno) is not null;
}
