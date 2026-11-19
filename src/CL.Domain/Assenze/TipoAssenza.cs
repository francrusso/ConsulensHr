namespace CL.Domain.Assenze;

public enum TipoAssenza
{
    Ferie,
    Rol,
    ExFestivita,
    Malattia,
    PermessoL104,
    CongedoParentale,
    CongedoMatrimoniale,
    Lutto,
    PermessoNonRetribuito,
    Altro,
}

public static class TipoAssenzaExtensions
{
    /// <summary>Le assenze che maturano e consumano un saldo (conto ore).</summary>
    public static bool HaSaldo(this TipoAssenza tipo) =>
        tipo is TipoAssenza.Ferie or TipoAssenza.Rol or TipoAssenza.ExFestivita;

    /// <summary>Le assenze che si comunicano ma non richiedono approvazione (es. malattia con protocollo INPS).</summary>
    public static bool RichiedeApprovazione(this TipoAssenza tipo) => tipo is not TipoAssenza.Malattia;

    public static string Descrizione(this TipoAssenza tipo) => tipo switch
    {
        TipoAssenza.Ferie => "Ferie",
        TipoAssenza.Rol => "Permesso ROL",
        TipoAssenza.ExFestivita => "Ex-festività",
        TipoAssenza.Malattia => "Malattia",
        TipoAssenza.PermessoL104 => "Permesso L. 104/92",
        TipoAssenza.CongedoParentale => "Congedo parentale",
        TipoAssenza.CongedoMatrimoniale => "Congedo matrimoniale",
        TipoAssenza.Lutto => "Permesso per lutto",
        TipoAssenza.PermessoNonRetribuito => "Permesso non retribuito",
        _ => "Altro",
    };
}
