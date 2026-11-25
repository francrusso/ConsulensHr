using System.Text.RegularExpressions;
using CL.Domain.Common;

namespace CL.Domain.Notifiche;

/// <summary>
/// Numero di cellulare per WhatsApp in formato internazionale (E.164, es. +393331234567). Si accetta come lo scrive una
/// persona: spazi, trattini e parentesi si tolgono, "00" diventa "+", un cellulare italiano senza prefisso prende +39.
/// </summary>
public static partial class NumeroWhatsApp
{
    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164();

    public static string Normalizza(string? numero)
    {
        var n = new string((numero ?? "").Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (n.StartsWith("00")) n = "+" + n[2..];
        else if (!n.StartsWith('+') && n.StartsWith('3') && n.Length is 9 or 10) n = "+39" + n; // cellulare italiano
        if (!E164().IsMatch(n))
            throw new DomainException("Numero non valido: scrivilo con il prefisso internazionale, es. +39 333 1234567.");
        return n;
    }

    /// <summary>Il numero da mostrare: +39 333 123 4567 (raggruppato a tre cifre dopo il prefisso italiano).</summary>
    public static string Leggibile(string numero) =>
        numero.StartsWith("+39") && numero.Length > 6
            ? $"+39 {numero[3..6]} {numero[6..Math.Min(9, numero.Length)]} {numero[Math.Min(9, numero.Length)..]}".TrimEnd()
            : numero;
}
