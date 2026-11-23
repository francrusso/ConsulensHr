using CL.Domain.Assenze;

namespace CL.Tests.Domain;

public sealed class CalendarioLavorativoNomiTests
{
    [Fact]
    public void Ogni_festivita_nazionale_ha_il_suo_nome()
    {
        Assert.Equal(12, CalendarioLavorativo.FestivitaNazionali(2025).Count);
        Assert.Equal(13, CalendarioLavorativo.FestivitaNazionali(2026).Count); // con San Francesco d'Assisi
        Assert.Equal("Natale", CalendarioLavorativo.NomeFestivita(new DateOnly(2026, 12, 25)));
        Assert.Equal("Pasqua", CalendarioLavorativo.NomeFestivita(CalendarioLavorativo.Pasqua(2026)));
        Assert.Equal("Lunedì dell'Angelo", CalendarioLavorativo.NomeFestivita(CalendarioLavorativo.Pasqua(2026).AddDays(1)));
        Assert.Equal("Pasqua", CalendarioLavorativo.NomeFestivita(new DateOnly(2038, 4, 25))); // Pasqua il 25 aprile
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
