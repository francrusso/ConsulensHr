using CL.Domain.Assenze;

namespace CL.Tests.Domain;

public sealed class CalendarioLavorativoNomiTests
{
    [Fact]
    public void Ogni_festivita_nazionale_ha_il_suo_nome()
    {
        foreach (var anno in new[] { 2025, 2026, 2027 })
            foreach (var g in CalendarioLavorativo.FestivitaNazionali(anno))
                Assert.NotNull(CalendarioLavorativo.NomeFestivita(g));
        Assert.Equal("Natale", CalendarioLavorativo.NomeFestivita(new DateOnly(2026, 12, 25)));
        Assert.Equal("Pasqua", CalendarioLavorativo.NomeFestivita(CalendarioLavorativo.Pasqua(2026)));
        Assert.Equal("Santo patrono", CalendarioLavorativo.NomeFestivita(new DateOnly(2026, 12, 7), 12, 7));
        Assert.Null(CalendarioLavorativo.NomeFestivita(new DateOnly(2026, 11, 23)));
    }
}

public sealed class ProfiloSenzaContrattiTests
{
    [Fact]
    public void Senza_contratti_attivi_le_posizioni_non_mancano()
    {
        var p = new CL.Domain.Aziende.ProfiloAzienda(Guid.NewGuid());
        Assert.Contains("contratto e posizioni INPS/INAIL", p.Mancanti());
        p.ImpostaSenzaContratti();
        Assert.True(p.SenzaContratti);
        Assert.DoesNotContain("contratto e posizioni INPS/INAIL", p.Mancanti());
    }
}
