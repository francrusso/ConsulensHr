using System.Text.RegularExpressions;
using CL.Domain.Common;

namespace CL.Domain.ValueObjects;

/// <summary>
/// Codice fiscale di persona fisica (16 caratteri) con verifica del carattere di controllo.
/// Gestisce anche i codici con omocodia (cifre sostituite da lettere).
/// </summary>
public sealed partial record CodiceFiscale
{
    public string Valore { get; }

    private CodiceFiscale(string valore) => Valore = valore;

    public static CodiceFiscale Parse(string? input)
    {
        var cf = Guard.NotEmpty(input, "Codice fiscale").ToUpperInvariant().Replace(" ", "");
        if (!Formato().IsMatch(cf))
            throw new DomainException("Formato del codice fiscale non valido.");
        if (CalcolaControllo(cf[..15]) != cf[15])
            throw new DomainException("Carattere di controllo del codice fiscale non valido.");
        return new CodiceFiscale(cf);
    }

    // Valore di ciascuna cifra/lettera (0-9 come A-J) quando sta in posizione dispari (tabella ministeriale);
    // in posizione pari vale la sua posizione nell'alfabeto (A = 0) o la cifra stessa.
    private static readonly int[] Dispari =
        [1, 0, 5, 7, 9, 13, 15, 17, 19, 21, 2, 4, 18, 20, 11, 3, 6, 8, 12, 14, 16, 10, 22, 25, 24, 23];

    private static char CalcolaControllo(string primi15)
    {
        var somma = 0;
        for (var i = 0; i < 15; i++)
        {
            var c = primi15[i];
            var idx = char.IsDigit(c) ? c - '0' : c - 'A';
            // Posizioni dispari (1-based) = indici pari (0-based)
            somma += i % 2 == 0 ? Dispari[idx] : idx;
        }
        return (char)('A' + somma % 26);
    }

    // Omocodia: le posizioni numeriche possono contenere L M N P Q R S T U V
    [GeneratedRegex("^[A-Z]{6}[0-9LMNPQRSTUV]{2}[ABCDEHLMPRST][0-9LMNPQRSTUV]{2}[A-Z][0-9LMNPQRSTUV]{3}[A-Z]$")]
    private static partial Regex Formato();

    public override string ToString() => Valore;
}
