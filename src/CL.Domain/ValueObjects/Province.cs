namespace CL.Domain.ValueObjects;

/// <summary>
/// Sigle automobilistiche delle province e città metropolitane italiane (più "EE" per l'estero dove ammesso).
/// L'elenco delle sigle è quello ISTAT di <see cref="Comuni.Province"/>.
/// </summary>
public static class Province
{
    public const string Estero = "EE";

    public static bool IsValida(string? sigla, bool ammessoEstero = false) =>
        sigla is { Length: 2 } && (Comuni.Province.ContainsKey(sigla.ToUpperInvariant())
                                   || (ammessoEstero && sigla.Equals(Estero, StringComparison.OrdinalIgnoreCase)));
}
