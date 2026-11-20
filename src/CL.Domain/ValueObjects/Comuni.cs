using System.Globalization;
using System.Text;

namespace CL.Domain.ValueObjects;

public sealed record Comune(string Nome, string Provincia);

/// <summary>
/// Comuni e province italiane dall'elenco ufficiale ISTAT (fonte: ISTAT, "Elenco dei comuni italiani", CC BY 4.0).
/// Serve a proporre i valori nelle select con ricerca e a rifiutare comuni scritti male o non coerenti con la provincia.
/// Per aggiornarlo basta sostituire comuni.csv con l'estrazione più recente (righe "P;sigla;nome" e "C;comune;sigla").
/// </summary>
public static class Comuni
{
    private static readonly Lazy<(IReadOnlyList<Comune> Comuni, IReadOnlyDictionary<string, string> Province)> Dati = new(Carica);

    public static IReadOnlyList<Comune> Tutti => Dati.Value.Comuni;

    /// <summary>Sigla → nome della provincia (es. "FR" → "Frosinone"), in ordine di sigla.</summary>
    public static IReadOnlyDictionary<string, string> Province => Dati.Value.Province;

    /// <summary>Il comune con il nome ufficiale, se esiste (in quella provincia, se indicata). Maiuscole, accenti e apostrofi non contano.</summary>
    public static Comune? Trova(string? nome, string? provincia = null)
    {
        if (string.IsNullOrWhiteSpace(nome)) return null;
        var n = Chiave(nome);
        return Tutti.FirstOrDefault(c => Chiave(c.Nome) == n
                                         && (string.IsNullOrWhiteSpace(provincia) || c.Provincia.Equals(provincia.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Ricerca per le select: prima i comuni che iniziano con il testo, poi quelli che lo contengono.</summary>
    public static IReadOnlyList<Comune> Cerca(string? testo, string? provincia = null, int massimo = 20)
    {
        var elenco = string.IsNullOrWhiteSpace(provincia) ? Tutti
            : Tutti.Where(c => c.Provincia.Equals(provincia.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (string.IsNullOrWhiteSpace(testo)) return elenco.Take(massimo).ToList();
        var t = Chiave(testo);
        return elenco.Where(c => Chiave(c.Nome).StartsWith(t, StringComparison.Ordinal))
            .Concat(elenco.Where(c => !Chiave(c.Nome).StartsWith(t, StringComparison.Ordinal) && Chiave(c.Nome).Contains(t, StringComparison.Ordinal)))
            .Take(massimo).ToList();
    }

    /// <summary>Solo lettere e cifre, minuscole e senza accenti: "Sant'Agata de' Goti" e "SANT AGATA DE GOTI" coincidono.</summary>
    private static string Chiave(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
            if (char.IsLetterOrDigit(ch) && CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    private static (IReadOnlyList<Comune>, IReadOnlyDictionary<string, string>) Carica()
    {
        using var s = typeof(Comuni).Assembly.GetManifestResourceStream("CL.Domain.comuni.csv")
                      ?? throw new InvalidOperationException("Elenco dei comuni mancante.");
        using var r = new StreamReader(s, Encoding.UTF8);
        var comuni = new List<Comune>(8000);
        var province = new SortedDictionary<string, string>(StringComparer.Ordinal);
        while (r.ReadLine() is { } riga)
        {
            var p = riga.Split(';');
            if (p.Length != 3) continue;
            if (p[0] == "P") province[p[1]] = p[2];
            else if (p[0] == "C") comuni.Add(new Comune(p[1], p[2]));
        }
        return (comuni, province);
    }
}
