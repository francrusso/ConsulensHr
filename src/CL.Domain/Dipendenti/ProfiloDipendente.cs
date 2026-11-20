using CL.Domain.Aziende;
using CL.Domain.Common;
using CL.Domain.ValueObjects;

namespace CL.Domain.Dipendenti;

public enum DestinazioneTfr
{
    /// <summary>In azienda (o al Fondo di Tesoreria INPS per le aziende con almeno 50 dipendenti).</summary>
    Azienda,
    FondoPensione,
}

public enum RelazioneFamiliare { Coniuge, Figlio, AltroFamiliare }

public enum TitoloStudio
{
    Nessuno, LicenzaMedia, QualificaProfessionale, Diploma, LaureaTriennale, LaureaMagistrale, Dottorato,
}

public sealed record DatiAnagrafici(string ComuneNascita, string ProvinciaNascita, string Cittadinanza, string? Telefono, string? EmailPersonale);

public sealed record DatiFamiliare(RelazioneFamiliare Relazione, string Nome, string Cognome, CodiceFiscale CodiceFiscale,
    DateOnly DataNascita, int PercentualeCarico, bool Disabile);

public sealed record AltriDati(TitoloStudio TitoloStudio, string TipoDocumento, string NumeroDocumento, DateOnly ScadenzaDocumento,
    DateOnly? ScadenzaPermessoSoggiorno);

/// <summary>
/// Dati che il dipendente fornisce per le paghe, raccolti col percorso guidato al primo accesso.
/// Dopo il completamento il dipendente può aggiornarli: ogni modifica è datata e lo studio viene avvisato.
/// </summary>
public sealed class ProfiloDipendente : Entity
{
    public const int UltimoPasso = 6;
    public const int MaxFamiliari = 12;
    private readonly List<FamiliareACarico> _familiari = [];

    public Guid DipendenteId { get; private set; }
    public int Passo { get; private set; } = 1;

    // 1. Anagrafica
    public string? ComuneNascita { get; private set; }
    public string? ProvinciaNascita { get; private set; }
    public string? Cittadinanza { get; private set; }
    public string? Telefono { get; private set; }
    public string? EmailPersonale { get; private set; }

    // 2. Residenza e domicilio
    public string? ResidenzaVia { get; private set; }
    public string? ResidenzaCap { get; private set; }
    public string? ResidenzaComune { get; private set; }
    public string? ResidenzaProvincia { get; private set; }
    public string? DomicilioVia { get; private set; }
    public string? DomicilioCap { get; private set; }
    public string? DomicilioComune { get; private set; }
    public string? DomicilioProvincia { get; private set; }

    // 3. Pagamento e TFR
    public Iban? Iban { get; private set; }
    public string? IntestatarioConto { get; private set; }
    public DestinazioneTfr? Tfr { get; private set; }
    public string? FondoPensione { get; private set; }

    // 4. Detrazioni e familiari
    public bool? RichiedeDetrazioni { get; private set; }
    public IReadOnlyCollection<FamiliareACarico> Familiari => _familiari.AsReadOnly();

    // 5. Altri dati
    public TitoloStudio? TitoloStudio { get; private set; }
    public string? TipoDocumento { get; private set; }
    public string? NumeroDocumento { get; private set; }
    public DateOnly? ScadenzaDocumento { get; private set; }
    public DateOnly? ScadenzaPermessoSoggiorno { get; private set; }

    // 6. Conferma e modifiche
    public DateTime? CompletatoIl { get; private set; }
    public DateTime? AggiornatoIl { get; private set; }
    public string? AggiornatoDa { get; private set; }

    public bool Completato => CompletatoIl is not null;

    private ProfiloDipendente() { } // EF Core

    public ProfiloDipendente(Guid dipendenteId) => DipendenteId = dipendenteId;

    public Indirizzo? Residenza => ResidenzaVia is null ? null : new(ResidenzaVia, ResidenzaCap!, ResidenzaComune!, ResidenzaProvincia!);
    public Indirizzo? Domicilio => DomicilioVia is null ? null : new(DomicilioVia, DomicilioCap!, DomicilioComune!, DomicilioProvincia!);

    public void ImpostaAnagrafica(DatiAnagrafici d, string da, DateTime adesso)
    {
        ComuneNascita = Guard.NotEmpty(d.ComuneNascita, "Comune di nascita");
        var prov = Guard.NotEmpty(d.ProvinciaNascita, "Provincia di nascita").ToUpperInvariant();
        if (!Province.IsValida(prov, ammessoEstero: true)) throw new DomainException("Provincia di nascita: usa la sigla (EE se sei nato all'estero).");
        ProvinciaNascita = prov;
        Cittadinanza = Guard.NotEmpty(d.Cittadinanza, "Cittadinanza");
        Telefono = Pulito(d.Telefono);
        EmailPersonale = Pulito(d.EmailPersonale)?.ToLowerInvariant();
        Salvato(1, da, adesso);
    }

    public void ImpostaResidenza(Indirizzo residenza, Indirizzo? domicilio, string da, DateTime adesso)
    {
        var r = residenza.Validato("di residenza");
        (ResidenzaVia, ResidenzaCap, ResidenzaComune, ResidenzaProvincia) = (r.Via, r.Cap, r.Comune, r.Provincia);
        var d = domicilio?.Validato("del domicilio");
        (DomicilioVia, DomicilioCap, DomicilioComune, DomicilioProvincia) = (d?.Via, d?.Cap, d?.Comune, d?.Provincia);
        Salvato(2, da, adesso);
    }

