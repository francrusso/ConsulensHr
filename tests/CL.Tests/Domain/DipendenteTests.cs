using CL.Domain.Assenze;
using CL.Domain.Common;
using CL.Domain.Dipendenti;

namespace CL.Tests.Domain;

public class DipendenteTests
{
    [Fact]
    public void Contratto_part_time_calcola_orario_e_retribuzione()
    {
        var d = Fixture.Assunto(Fixture.Azienda(), new DateOnly(2026, 1, 1), partTime: 50);
        var c = d.ContrattoAttivo(new DateOnly(2026, 6, 1))!;

        Assert.Equal(20m, c.OreSettimanali);
        Assert.Equal(4m, c.OreGiornaliere);
        Assert.Equal(2000m, c.RetribuzioneMensileLorda);
        Assert.Equal(80m, c.SpettanzaAnnua(TipoAssenza.Ferie));
    }

    [Fact]
    public void Non_ammette_contratti_sovrapposti()
    {
        var d = Fixture.Assunto(Fixture.Azienda(), new DateOnly(2026, 1, 1));
        Assert.Throws<DomainException>(() =>
            d.Assumi(TipoContratto.TempoDeterminato, "Commercio", "3", new DateOnly(2026, 6, 1), new DateOnly(2026, 12, 31), 25_000m));
    }

    [Fact]
    public void Dopo_cessazione_si_puo_riassumere()
    {
        var d = Fixture.Assunto(Fixture.Azienda(), new DateOnly(2025, 1, 1));
        d.Cessa(new DateOnly(2025, 12, 31));
        d.Assumi(TipoContratto.TempoDeterminato, "Commercio", "3", new DateOnly(2026, 3, 1), new DateOnly(2026, 8, 31), 25_000m);

        Assert.False(d.InForzaIl(new DateOnly(2026, 1, 15)));
        Assert.Equal("3", d.ContrattoAttivo(new DateOnly(2026, 5, 1))!.Livello);
    }

    [Fact]
    public void Tempo_determinato_richiede_data_fine()
    {
        var d = Fixture.Dipendente(Fixture.Azienda());
        Assert.Throws<DomainException>(() =>
            d.Assumi(TipoContratto.TempoDeterminato, "Commercio", "4", new DateOnly(2026, 1, 1), null, 28_000m));
    }

    [Fact]
    public void Non_ammette_assunzione_sotto_i_16_anni()
    {
        var d = Fixture.Dipendente(Fixture.Azienda()); // nato il 10/12/1985
        Assert.Throws<DomainException>(() =>
            d.Assumi(TipoContratto.Apprendistato, "Commercio", "5", new DateOnly(2001, 12, 9), null, 15_000m));
    }
}
