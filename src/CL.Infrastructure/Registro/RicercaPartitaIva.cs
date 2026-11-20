using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CL.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace CL.Infrastructure.Registro;

public enum EsitoRicerca { Trovata, NonTrovata, ServizioNonDisponibile }

/// <summary>Dati dell'impresa restituiti dal registro, già scomposti per i campi del modulo.</summary>
public sealed record DatiImpresa(string RagioneSociale, string? Indirizzo, string? Cap, string? Comune, string? Provincia);

public sealed record RisultatoRicerca(EsitoRicerca Esito, DatiImpresa? Dati = null);

/// <summary>Ricerca dei dati di un'impresa a partire dalla partita IVA.</summary>
public interface IRicercaPartitaIva
{
    Task<RisultatoRicerca> CercaAsync(PartitaIva partitaIva, CancellationToken ct = default);
}

/// <summary>
/// VIES, il servizio della Commissione europea per le partite IVA comunitarie: gratuito, senza chiavi,
/// per l'Italia restituisce denominazione e sede legale (dati dell'Agenzia delle Entrate).
/// </summary>
internal sealed partial class RicercaVies(HttpClient http, ILogger<RicercaVies> logger) : IRicercaPartitaIva
{
    public const string IndirizzoServizio = "https://ec.europa.eu/taxation_customs/vies/rest-api/";

    private sealed record RispostaVies(
        [property: JsonPropertyName("isValid")] bool IsValid,
        [property: JsonPropertyName("userError")] string? UserError,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("address")] string? Address);

    public async Task<RisultatoRicerca> CercaAsync(PartitaIva partitaIva, CancellationToken ct = default)
    {
        RispostaVies? r;
        try
        {
            r = await http.GetFromJsonAsync<RispostaVies>($"ms/IT/vat/{partitaIva.Valore}", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Ricerca VIES non riuscita per la partita IVA {PartitaIva}", partitaIva.Valore);
            return new RisultatoRicerca(EsitoRicerca.ServizioNonDisponibile);
        }

        if (r is null || (r.UserError is { } e && e != "VALID" && e != "INVALID"))
            return new RisultatoRicerca(EsitoRicerca.ServizioNonDisponibile); // es. MS_UNAVAILABLE, TIMEOUT
        if (!r.IsValid || string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim() == "---")
            return new RisultatoRicerca(EsitoRicerca.NonTrovata);

        var (via, cap, comune, provincia) = ScomponiIndirizzo(r.Address);
        return new RisultatoRicerca(EsitoRicerca.Trovata, new DatiImpresa(Maiuscole(r.Name), via, cap, comune, provincia));
    }

    [GeneratedRegex(@"^(?<cap>\d{5})\s+(?<comune>.+?)\s+(?<prov>[A-Z]{2})$")]
    private static partial Regex RigaComune();

    /// <summary>"PIAZZALE ENRICO MATTEI 1 \n00144 ROMA RM\n" → ("Piazzale Enrico Mattei 1", "00144", "Roma", "RM").</summary>
    internal static (string? Via, string? Cap, string? Comune, string? Provincia) ScomponiIndirizzo(string? indirizzo)
    {
        var righe = (indirizzo ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(r => r != "---").ToList();
        if (righe.Count == 0) return (null, null, null, null);

        var m = RigaComune().Match(righe[^1].ToUpperInvariant());
        if (!m.Success) return (Maiuscole(string.Join(", ", righe)), null, null, null);
        var via = righe.Count > 1 ? Maiuscole(string.Join(", ", righe[..^1])) : null;
        var prov = m.Groups["prov"].Value;
        return (via, m.Groups["cap"].Value, Maiuscole(m.Groups["comune"].Value), Province.IsValida(prov) ? prov : null);
    }

    private static readonly CultureInfo Italiano = CultureInfo.GetCultureInfo("it-IT");

    /// <summary>Il registro scrive tutto in maiuscolo: "ROSSI S.R.L." → "Rossi S.r.l.".</summary>
    internal static string Maiuscole(string testo)
    {
        var t = Italiano.TextInfo.ToTitleCase(testo.Trim().ToLower(Italiano));
        foreach (var (da, a) in new[] { ("S.R.L.", "S.r.l."), ("S.P.A.", "S.p.A."), ("S.N.C.", "S.n.c."), ("S.A.S.", "S.a.s."), ("S.R.L.S.", "S.r.l.s."),
                     ("Srl", "S.r.l."), ("Spa", "S.p.A."), ("Snc", "S.n.c."), ("Sas", "S.a.s."), ("Srls", "S.r.l.s.") })
            t = Regex.Replace(t, $@"(?<![\p{{L}}.]){Regex.Escape(da)}(?![\p{{L}}])", a, RegexOptions.IgnoreCase);
        return t;
    }
}
