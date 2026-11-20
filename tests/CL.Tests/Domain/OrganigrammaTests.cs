using CL.Domain.Common;
using CL.Domain.Organizzazione;

namespace CL.Tests.Domain;

public class OrganigrammaTests
{
    private static readonly Guid Azienda = Guid.NewGuid();

    [Fact]
    public void Approva_il_responsabile_diretto_poi_quello_del_team_risalendo_la_gerarchia()
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
        Assert.Equal(anna, Organigramma.Approvatore(new(luca, nord.Id, null), tutti));          // il responsabile sale al team superiore con un responsabile
        Assert.Equal(anna, Organigramma.Approvatore(new(mario, vendite.Id, null), tutti));      // team senza responsabile: si risale
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
}