    public void ImpostaPagamento(Iban iban, string? intestatario, DestinazioneTfr tfr, string? fondoPensione, string da, DateTime adesso)
    {
        if (tfr == DestinazioneTfr.FondoPensione && string.IsNullOrWhiteSpace(fondoPensione))
            throw new DomainException("Indica il fondo pensione a cui destinare il TFR.");
        Iban = iban;
        IntestatarioConto = Pulito(intestatario);
        Tfr = tfr;
        FondoPensione = tfr == DestinazioneTfr.FondoPensione ? fondoPensione!.Trim() : null;
        Salvato(3, da, adesso);
    }

    public void ImpostaFamiglia(bool richiedeDetrazioni, IReadOnlyCollection<DatiFamiliare> familiari, string da, DateTime adesso)
    {
        if (familiari.Count > MaxFamiliari) throw new DomainException($"Al massimo {MaxFamiliari} familiari.");
        if (familiari.Count(f => f.Relazione == RelazioneFamiliare.Coniuge) > 1) throw new DomainException("Si può indicare un solo coniuge.");
        if (familiari.GroupBy(f => f.CodiceFiscale.Valore).Any(g => g.Count() > 1)) throw new DomainException("Lo stesso familiare è indicato due volte.");
        var nuovi = familiari.Select(f => new FamiliareACarico(Id, f)).ToList(); // valida tutto prima di toccare l'elenco
        _familiari.Clear();
        _familiari.AddRange(nuovi);
        RichiedeDetrazioni = richiedeDetrazioni;
        Salvato(4, da, adesso);
    }

    public void ImpostaAltriDati(AltriDati a, string da, DateTime adesso)
    {
        TitoloStudio = a.TitoloStudio;
        TipoDocumento = Guard.NotEmpty(a.TipoDocumento, "Tipo di documento");
        NumeroDocumento = Guard.NotEmpty(a.NumeroDocumento, "Numero del documento").ToUpperInvariant();
        ScadenzaDocumento = a.ScadenzaDocumento;
        ScadenzaPermessoSoggiorno = a.ScadenzaPermessoSoggiorno;
        Salvato(5, da, adesso);
    }

    public IReadOnlyList<string> Mancanti()
    {
        var m = new List<string>();
        if (ComuneNascita is null) m.Add("dati anagrafici");
        if (ResidenzaVia is null) m.Add("residenza");
        if (Iban is null) m.Add("pagamento e TFR");
        if (RichiedeDetrazioni is null) m.Add("detrazioni e familiari");
        if (NumeroDocumento is null) m.Add("documento d'identità");
        return m;
    }

    public void Completa(bool dichiarazione, string da, DateTime adesso)
    {
        if (Mancanti() is { Count: > 0 } mancanti) throw new DomainException($"Mancano ancora: {string.Join(", ", mancanti)}.");
        if (!dichiarazione) throw new DomainException("Per completare serve la dichiarazione di veridicità dei dati.");
        CompletatoIl ??= adesso;
        AggiornatoIl = adesso;
        AggiornatoDa = da;
        Passo = UltimoPasso;
    }

    private void Salvato(int passo, string da, DateTime adesso)
    {
        Passo = Math.Max(Passo, Math.Min(passo + 1, UltimoPasso));
        AggiornatoIl = adesso;
        AggiornatoDa = da;
    }

    private static string? Pulito(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed class FamiliareACarico : Entity
{
    public Guid ProfiloId { get; private set; }
    public RelazioneFamiliare Relazione { get; private set; }
    public string Nome { get; private set; } = null!;
    public string Cognome { get; private set; } = null!;
    public CodiceFiscale CodiceFiscale { get; private set; } = null!;
    public DateOnly DataNascita { get; private set; }
    /// <summary>Percentuale di carico ai fini delle detrazioni (50 o 100).</summary>
    public int PercentualeCarico { get; private set; }
    public bool Disabile { get; private set; }

    private FamiliareACarico() { } // EF Core

    internal FamiliareACarico(Guid profiloId, DatiFamiliare d)
    {
        if (d.PercentualeCarico is not (50 or 100)) throw new DomainException("La percentuale di carico è 50% o 100%.");
        if (d.DataNascita > DateOnly.FromDateTime(DateTime.Today)) throw new DomainException($"Data di nascita di {d.Nome} nel futuro.");
        ProfiloId = profiloId;
        Relazione = d.Relazione;
        Nome = Guard.NotEmpty(d.Nome, "Nome del familiare");
        Cognome = Guard.NotEmpty(d.Cognome, "Cognome del familiare");
        CodiceFiscale = d.CodiceFiscale;
        DataNascita = d.DataNascita;
        PercentualeCarico = d.PercentualeCarico;
        Disabile = d.Disabile;
    }

    public string Descrizione => Relazione switch
    {
        RelazioneFamiliare.Coniuge => "Coniuge",
        RelazioneFamiliare.Figlio => "Figlio/a",
        _ => "Altro familiare",
    };
}

public static class TitoloStudioExtensions
{
    public static string Descrizione(this TitoloStudio t) => t switch
    {
        TitoloStudio.Nessuno => "Nessun titolo",
        TitoloStudio.LicenzaMedia => "Licenza media",
        TitoloStudio.QualificaProfessionale => "Qualifica professionale",
        TitoloStudio.Diploma => "Diploma di scuola superiore",
        TitoloStudio.LaureaTriennale => "Laurea triennale",
        TitoloStudio.LaureaMagistrale => "Laurea magistrale / a ciclo unico",
        _ => "Dottorato / master",
    };
}
