using System.Numerics;
using CL.Domain.Common;

namespace CL.Domain.ValueObjects;

/// <summary>IBAN con verifica mod-97 (ISO 13616). Per l'Italia verifica anche la lunghezza (27).</summary>
public sealed record Iban
{
    public string Valore { get; }

    private Iban(string valore) => Valore = valore;

    public static Iban Parse(string? input)
    {
        var iban = Guard.NotEmpty(input, "IBAN").Replace(" ", "").ToUpperInvariant();
        if (iban.Length is < 15 or > 34 || !iban.All(char.IsAsciiLetterOrDigit)
            || !char.IsAsciiLetter(iban[0]) || !char.IsAsciiLetter(iban[1]))
            throw new DomainException("Formato IBAN non valido.");
        if (iban.StartsWith("IT") && iban.Length != 27)
            throw new DomainException("Un IBAN italiano deve avere 27 caratteri.");

        var riordinato = iban[4..] + iban[..4];
        var numerico = string.Concat(riordinato.Select(c => char.IsDigit(c) ? c.ToString() : (c - 'A' + 10).ToString()));
        if (BigInteger.Parse(numerico) % 97 != 1)
            throw new DomainException("Cifre di controllo IBAN non valide.");
        return new Iban(iban);
    }

    public override string ToString() => Valore;
}
