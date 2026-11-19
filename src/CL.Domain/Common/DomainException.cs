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
}
