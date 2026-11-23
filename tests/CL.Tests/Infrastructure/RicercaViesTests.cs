using CL.Infrastructure.Registro;

namespace CL.Tests.Infrastructure;

public class RicercaViesTests
{
    [Fact]
    public void Scompone_l_indirizzo_italiano_di_vies()
    {
        var (via, cap, comune, prov) = RicercaVies.ScomponiIndirizzo("PIAZZALE ENRICO MATTEI 1 \n00144 ROMA RM\n");
        Assert.Equal("Piazzale Enrico Mattei 1", via);
        Assert.Equal("00144", cap);
        Assert.Equal("Roma", comune);
        Assert.Equal("RM", prov);

        (via, _, comune, _) = RicercaVies.ScomponiIndirizzo("VIA DEI MILLE 5\nSCALA B\n80121 NAPOLI NA");
        Assert.Equal("Via Dei Mille 5, Scala B", via);
        Assert.Equal("Napoli", comune);

        // Formato inatteso: tutto nell'indirizzo, niente invenzioni.
        (via, cap, comune, _) = RicercaVies.ScomponiIndirizzo("SOMEWHERE STREET 3");
        Assert.Equal("Somewhere Street 3", via);
        Assert.Null(cap);
        Assert.Null(comune);
    }

    [Theory]
    [InlineData("ENI S.P.A.", "Eni S.p.A.")]
    [InlineData("ROSSI MARIO SRL", "Rossi Mario S.r.l.")]
    [InlineData("BAR SPORT DI BIANCHI LUCA & C. SNC", "Bar Sport Di Bianchi Luca & C. S.n.c.")]
    public void Normalizza_maiuscole_e_forme_societarie(string dalRegistro, string atteso) =>
        Assert.Equal(atteso, RicercaVies.Maiuscole(dalRegistro));
}
