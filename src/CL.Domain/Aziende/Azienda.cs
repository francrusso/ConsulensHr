using CL.Domain.Common;
using CL.Domain.ValueObjects;

namespace CL.Domain.Aziende;

/// <summary>Azienda cliente del consulente di consulenza del lavoro.</summary>
public sealed class Azienda : Entity
{
    public string RagioneSociale { get; private set; } = null!;
    public PartitaIva PartitaIva { get; private set; } = null!;
    public string? CodiceFiscale { get; private set; }
    /// <summary>Via e numero civico della sede.</summary>
    public string? Indirizzo { get; private set; }
    public string? Cap { get; private set; }
    public string? Comune { get; private set; }
    public string? Provincia { get; private set; }
    public string? Email { get; private set; }
    /// <summary>Logo nell'archivio privato (chiave generata dall'applicazione), se caricato.</summary>
    public string? LogoChiave { get; private set; }
    /// <summary>Data del santo patrono del comune della sede (festività locale), se applicabile.</summary>
    public int? SantoPatronoMese { get; private set; }
    public int? SantoPatronoGiorno { get; private set; }
    public bool Attiva { get; private set; } = true;
    /// <summary>Tipi di richiesta non offerti dall'azienda (chiavi di <see cref="TipiRichiesta"/>, separate da virgola).</summary>
    public string? RichiesteDisattivate { get; private set; }

    /// <summary>Il tipo di richiesta è offerto ai dipendenti e all'azienda.</summary>
    public bool Abilitata(string chiave) =>
        string.IsNullOrEmpty(RichiesteDisattivate) || !RichiesteDisattivate.Split(',').Contains(chiave);

    /// <summary>Sceglie i tipi di richiesta offerti; ferie e malattia restano sempre.</summary>
    public void ImpostaRichiesteAbilitate(IEnumerable<string> abilitate)
    {
        var si = abilitate.ToHashSet();
        var no = TipiRichiesta.Tutti.Where(t => !t.Obbligatorio && !si.Contains(t.Chiave)).Select(t => t.Chiave).ToList();
        RichiesteDisattivate = no.Count == 0 ? null : string.Join(',', no);
    }

    private Azienda() { } // EF Core

    public Azienda(string ragioneSociale, PartitaIva partitaIva)
    {
        RagioneSociale = Guard.NotEmpty(ragioneSociale, "Ragione sociale");
        PartitaIva = partitaIva;
    }

    /// <summary>Dati anagrafici con controlli formali: CAP di 5 cifre, sigla di provincia esistente, codice fiscale valido, email.</summary>
    public void AggiornaAnagrafica(string ragioneSociale, string? codiceFiscale, string? indirizzo, string? cap, string? comune,
        string? provincia, string? email)
    {
        var rs = Guard.NotEmpty(ragioneSociale, "Ragione sociale");
        if (rs.Length > 200) throw new DomainException("Ragione sociale troppo lunga (massimo 200 caratteri).");
        var cf = Pulito(codiceFiscale)?.Replace(" ", "").ToUpperInvariant();
        if (cf is not null) cf = ValidaCodiceFiscale(cf);
        cap = Pulito(cap);
        if (cap is not null && (cap.Length != 5 || !cap.All(char.IsAsciiDigit))) throw new DomainException("Il CAP è di 5 cifre.");
        provincia = Pulito(provincia)?.ToUpperInvariant();
        if (provincia is not null && !Province.IsValida(provincia)) throw new DomainException($"La provincia \"{provincia}\" non esiste: usa la sigla (es. MI).");
        email = Pulito(email)?.ToLowerInvariant();
        if (email is not null && !EmailValida(email)) throw new DomainException("Indirizzo email non valido.");

        RagioneSociale = rs;
        CodiceFiscale = cf;
        Indirizzo = Pulito(indirizzo);
        Cap = cap;
        Comune = Pulito(comune);
        Provincia = provincia;
        Email = email;
    }

    public string? SedeCompleta => Indirizzo is null && Comune is null ? null
        : string.Join(", ", new[] { Indirizzo, string.Join(" ", new[] { Cap, Comune, Provincia is null ? null : $"({Provincia})" }.Where(x => x is not null)) }
            .Where(x => !string.IsNullOrWhiteSpace(x)));

    public void ImpostaLogo(string chiave) => LogoChiave = Guard.NotEmpty(chiave, "Logo");
    public void RimuoviLogo() => LogoChiave = null;

    /// <summary>Codice fiscale di una società (11 cifre, stesso controllo della partita IVA) o di una persona (16 caratteri, ditte individuali).</summary>
    private static string ValidaCodiceFiscale(string cf)
    {
        if (cf.Length == 11 && cf.All(char.IsAsciiDigit))
        {
            try { return PartitaIva.Parse(cf).Valore; }
            catch (DomainException) { throw new DomainException("Codice fiscale dell'azienda non valido (cifra di controllo errata)."); }
        }
        return ValueObjects.CodiceFiscale.Parse(cf).Valore;
    }

    private static bool EmailValida(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 && at == email.LastIndexOf('@') && email.IndexOf('.', at) > at + 1 && !email.EndsWith('.') && !email.Contains(' ') && email.Length <= 256;
    }

    private static string? Pulito(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public void ImpostaSantoPatrono(int mese, int giorno)
    {
        try { _ = new DateOnly(2024, mese, giorno); } // 2024 bisestile: ammette il 29/02
        catch (ArgumentOutOfRangeException) { throw new DomainException("Data del santo patrono non valida."); }
        SantoPatronoMese = mese;
        SantoPatronoGiorno = giorno;
    }

    public void RimuoviSantoPatrono()
    {
        SantoPatronoMese = null;
        SantoPatronoGiorno = null;
    }

    public void Disattiva() => Attiva = false;
    public void Riattiva() => Attiva = true;
}
