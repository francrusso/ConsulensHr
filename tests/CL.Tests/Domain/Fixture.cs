using CL.Domain.Aziende;
using CL.Domain.Dipendenti;
using CL.Domain.ValueObjects;

namespace CL.Tests.Domain;

internal static class Fixture
{
    public static readonly DateTime Adesso = new(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);

    public static Azienda Azienda() => new("Acme S.r.l.", PartitaIva.Parse("01234567897"));

    public static Dipendente Dipendente(Azienda azienda) =>
        new(azienda.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 12, 10));

    public static Dipendente Assunto(Azienda azienda, DateOnly dal, decimal partTime = 100)
    {
        var d = Dipendente(azienda);
        d.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", dal, null, 28_000m,
            mensilitaAnnue: 14, percentualePartTime: partTime);
        return d;
    }
}
