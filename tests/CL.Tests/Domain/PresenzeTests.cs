using CL.Domain.Assenze;
using CL.Domain.Common;
using CL.Domain.Presenze;

namespace CL.Tests.Domain;

public class PresenzeTests
{
    [Fact]
    public void Assenza_a_cavallo_di_due_mesi_si_ripartisce_sui_giorni_lavorativi()
    {
        var azienda = Fixture.Azienda();
        var dip = Fixture.Assunto(azienda, new DateOnly(2026, 1, 1));
        // Da lunedì 26/10 a venerdì 6/11/2026: 5 giorni lavorativi in ottobre, 5 in novembre (l'1/11 è domenica).
        var ferie = PianificatoreAssenze.Crea(dip, azienda, TipoAssenza.Ferie, new(2026, 10, 26), new(2026, 11, 6), [], Fixture.Adesso);
        var rol = PianificatoreAssenze.Crea(dip, azienda, TipoAssenza.Rol, new(2026, 10, 15), new(2026, 10, 15), [], Fixture.Adesso, orePermesso: 2);
        var malattia = PianificatoreAssenze.Crea(dip, azienda, TipoAssenza.Malattia, new(2026, 10, 7), new(2026, 10, 8), [], Fixture.Adesso);
        var inAttesa = PianificatoreAssenze.Crea(dip, azienda, TipoAssenza.Ferie, new(2026, 10, 20), new(2026, 10, 20), [], Fixture.Adesso);
        ferie.Approva("hr", Fixture.Adesso);
        rol.Approva("hr", Fixture.Adesso);

        var ottobre = ComunicazioneMensile.AssenzeDi([ferie, rol, malattia, inAttesa], 2026, 10, dip.ContrattoAttivo(new(2026, 10, 1)), null, null);
        var novembre = ComunicazioneMensile.AssenzeDi([ferie, rol, malattia, inAttesa], 2026, 11, dip.ContrattoAttivo(new(2026, 11, 1)), null, null);

        Assert.Equal(40m, ottobre.Ferie); // la richiesta ancora in attesa non conta
        Assert.Equal(2m, ottobre.Rol);
        Assert.Equal(16m, ottobre.Malattia);
        Assert.Equal(40m, novembre.Ferie);
        Assert.Equal(0m, novembre.Rol);
    }

    [Fact]
    public void Dopo_l_invio_l_azienda_non_modifica_finche_lo_studio_non_riapre()
    {
        var c = new ComunicazioneMensile(Guid.NewGuid(), 2026, 10);
        var dip = Guid.NewGuid();
        c.ImpostaVoci(dip, VociMensili.Nessuna with { OreStraordinario = 5 });
        c.Invia(new Dictionary<Guid, AssenzeDelMese> { [dip] = new(8, 0, 0, 0, 0), [Guid.NewGuid()] = AssenzeDelMese.Nessuna }, "hr", Fixture.Adesso);

        Assert.Equal(StatoComunicazione.Inviata, c.Stato);
        Assert.Equal(2, c.Righe.Count); // creata anche la riga del dipendente senza variazioni
        Assert.Equal(8m, c.RigaDi(dip)!.OreFerie);
        Assert.Throws<DomainException>(() => c.ImpostaVoci(dip, VociMensili.Nessuna));

        c.SegnaElaborata("studio", Fixture.Adesso);
        c.Riapri();
        c.ImpostaVoci(dip, VociMensili.Nessuna with { OreStraordinario = 6 });
        Assert.Equal(6m, c.RigaDi(dip)!.OreStraordinario);
        Assert.Throws<DomainException>(() => c.ImpostaVoci(dip, VociMensili.Nessuna with { BuoniPasto = 40 }));
    }
}
