using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using CL.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace CL.Infrastructure.Indirizzi;

/// <summary>Indirizzo suggerito, già scomposto per i campi del modulo (provincia come sigla, se riconosciuta).</summary>
public sealed record IndirizzoTrovato(string Etichetta, string Via, string? Cap, string? Comune, string? Provincia);

/// <summary>Suggerimenti di indirizzi italiani mentre si scrive.</summary>
public interface IRicercaIndirizzi
{
    Task<IReadOnlyList<IndirizzoTrovato>> CercaAsync(string testo, CancellationToken ct = default);
}

/// <summary>
/// Photon (komoot), ricerca "mentre scrivi" sui dati di OpenStreetMap: gratuita e senza chiavi. Le richieste partono dal
/// server, non dal browser, così all'esterno non arrivano né l'IP né i cookie di chi compila. Se in futuro servisse Google
/// Places basta un'altra implementazione di <see cref="IRicercaIndirizzi"/>.
/// </summary>
internal sealed class RicercaPhoton(HttpClient http, ILogger<RicercaPhoton> logger) : IRicercaIndirizzi
{
    public const string IndirizzoServizio = "https://photon.komoot.io/";

    private sealed record Risposta([property: JsonPropertyName("features")] List<Elemento>? Features);
    private sealed record Elemento([property: JsonPropertyName("properties")] Proprieta? Properties);
    private sealed record Proprieta(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("street")] string? Street,
        [property: JsonPropertyName("housenumber")] string? HouseNumber,
        [property: JsonPropertyName("postcode")] string? Postcode,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("locality")] string? Locality,
        [property: JsonPropertyName("county")] string? County,
        [property: JsonPropertyName("countrycode")] string? CountryCode,
        [property: JsonPropertyName("type")] string? Type);

    public async Task<IReadOnlyList<IndirizzoTrovato>> CercaAsync(string testo, CancellationToken ct = default)
    {
        // Riquadro dell'Italia e centro di gravità sul Paese: i risultati esteri restano fuori.
        var url = $"api/?q={Uri.EscapeDataString(testo)}&limit=8&lat=42.5&lon=12.5&bbox=6.6,35.4,18.6,47.1";
        Risposta? r;
        try { r = await http.GetFromJsonAsync<Risposta>(url, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Ricerca indirizzi non disponibile");
            return [];
        }
        return (r?.Features ?? [])
            .Select(f => f.Properties)
            .Where(p => p is not null && string.Equals(p.CountryCode, "IT", StringComparison.OrdinalIgnoreCase))
            .Select(p =>
            {
                var strada = p!.Street ?? (p.Type is "street" ? p.Name : null);
                if (strada is null) return null;
                var via = p.HouseNumber is { Length: > 0 } n ? $"{strada} {n}" : strada;
                // Comune e provincia con i nomi ufficiali ISTAT, gli stessi delle select del modulo.
                var ufficiale = Comuni.Trova(p.City ?? p.Locality, SiglaProvincia(p.County));
                var comune = ufficiale?.Nome ?? p.City ?? p.Locality;
                var prov = ufficiale?.Provincia ?? SiglaProvincia(p.County);
                var cap = p.Postcode is { Length: 5 } c && c.All(char.IsAsciiDigit) ? c : null;
                var etichetta = via + (comune is null ? "" : $", {(cap is null ? "" : cap + " ")}{comune}") + (prov is null ? "" : $" ({prov})");
                return new IndirizzoTrovato(etichetta, via, cap, comune, prov);
            })
            .OfType<IndirizzoTrovato>()
            .DistinctBy(i => i.Etichetta)
            .Take(6)
            .ToList();
    }

    /// <summary>Sigla dalla provincia come la chiama OpenStreetMap ("Milano", "Roma Capitale", "Reggio nell'Emilia"…).</summary>
    internal static string? SiglaProvincia(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return null;
        var n = Normalizza(nome);
        foreach (var prefisso in new[] { "cittametropolitanadi", "provinciaautonomadi", "liberoconsorziocomunaledi", "provinciadi", "provinciadel" })
            if (n.StartsWith(prefisso, StringComparison.Ordinal)) n = n[prefisso.Length..];
        return Nomi.Where(x => n.Contains(x.Chiave, StringComparison.Ordinal)).Select(x => x.Sigla).FirstOrDefault();
    }

    private static string Normalizza(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark && char.IsLetter(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    // Chiavi normalizzate (solo lettere), dalla più lunga: "ravenna" va provata prima di "enna".
    private static readonly (string Chiave, string Sigla)[] Nomi = new (string Nome, string Sigla)[]
    {
        ("Agrigento", "AG"), ("Alessandria", "AL"), ("Ancona", "AN"), ("Aosta", "AO"), ("Ascoli Piceno", "AP"), ("L'Aquila", "AQ"),
        ("Arezzo", "AR"), ("Asti", "AT"), ("Avellino", "AV"), ("Bari", "BA"), ("Bergamo", "BG"), ("Biella", "BI"), ("Belluno", "BL"),
        ("Benevento", "BN"), ("Bologna", "BO"), ("Brindisi", "BR"), ("Brescia", "BS"), ("Barletta", "BT"), ("Bolzano", "BZ"),
        ("Alto Adige", "BZ"), ("Cagliari", "CA"), ("Campobasso", "CB"), ("Caserta", "CE"), ("Chieti", "CH"), ("Caltanissetta", "CL"),
        ("Cuneo", "CN"), ("Como", "CO"), ("Cremona", "CR"), ("Cosenza", "CS"), ("Catania", "CT"), ("Catanzaro", "CZ"), ("Enna", "EN"),
        ("Forlì", "FC"), ("Ferrara", "FE"), ("Foggia", "FG"), ("Firenze", "FI"), ("Fermo", "FM"), ("Frosinone", "FR"), ("Genova", "GE"),
        ("Gorizia", "GO"), ("Grosseto", "GR"), ("Imperia", "IM"), ("Isernia", "IS"), ("Crotone", "KR"), ("Lecco", "LC"), ("Lecce", "LE"),
        ("Livorno", "LI"), ("Lodi", "LO"), ("Latina", "LT"), ("Lucca", "LU"), ("Monza", "MB"), ("Macerata", "MC"), ("Messina", "ME"),
        ("Milano", "MI"), ("Mantova", "MN"), ("Modena", "MO"), ("Massa", "MS"), ("Matera", "MT"), ("Napoli", "NA"), ("Novara", "NO"),
        ("Nuoro", "NU"), ("Oristano", "OR"), ("Palermo", "PA"), ("Piacenza", "PC"), ("Padova", "PD"), ("Pescara", "PE"), ("Perugia", "PG"),
        ("Pisa", "PI"), ("Pordenone", "PN"), ("Prato", "PO"), ("Parma", "PR"), ("Pistoia", "PT"), ("Pesaro", "PU"), ("Pavia", "PV"),
        ("Potenza", "PZ"), ("Ravenna", "RA"), ("Reggio di Calabria", "RC"), ("Reggio Calabria", "RC"), ("Reggio nell'Emilia", "RE"),
        ("Reggio Emilia", "RE"), ("Ragusa", "RG"), ("Rieti", "RI"), ("Roma", "RM"), ("Rimini", "RN"), ("Rovigo", "RO"), ("Salerno", "SA"),
        ("Siena", "SI"), ("Sondrio", "SO"), ("La Spezia", "SP"), ("Siracusa", "SR"), ("Sassari", "SS"), ("Sud Sardegna", "SU"),
        ("Savona", "SV"), ("Taranto", "TA"), ("Teramo", "TE"), ("Trento", "TN"), ("Torino", "TO"), ("Trapani", "TP"), ("Terni", "TR"),
        ("Trieste", "TS"), ("Treviso", "TV"), ("Udine", "UD"), ("Varese", "VA"), ("Verbano", "VB"), ("Vercelli", "VC"), ("Venezia", "VE"),
        ("Vicenza", "VI"), ("Verona", "VR"), ("Viterbo", "VT"), ("Vibo Valentia", "VV"),
    }.Select(x => (Normalizza(x.Nome), x.Sigla)).OrderByDescending(x => x.Item1.Length).ToArray();
}
