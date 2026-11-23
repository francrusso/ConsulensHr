namespace CL.Domain.Common;

/// <summary>Violazione di una regola di dominio (messaggio pensato per l'utente).</summary>
public sealed class DomainException(string message) : Exception(message);

internal static class Guard
{
    public static string NotEmpty(string? value, string campo)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"Il campo '{campo}' è obbligatorio.");
        return value.Trim();
    }

    public static decimal NonNegative(decimal value, string campo)
    {
        if (value < 0)
            throw new DomainException($"Il campo '{campo}' non può essere negativo.");
        return value;
    }

    /// <summary>Testo facoltativo: senza spazi ai lati, null se vuoto.</summary>
    public static string? Facoltativo(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Nome di un file caricato, senza percorso e al massimo di 200 caratteri; se manca, <paramref name="predefinito"/>.</summary>
    public static string NomeFile(string? nomeFile, string predefinito)
    {
        var nome = Path.GetFileName(nomeFile ?? "").Trim();
        return nome.Length == 0 ? predefinito : nome.Length > 200 ? nome[..200] : nome;
    }
}
