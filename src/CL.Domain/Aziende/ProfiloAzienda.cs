using CL.Domain.Common;
using CL.Domain.ValueObjects;

namespace CL.Domain.Aziende;

public sealed record DatiSocietari(string FormaGiuridica, string? NumeroRea, string CodiceAteco, DateOnly? DataInizioAttivita,
    string Pec, string? Telefono);

public sealed record Indirizzo(string Via, string Cap, string Comune, string Provincia)
{
    /// <summary>Controlla CAP (5 cifre) e sigla di provincia (2 lettere) e restituisce l'indirizzo normalizzato.</summary>
    public Indirizzo Validato(string quale)
    {
        var cap = Guard.NotEmpty(Cap, $"CAP {quale}");
        if (cap.Length != 5 || !cap.All(char.IsAsciiDigit)) throw new DomainException($"CAP {quale} non valido.");
        var prov = Guard.NotEmpty(Provincia, $"Provincia {quale}").ToUpperInvariant();
        if (!Province.IsValida(prov)) throw new DomainException($"Provincia {quale}: \"{prov}\" non esiste, usa la sigla (es. MI).");
        // Comune dall'elenco ISTAT, coerente con la provincia; si salva con il nome ufficiale.
        var nome = Guard.NotEmpty(Comune, $"Comune {quale}");
        var comune = Comuni.Trova(nome, prov)
                     ?? throw new DomainException(Comuni.Trova(nome) is { } altrove
                         ? $"Comune {quale}: {altrove.Nome} è in provincia di {altrove.Provincia}, non {prov}."
                         : $"Comune {quale}: \"{nome}\" non risulta tra i comuni italiani.");
        return new Indirizzo(Guard.NotEmpty(Via, $"Indirizzo {quale}"), cap, comune.Nome, prov);
    }
}

public sealed record LegaleRappresentante(string Nome, string Cognome, CodiceFiscale CodiceFiscale, string? Email, string? Telefono);

public sealed record PosizioniLavoro(string Ccnl, string MatricolaInps, string InailCodiceDitta, string InailPat,
    string? CassaEdile, string? EnteBilaterale, string? FondoPensione, int GiorniLavorativiSettimana, decimal OreSettimanali)
{
    /// <summary>Codice statistico contributivo INPS (5 cifre) e codici di autorizzazione (es. "1R, 2B").</summary>
    public string? Csc { get; init; }
    public string? CodiciAutorizzazione { get; init; }
    /// <summary>Contratti integrativi o aziendali applicati, oltre al CCNL.</summary>
    public string? ContrattiIntegrativi { get; init; }
    public IReadOnlyList<VoceInail> VociInail { get; init; } = [];
}

/// <summary>Voce di tariffa INAIL della PAT e tasso applicato (per mille).</summary>
public sealed class VoceInail
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Voce { get; private set; } = null!;
    public decimal Tasso { get; private set; }
    private VoceInail() { }
    public VoceInail(string voce, decimal tasso)
    {
        Voce = Guard.NotEmpty(voce, "Voce di tariffa INAIL").ToUpperInvariant();
        if (Voce.Length > 20) throw new DomainException("Voce di tariffa INAIL troppo lunga.");
        if (tasso is < 0 or > 1000) throw new DomainException("Tasso INAIL non valido (per mille, tra 0 e 1000).");
        Tasso = tasso;
    }
}

/// <summary>Titolare effettivo (antiriciclaggio): chi possiede o controlla l'azienda.</summary>
public sealed class TitolareEffettivo
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Nome { get; private set; } = null!;
    public string Cognome { get; private set; } = null!;
    public CodiceFiscale CodiceFiscale { get; private set; } = null!;
    /// <summary>Quota di partecipazione in percentuale, se nota.</summary>
    public decimal? Quota { get; private set; }
    private TitolareEffettivo() { }
    public TitolareEffettivo(string nome, string cognome, CodiceFiscale cf, decimal? quota)
    {
        Nome = Guard.NotEmpty(nome, "Nome del titolare effettivo");
        Cognome = Guard.NotEmpty(cognome, "Cognome del titolare effettivo");
        CodiceFiscale = cf;
        if (quota is < 0 or > 100) throw new DomainException("Quota del titolare effettivo tra 0 e 100%.");
        Quota = quota;
    }
}

