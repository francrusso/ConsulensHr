using CL.Domain.BustePaga;
using CL.Domain.Common;

namespace CL.Tests.Domain;

public class BustaPagaTests
{
    private static BustaPaga Nuova(decimal lordo = 2000m, decimal netto = 1500m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 2026, 10, TipoCedolino.Ordinario, lordo, netto,
            "cedolino_10_2026.pdf", "aziende/x/2026/10/abc.pdf", "ABCDEF", 1024, Fixture.Adesso);

    [Fact]
    public void Visualizzazione_registrata_solo_la_prima_volta()
    {
        var b = Nuova();
        Assert.Throws<DomainException>(() => b.RegistraVisualizzazione(Fixture.Adesso)); // ancora in bozza

        b.Pubblica(Fixture.Adesso);
        b.RegistraVisualizzazione(Fixture.Adesso.AddDays(1));
        b.RegistraVisualizzazione(Fixture.Adesso.AddDays(2));

        Assert.Equal(Fixture.Adesso.AddDays(1), b.VisualizzataIl);
        Assert.Equal("10/2026", b.Periodo);
        Assert.Equal("abcdef", b.HashSha256);
    }

    [Fact]
    public void Netto_superiore_al_lordo_non_ammesso() =>
        Assert.Throws<DomainException>(() => Nuova(lordo: 1000m, netto: 1200m));

    [Fact]
    public void Ritiro_nasconde_il_cedolino()
    {
        var b = Nuova();
        b.Pubblica(Fixture.Adesso);
        b.Ritira();
        Assert.Throws<DomainException>(() => b.RegistraVisualizzazione(Fixture.Adesso));
    }
}
