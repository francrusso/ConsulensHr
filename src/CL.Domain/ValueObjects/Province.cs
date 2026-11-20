namespace CL.Domain.ValueObjects;

/// <summary>Sigle automobilistiche delle province e città metropolitane italiane (più "EE" per l'estero dove ammesso).</summary>
public static class Province
{
    public const string Estero = "EE";

    public static readonly IReadOnlySet<string> Sigle = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AG", "AL", "AN", "AO", "AP", "AQ", "AR", "AT", "AV", "BA", "BG", "BI", "BL", "BN", "BO", "BR", "BS", "BT", "BZ",
        "CA", "CB", "CE", "CH", "CL", "CN", "CO", "CR", "CS", "CT", "CZ", "EN", "FC", "FE", "FG", "FI", "FM", "FR", "GE",
        "GO", "GR", "IM", "IS", "KR", "LC", "LE", "LI", "LO", "LT", "LU", "MB", "MC", "ME", "MI", "MN", "MO", "MS", "MT",
        "NA", "NO", "NU", "OR", "PA", "PC", "PD", "PE", "PG", "PI", "PN", "PO", "PR", "PT", "PU", "PV", "PZ", "RA", "RC",
        "RE", "RG", "RI", "RM", "RN", "RO", "SA", "SI", "SO", "SP", "SR", "SS", "SU", "SV", "TA", "TE", "TN", "TO", "TP",
        "TR", "TS", "TV", "UD", "VA", "VB", "VC", "VE", "VI", "VR", "VT", "VV",
    };

    public static bool IsValida(string? sigla, bool ammessoEstero = false) =>
        sigla is { Length: 2 } && (Sigle.Contains(sigla) || (ammessoEstero && sigla.Equals(Estero, StringComparison.OrdinalIgnoreCase)));
}
