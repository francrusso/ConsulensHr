using CL.Domain.Common;
using CL.Domain.ValueObjects;

namespace CL.Domain.Aziende;

public sealed record DatiSocietari(string FormaGiuridica, string? NumeroRea, string CodiceAteco, DateOnly? DataInizioAttivita,
    string Pec, string? Telefono);

public sealed record Indirizzo(string Via, string Cap, string Comune, string Provincia);

public sealed record LegaleRappresentante(string Nome, string Cognome, CodiceFiscale CodiceFiscale, string? Email, string? Telefono);

public sealed record PosizioniLavoro(string Ccnl, string MatricolaInps, string InailCodiceDitta, string InailPat,
    string? CassaEdile, string? EnteBilaterale, string? FondoPensione, int GiorniLavorativiSettimana, decimal OreSettimanali);

public sealed record DatiPaghe(Iban? IbanAddebito, int? GiornoPagamento, string ReferenteNome, string ReferenteEmail,
    string? ReferenteTelefono, string? NoteStudio);

/// <summary>
/// Le informazioni che l'azienda fornisce allo studio per la gestione delle pratiche di lavoro,
/// raccolte con il percorso guidato al primo accesso. Ogni passo si salva da solo; il profilo è
/// completo quando tutti i dati obbligatori ci sono e l'azienda ha preso visione dell'informativa privacy.
/// </summary>
public sealed class ProfiloAzienda : Entity
{
    public const int UltimoPasso = 6;

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

    // 4. Contratto e posizioni previdenziali/assicurative
    public string? Ccnl { get; private set; }
    public string? MatricolaInps { get; private set; }
    public string? InailCodiceDitta { get; private set; }
    public string? InailPat { get; private set; }
    public string? CassaEdile { get; private set; }
    public string? EnteBilaterale { get; private set; }
    public string? FondoPensione { get; private set; }
    public int? GiorniLavorativiSettimana { get; private set; }
    public decimal? OreSettimanali { get; private set; }

    // 5. Paghe e referente
    public Iban? IbanAddebito { get; private set; }
    public int? GiornoPagamento { get; private set; }
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

    public DatiSocietari? Societari => FormaGiuridica is null ? null
        : new(FormaGiuridica, NumeroRea, CodiceAteco!, DataInizioAttivita, Pec!, Telefono);
    public Indirizzo? SedeLegale => SedeLegaleVia is null ? null
        : new(SedeLegaleVia, SedeLegaleCap!, SedeLegaleComune!, SedeLegaleProvincia!);
    public Indirizzo? SedeOperativa => SedeOperativaVia is null ? null
        : new(SedeOperativaVia, SedeOperativaCap!, SedeOperativaComune!, SedeOperativaProvincia!);

    public void ImpostaDatiSocietari(DatiSocietari d)
    {
        FormaGiuridica = Guard.NotEmpty(d.FormaGiuridica, "Forma giuridica");
        NumeroRea = Pulito(d.NumeroRea)?.ToUpperInvariant();
        CodiceAteco = Guard.NotEmpty(d.CodiceAteco, "Codice ATECO");
        DataInizioAttivita = d.DataInizioAttivita;
        Pec = Guard.NotEmpty(d.Pec, "PEC").ToLowerInvariant();
        Telefono = Pulito(d.Telefono);
        Avanza(1);
    }

    public void ImpostaSedi(Indirizzo legale, Indirizzo? operativa)
    {
        (SedeLegaleVia, SedeLegaleCap, SedeLegaleComune, SedeLegaleProvincia) = Valida(legale, "sede legale");
        (SedeOperativaVia, SedeOperativaCap, SedeOperativaComune, SedeOperativaProvincia) =
            operativa is null ? (null, null, null, null) : Valida(operativa, "sede operativa");
        Avanza(2);
    }

    public void ImpostaLegaleRappresentante(LegaleRappresentante l)
    {
        LegaleNome = Guard.NotEmpty(l.Nome, "Nome del legale rappresentante");
        LegaleCognome = Guard.NotEmpty(l.Cognome, "Cognome del legale rappresentante");
        LegaleCodiceFiscale = l.CodiceFiscale;
        LegaleEmail = Pulito(l.Email)?.ToLowerInvariant();
        LegaleTelefono = Pulito(l.Telefono);
        Avanza(3);
    }

    public void ImpostaPosizioni(PosizioniLavoro p)
    {
        if (p.GiorniLavorativiSettimana is not (5 or 6)) throw new DomainException("La settimana lavorativa è di 5 o 6 giorni.");
        if (p.OreSettimanali is <= 0 or > 48) throw new DomainException("Orario settimanale non valido (massimo 48 ore).");
        Ccnl = Guard.NotEmpty(p.Ccnl, "CCNL applicato");
        MatricolaInps = Guard.NotEmpty(p.MatricolaInps, "Matricola INPS").ToUpperInvariant();
        InailCodiceDitta = Guard.NotEmpty(p.InailCodiceDitta, "Codice ditta INAIL").ToUpperInvariant();
        InailPat = Guard.NotEmpty(p.InailPat, "PAT INAIL").ToUpperInvariant();
        CassaEdile = Pulito(p.CassaEdile);
        EnteBilaterale = Pulito(p.EnteBilaterale);
        FondoPensione = Pulito(p.FondoPensione);
        GiorniLavorativiSettimana = p.GiorniLavorativiSettimana;
        OreSettimanali = p.OreSettimanali;
        Avanza(4);
    }

    public void ImpostaPaghe(DatiPaghe p)
    {
        if (p.GiornoPagamento is < 1 or > 31) throw new DomainException("Giorno di pagamento non valido.");
        IbanAddebito = p.IbanAddebito;
        GiornoPagamento = p.GiornoPagamento;
        ReferenteNome = Guard.NotEmpty(p.ReferenteNome, "Referente per le paghe");
        ReferenteEmail = Guard.NotEmpty(p.ReferenteEmail, "Email del referente").ToLowerInvariant();
        ReferenteTelefono = Pulito(p.ReferenteTelefono);
        NoteStudio = Pulito(p.NoteStudio);
        Avanza(5);
    }

    /// <summary>Dati obbligatori ancora mancanti (vuoto = si può completare).</summary>
    public IReadOnlyList<string> Mancanti()
    {
        var m = new List<string>();
        if (FormaGiuridica is null) m.Add("dati societari");
        if (SedeLegaleVia is null) m.Add("sede legale");
        if (LegaleCodiceFiscale is null) m.Add("legale rappresentante");
        if (MatricolaInps is null) m.Add("contratto e posizioni INPS/INAIL");
        if (ReferenteEmail is null) m.Add("paghe e referente");
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

    private void Avanza(int salvato) => Passo = Math.Max(Passo, Math.Min(salvato + 1, UltimoPasso));

    private static (string, string, string, string) Valida(Indirizzo i, string quale)
    {
        var cap = Guard.NotEmpty(i.Cap, $"CAP della {quale}");
        if (cap.Length != 5 || !cap.All(char.IsAsciiDigit)) throw new DomainException($"CAP della {quale} non valido.");
        var prov = Guard.NotEmpty(i.Provincia, $"Provincia della {quale}").ToUpperInvariant();
        if (prov.Length != 2 || !prov.All(char.IsAsciiLetter)) throw new DomainException($"Provincia della {quale}: usa la sigla (es. MI).");
        return (Guard.NotEmpty(i.Via, $"Indirizzo della {quale}"), cap, Guard.NotEmpty(i.Comune, $"Comune della {quale}"), prov);
    }

    private static string? Pulito(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
