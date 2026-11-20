using CL.Domain.Common;
using CL.Domain.Dipendenti;
using CL.Domain.Organizzazione;

namespace CL.Tests.Domain;

public class OrganigrammaTests
{
    private static readonly Guid Azienda = Guid.NewGuid();

    [Fact]
    public void Approva_il_responsabile_diretto_poi_quello_del_team_altrimenti_il_referente()
    {
        var (anna, luca, sara, mario) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var direzione = new Team(Azienda, "Direzione");
        var vendite = new Team(Azienda, "Vendite");
        var nord = new Team(Azienda, "Vendite Nord");
        var tutti = new[] { direzione, vendite, nord };
        vendite.ImpostaPadre(direzione, tutti);
        nord.ImpostaPadre(vendite, tutti);
        typeof(Team).GetProperty(nameof(Team.ResponsabileId))!.SetValue(direzione, anna);
        typeof(Team).GetProperty(nameof(Team.ResponsabileId))!.SetValue(nord, luca);

        Assert.Equal(luca, Organigramma.Approvatore(new(sara, nord.Id, null), tutti));          // responsabile del team
        Assert.Null(Organigramma.Approvatore(new(mario, vendite.Id, null), tutti));             // team senza responsabile: referente dell'azienda
        Assert.Null(Organigramma.Approvatore(new(luca, nord.Id, null), tutti));                 // il responsabile sale a Vendite, che non ne ha: referente
        typeof(Team).GetProperty(nameof(Team.ResponsabileId))!.SetValue(vendite, mario);
        Assert.Equal(mario, Organigramma.Approvatore(new(luca, nord.Id, null), tutti));         // il responsabile di Vendite Nord lo approva quello di Vendite
        Assert.Equal(anna, Organigramma.Approvatore(new(mario, vendite.Id, null), tutti));      // e quello di Vendite il responsabile della Direzione
        Assert.Equal(mario, Organigramma.Approvatore(new(sara, nord.Id, mario), tutti));        // il responsabile diretto prevale
        Assert.Null(Organigramma.Approvatore(new(anna, direzione.Id, null), tutti));            // in cima: decide il referente
        Assert.Null(Organigramma.Approvatore(new(sara, null, null), tutti));                    // senza team né responsabile
    }

    [Fact]
    public void Gerarchia_dei_team_senza_cicli()
    {
        var a = new Team(Azienda, "A");
        var b = new Team(Azienda, "B");
        var c = new Team(Azienda, "C");
        var tutti = new[] { a, b, c };
        b.ImpostaPadre(a, tutti);
        c.ImpostaPadre(b, tutti);
        Assert.Throws<DomainException>(() => a.ImpostaPadre(c, tutti));
        Assert.Throws<DomainException>(() => a.ImpostaPadre(a, tutti));
        Assert.Throws<DomainException>(() => a.ImpostaPadre(new Team(Guid.NewGuid(), "Altra azienda"), tutti));
    }

    [Fact]
    public void Un_dipendente_non_e_responsabile_di_se_stesso()
    {
        var azienda = Fixture.Azienda();
        var d = Fixture.Dipendente(azienda);
        Assert.Throws<DomainException>(() => d.ImpostaOrganizzazione(null, "Commesso", d));
    }

    [Theory]
    [InlineData(null, null, "neutro")]                                   // niente dichiarato: neutro (mai dedotto dal codice fiscale)
    [InlineData(Genere.Donna, null, "donna")]
    [InlineData(Genere.Uomo, null, "uomo")]
    [InlineData(Genere.NonBinario, null, "neutro")]
    [InlineData(Genere.NonIndicato, Pronomi.NonIndicati, "neutro")]
    [InlineData(null, Pronomi.Femminili, "donna")]
    [InlineData(null, Pronomi.Maschili, "uomo")]
    [InlineData(Genere.Uomo, Pronomi.Neutri, "neutro")]                  // i pronomi contano più del genere
    [InlineData(Genere.Donna, Pronomi.Tutti, "neutro")]
    [InlineData(Genere.NonBinario, Pronomi.Femminili, "donna")]
    public void Segnaposto_della_foto_da_pronomi_e_genere(Genere? genere, Pronomi? pronomi, string atteso) =>
        Assert.Equal(atteso, Dipendente.SegnapostoPer(genere, pronomi));

    [Fact]
    public void I_sotto_team_hanno_il_colore_del_team_madre_piu_chiaro()
    {
        var az = Guid.NewGuid();
        var madre = new Team(az, "Vendite");
        madre.ImpostaAspetto("#F7C9B0", null);
        var figlio = new Team(az, "Vendite Nord");
        figlio.ImpostaAspetto("#B5CCFB", null); // colore proprio ignorato: conta quello della madre
        var tutti = new List<Team> { madre, figlio };
        figlio.ImpostaPadre(madre, tutti);
        var nipote = new Team(az, "Milano");
        tutti.Add(nipote);
        nipote.ImpostaPadre(figlio, tutti);

        Assert.Equal("#F7C9B0", Organigramma.ColoreDi(madre, tutti));
        var c1 = Organigramma.ColoreDi(figlio, tutti);
        var c2 = Organigramma.ColoreDi(nipote, tutti);
        Assert.Equal(Team.Schiarisci("#F7C9B0", 0.45), c1);
        Assert.True(Luce(c2) > Luce(c1) && Luce(c1) > Luce("#F7C9B0"));
        Assert.Equal("#FFFFFF", Team.Schiarisci("#F7C9B0", 1));

        static int Luce(string h) => Convert.ToInt32(h[1..3], 16) + Convert.ToInt32(h[3..5], 16) + Convert.ToInt32(h[5..7], 16);
    }
}