/// <summary>Ambito di un referente dell'azienda.</summary>
public enum AmbitoReferente { Amministrazione, RisorseUmane, Altro }

/// <summary>Persona di riferimento dell'azienda per amministrazione o personale (contatti per lo studio).</summary>
public sealed class ReferenteAzienda
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public AmbitoReferente Ambito { get; private set; }
    public string Nome { get; private set; } = null!;
    public string? Ruolo { get; private set; }
    public string? Email { get; private set; }
    public string? Telefono { get; private set; }
    private ReferenteAzienda() { }
    public ReferenteAzienda(AmbitoReferente ambito, string nome, string? ruolo, string? email, string? telefono)
    {
        Ambito = ambito;
        Nome = Guard.NotEmpty(nome, "Nome del referente");
        Ruolo = Guard.Facoltativo(ruolo);
        Email = Guard.Facoltativo(email)?.ToLowerInvariant();
        Telefono = Guard.Facoltativo(telefono);
        if (Email is null && Telefono is null) throw new DomainException($"Indica email o telefono di {Nome}.");
    }
}

/// <summary>Sede o unità locale dell'azienda (dove lavora il personale), con il suo referente.</summary>
public sealed class UnitaLocale
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Nome { get; private set; } = null!;
    public string Via { get; private set; } = null!;
    public string Cap { get; private set; } = null!;
    public string Comune { get; private set; } = null!;
    public string Provincia { get; private set; } = null!;
    public string? ReferenteNome { get; private set; }
    public string? ReferenteEmail { get; private set; }
    public string? ReferenteTelefono { get; private set; }
    public Indirizzo Indirizzo => new(Via, Cap, Comune, Provincia);
    private UnitaLocale() { }
    public UnitaLocale(Guid? id, string? nome, Indirizzo indirizzo, string? referenteNome, string? referenteEmail, string? referenteTelefono)
    {
        if (id is { } i && i != Guid.Empty) Id = i;
        var v = indirizzo.Validato("dell'unità locale");
        (Via, Cap, Comune, Provincia) = (v.Via, v.Cap, v.Comune, v.Provincia);
        Nome = Guard.Facoltativo(nome) ?? $"Unità di {Comune}";
        ReferenteNome = Guard.Facoltativo(referenteNome);
        ReferenteEmail = Guard.Facoltativo(referenteEmail)?.ToLowerInvariant();
        ReferenteTelefono = Guard.Facoltativo(referenteTelefono);
    }
}

/// <summary>Cambio consulente: chi seguiva le paghe prima e da quando passa al nuovo consulente.</summary>
public sealed record DatiSubentro(string ConsulentePrecedente, string? ContattiPrecedente, DateOnly DataSubentro, DateOnly? UltimoMeseElaborato);

/// <summary>Dati per le paghe. Il referente paghe non è più qui: è la persona del personale segnata come tale nella sua scheda.</summary>
public sealed record DatiPaghe(Iban? IbanAddebito, int? GiornoPagamento, string? NoteStudio);

/// <summary>
/// Le informazioni che l'azienda fornisce al consulente per la gestione delle pratiche di lavoro,
/// raccolte con il percorso guidato al primo accesso. Ogni passo si salva da solo; il profilo è
/// completo quando tutti i dati obbligatori ci sono e l'azienda ha preso visione dell'informativa privacy.
/// </summary>
public sealed class ProfiloAzienda : Entity
{
    public const int UltimoPasso = 7;
    /// <summary>Passo "Cambio consulente": solo per le aziende che arrivano da un altro consulente.</summary>
    public const int PassoSubentro = 6;

    public Guid AziendaId { get; private set; }
    /// <summary>Primo passo non ancora salvato (1–6): da qui riprende il percorso.</summary>
    public int Passo { get; private set; } = 1;

    // 1. Dati societari
    public string? FormaGiuridica { get; private set; }
    public string? NumeroRea { get; private set; }
    public string? CodiceAteco { get; private set; }
    public DateOnly? DataInizioAttivita { get; private set; }
    public string? Pec { get; private set; }
    public string? Telefono { get; private set; }

