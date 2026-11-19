using CL.Domain.Common;

namespace CL.Domain.ValueObjects;

/// <summary>Partita IVA italiana (11 cifre, algoritmo di Luhn).</summary>
public sealed record PartitaIva
{
    public string Valore { get; }

    private PartitaIva(string valore) => Valore = valore;

    public static PartitaIva Parse(string? input)
    {
        var p = Guard.NotEmpty(input, "Partita IVA").Replace(" ", "");
        if (p.StartsWith("IT", StringComparison.OrdinalIgnoreCase)) p = p[2..];
        if (p.Length != 11 || !p.All(char.IsDigit))
            throw new DomainException("La partita IVA deve essere composta da 11 cifre.");

        var somma = 0;
        for (var i = 0; i < 10; i++)
        {
            var d = p[i] - '0';
            if (i % 2 == 1) { d *= 2; if (d > 9) d -= 9; }
            somma += d;
        }
        if ((10 - somma % 10) % 10 != p[10] - '0')
            throw new DomainException("Cifra di controllo della partita IVA non valida.");
        return new PartitaIva(p);
    }

    public override string ToString() => Valore;
}