    // 2. Sedi
    public string? SedeLegaleVia { get; private set; }
    public string? SedeLegaleCap { get; private set; }
    public string? SedeLegaleComune { get; private set; }
    public string? SedeLegaleProvincia { get; private set; }
    // Sede operativa singola (prima delle unità locali): resta per i profili vecchi, ora si usano le unità locali.
    public string? SedeOperativaVia { get; private set; }
    public string? SedeOperativaCap { get; private set; }
    public string? SedeOperativaComune { get; private set; }
    public string? SedeOperativaProvincia { get; private set; }

    // 3. Legale rappresentante
    public string? LegaleNome { get; private set; }
    public string? LegaleCognome { get; private set; }
    public CodiceFiscale? LegaleCodiceFiscale { get; private set; }
    public string? LegaleEmail { get; private set; }
    public string? LegaleTelefono { get; private set; }
    /// <summary>Il legale rappresentante è anche l'unico titolare effettivo.</summary>
    public bool LegaleUnicoTitolare { get; private set; }
    private readonly List<TitolareEffettivo> _titolariEffettivi = [];
    public IReadOnlyList<TitolareEffettivo> TitolariEffettivi => _titolariEffettivi;
    private readonly List<UnitaLocale> _unitaLocali = [];
    public IReadOnlyList<UnitaLocale> UnitaLocali => _unitaLocali;

    // 4. Contratto e posizioni previdenziali/assicurative
    public string? Ccnl { get; private set; }
    public string? MatricolaInps { get; private set; }
    /// <summary>L'azienda non ha ancora contratti di lavoro attivi: le posizioni INPS/INAIL si apriranno con la prima assunzione.</summary>
    public bool SenzaContratti { get; private set; }
    public string? InailCodiceDitta { get; private set; }
    public string? InailPat { get; private set; }
    public string? CassaEdile { get; private set; }
    public string? EnteBilaterale { get; private set; }
    public string? FondoPensione { get; private set; }
    public int? GiorniLavorativiSettimana { get; private set; }
    public decimal? OreSettimanali { get; private set; }
    public string? Csc { get; private set; }
    public string? CodiciAutorizzazione { get; private set; }
    public string? ContrattiIntegrativi { get; private set; }
    private readonly List<VoceInail> _vociInail = [];
    public IReadOnlyList<VoceInail> VociInail => _vociInail;

    // 6. Cambio consulente (solo subentro)
    /// <summary>L'azienda arriva da un altro consulente: nel percorso c'è anche il passo "Cambio consulente".</summary>
    public bool Subentro { get; private set; }
    public string? ConsulentePrecedente { get; private set; }
    public string? ContattiPrecedente { get; private set; }
    public DateOnly? DataSubentro { get; private set; }
    public DateOnly? UltimoMeseElaborato { get; private set; }

    // 5. Paghe
    public Iban? IbanAddebito { get; private set; }
    public int? GiornoPagamento { get; private set; }
    // Referente paghe: non più compilato (ora è la persona segnata come tale in Personale); le colonne restano nello schema.
    public string? ReferenteNome { get; private set; }
    public string? ReferenteEmail { get; private set; }
    public string? ReferenteTelefono { get; private set; }
    public string? NoteStudio { get; private set; }

    // 6. Conferma
    public DateTime? PrivacyAccettataIl { get; private set; }
    public DateTime? CompletatoIl { get; private set; }
    public string? CompletatoDa { get; private set; }

    public bool Completato => CompletatoIl is not null;

    private ProfiloAzienda() { } // EF Core

    public ProfiloAzienda(Guid aziendaId) => AziendaId = aziendaId;

    public Indirizzo? SedeLegale => SedeLegaleVia is null ? null
        : new(SedeLegaleVia, SedeLegaleCap!, SedeLegaleComune!, SedeLegaleProvincia!);
    public Indirizzo? SedeOperativa => SedeOperativaVia is null ? null
        : new(SedeOperativaVia, SedeOperativaCap!, SedeOperativaComune!, SedeOperativaProvincia!);

    public void ImpostaDatiSocietari(DatiSocietari d)
    {
        FormaGiuridica = Guard.NotEmpty(d.FormaGiuridica, "Forma giuridica");
        NumeroRea = Guard.Facoltativo(d.NumeroRea)?.ToUpperInvariant();
        CodiceAteco = Guard.NotEmpty(d.CodiceAteco, "Codice ATECO");
        DataInizioAttivita = d.DataInizioAttivita;
        Pec = Guard.NotEmpty(d.Pec, "PEC").ToLowerInvariant();
        Telefono = Guard.Facoltativo(d.Telefono);
        Avanza(1);
    }

    /// <summary>Sede legale e unità locali (dove lavora il personale), ciascuna con il suo referente.</summary>
    public void ImpostaSedi(Indirizzo legale, IEnumerable<UnitaLocale> unita)
    {
        (SedeLegaleVia, SedeLegaleCap, SedeLegaleComune, SedeLegaleProvincia) = legale.Validato("della sede legale");
        var nuove = unita.ToList();
        if (nuove.Count > 20) throw new DomainException("Al massimo 20 unità locali.");
        _unitaLocali.RemoveAll(u => nuove.All(n => n.Id != u.Id));
        foreach (var n in nuove)
        {
            var i = _unitaLocali.FindIndex(u => u.Id == n.Id);
            if (i >= 0) _unitaLocali[i] = n; else _unitaLocali.Add(n);
        }
        (SedeOperativaVia, SedeOperativaCap, SedeOperativaComune, SedeOperativaProvincia) = ((string?)null, (string?)null, (string?)null, (string?)null);
        Avanza(2);
    }

    public void ImpostaLegaleRappresentante(LegaleRappresentante l)
    {
        LegaleNome = Guard.NotEmpty(l.Nome, "Nome del legale rappresentante");
        LegaleCognome = Guard.NotEmpty(l.Cognome, "Cognome del legale rappresentante");
        LegaleCodiceFiscale = l.CodiceFiscale;
        LegaleEmail = Guard.Facoltativo(l.Email)?.ToLowerInvariant();
        LegaleTelefono = Guard.Facoltativo(l.Telefono);
        Avanza(3);
    }

    /// <summary>Titolari effettivi; se il legale rappresentante è l'unico, l'elenco resta vuoto.</summary>
    public void ImpostaTitolariEffettivi(bool legaleUnico, IEnumerable<TitolareEffettivo> titolari)
    {
        var elenco = legaleUnico ? [] : titolari.ToList();
        if (elenco.Count > 10) throw new DomainException("Al massimo 10 titolari effettivi.");
        if (elenco.Sum(t => t.Quota ?? 0) > 100) throw new DomainException("Le quote dei titolari effettivi superano il 100%.");
        LegaleUnicoTitolare = legaleUnico;
        _titolariEffettivi.Clear();
        _titolariEffettivi.AddRange(elenco);
    }

    private readonly List<ReferenteAzienda> _referenti = [];
    /// <summary>Referenti per amministrazione e personale.</summary>
    public IReadOnlyList<ReferenteAzienda> Referenti => _referenti;

    public void ImpostaReferenti(IEnumerable<ReferenteAzienda> referenti)
    {
        var elenco = referenti.ToList();
        if (elenco.Count > 10) throw new DomainException("Al massimo 10 referenti.");
        _referenti.Clear();
        _referenti.AddRange(elenco);
    }

    public void ImpostaPosizioni(PosizioniLavoro p)
    {
        if (p.GiorniLavorativiSettimana is not (5 or 6)) throw new DomainException("La settimana lavorativa è di 5 o 6 giorni.");
        if (p.OreSettimanali is <= 0 or > 48) throw new DomainException("Orario settimanale non valido (massimo 48 ore).");
        Ccnl = Guard.NotEmpty(p.Ccnl, "CCNL applicato");
        MatricolaInps = Guard.NotEmpty(p.MatricolaInps, "Matricola INPS").ToUpperInvariant();
        InailCodiceDitta = Guard.NotEmpty(p.InailCodiceDitta, "Codice ditta INAIL").ToUpperInvariant();
        InailPat = Guard.NotEmpty(p.InailPat, "PAT INAIL").ToUpperInvariant();
        CassaEdile = Guard.Facoltativo(p.CassaEdile);
        EnteBilaterale = Guard.Facoltativo(p.EnteBilaterale);
        FondoPensione = Guard.Facoltativo(p.FondoPensione);
        GiorniLavorativiSettimana = p.GiorniLavorativiSettimana;
        OreSettimanali = p.OreSettimanali;
        var csc = Guard.Facoltativo(p.Csc);
        if (csc is not null && (csc.Length != 5 || !csc.All(char.IsAsciiDigit))) throw new DomainException("Il CSC INPS è di 5 cifre.");
        Csc = csc;
        CodiciAutorizzazione = Guard.Facoltativo(p.CodiciAutorizzazione)?.ToUpperInvariant();
        ContrattiIntegrativi = Guard.Facoltativo(p.ContrattiIntegrativi);
        if (p.VociInail.Count > 10) throw new DomainException("Al massimo 10 voci di tariffa INAIL.");
        _vociInail.Clear();
        _vociInail.AddRange(p.VociInail);
        SenzaContratti = false;
        Avanza(4);
    }

    /// <summary>Nessun contratto attivo per ora: il passo è fatto, le posizioni restano da indicare (le aprirà il consulente).</summary>
    public void ImpostaSenzaContratti()
    {
        if (Subentro) throw new DomainException("Chi cambia consulente ha già le posizioni INPS e INAIL: indicale.");
        SenzaContratti = true;
        Avanza(4);
    }

    public void ImpostaPaghe(DatiPaghe p)
    {
        if (p.GiornoPagamento is < 1 or > 31) throw new DomainException("Giorno di pagamento non valido.");
        IbanAddebito = p.IbanAddebito;
        GiornoPagamento = p.GiornoPagamento;
        NoteStudio = Guard.Facoltativo(p.NoteStudio);
        Avanza(5);
    }

    /// <summary>
    /// Nuova attività o cambio consulente (dall'azienda). Senza subentro il passo "Cambio consulente" non c'è:
    /// chi era fermo lì passa al riepilogo.
    /// </summary>
    public void ImpostaSubentro(bool subentro)
    {
        Subentro = subentro;
        if (!subentro && Passo == PassoSubentro) Passo = UltimoPasso;
        if (subentro) SenzaContratti = false;
    }

    public void ImpostaDatiSubentro(DatiSubentro d)
    {
        if (!Subentro) throw new DomainException("L'azienda non arriva da un altro consulente.");
        ConsulentePrecedente = Guard.NotEmpty(d.ConsulentePrecedente, "Consulente precedente");
        ContattiPrecedente = Guard.Facoltativo(d.ContattiPrecedente);
        DataSubentro = d.DataSubentro;
        if (d.UltimoMeseElaborato is { } m && m > d.DataSubentro) throw new DomainException("L'ultimo mese elaborato dal consulente precedente è dopo la data del cambio.");
        UltimoMeseElaborato = d.UltimoMeseElaborato is { } u ? new DateOnly(u.Year, u.Month, 1) : null;
        Avanza(PassoSubentro);
    }

    /// <summary>Dati obbligatori ancora mancanti (vuoto = si può completare).</summary>
    public IReadOnlyList<string> Mancanti()
    {
        var m = new List<string>();
        if (FormaGiuridica is null) m.Add("dati societari");
        if (SedeLegaleVia is null) m.Add("sede legale");
        if (LegaleCodiceFiscale is null) m.Add("legale rappresentante");
        if (MatricolaInps is null && !SenzaContratti) m.Add("contratto e posizioni INPS/INAIL");
        if (Passo <= 5 && CompletatoIl is null) m.Add("paghe");
        if (Subentro && DataSubentro is null) m.Add("cambio consulente");
        return m;
    }

    public void Completa(bool privacyAccettata, string da, DateTime adesso)
    {
        if (Mancanti() is { Count: > 0 } mancanti) throw new DomainException($"Mancano ancora: {string.Join(", ", mancanti)}.");
        if (!privacyAccettata) throw new DomainException("Per completare serve la presa visione dell'informativa privacy.");
        PrivacyAccettataIl ??= adesso;
        CompletatoIl ??= adesso;
        CompletatoDa ??= da;
        Passo = UltimoPasso;
    }

    /// <summary>Il passo da cui riprendere avanza solo: salvare di nuovo un passo già fatto non fa tornare indietro.</summary>
    private void Avanza(int salvato)
    {
        var prossimo = salvato + 1;
        if (prossimo == PassoSubentro && !Subentro) prossimo = UltimoPasso; // senza cambio consulente si salta quel passo
        Passo = Math.Max(Passo, Math.Min(prossimo, UltimoPasso));
    }
}
