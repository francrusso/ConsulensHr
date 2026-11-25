using CL.Domain.Common;
using CL.Domain.Notifiche;

namespace CL.Tests.Domain;

public sealed class NotificheTests
{
    [Fact]
    public void Ogni_tipologia_ha_una_sola_definizione_con_destinatari()
    {
        var tipi = Enum.GetValues<TipoNotifica>();
        Assert.Equal(tipi.Length, CatalogoNotifiche.Tutte.Count);
        Assert.Equal(tipi.Order(), CatalogoNotifiche.Tutte.Select(d => d.Tipo).Order());
        Assert.All(CatalogoNotifiche.Tutte, d =>
        {
            Assert.NotEqual(Destinatari.Nessuno, d.Destinatari);
            Assert.False(string.IsNullOrWhiteSpace(d.Nome));
            Assert.False(string.IsNullOrWhiteSpace(d.Descrizione));
        });
        // Il tipo è salvato come testo in una colonna di 32 caratteri.
        Assert.All(tipi, t => Assert.True(t.ToString().Length <= 32, t.ToString()));
    }

    [Fact]
    public void Ogni_ruolo_riceve_le_sue_tipologie()
    {
        var dipendente = CatalogoNotifiche.Per(Destinatari.Dipendente).Select(d => d.Tipo).ToList();
        Assert.Contains(TipoNotifica.BustaPagaPubblicata, dipendente);
        Assert.DoesNotContain(TipoNotifica.AssenzaDaApprovare, dipendente);
        // Chi è responsabile di qualcuno decide anche le sue assenze.
        Assert.Contains(TipoNotifica.AssenzaDaApprovare, CatalogoNotifiche.Per(Destinatari.Dipendente | Destinatari.Responsabile).Select(d => d.Tipo));
        Assert.DoesNotContain(TipoNotifica.PresenzeInviate, CatalogoNotifiche.Per(Destinatari.Referente).Select(d => d.Tipo));
        Assert.Contains(TipoNotifica.PresenzeInviate, CatalogoNotifiche.Per(Destinatari.Studio).Select(d => d.Tipo));
    }

    [Theory]
    [InlineData("https://altro-sito.it/ferie")]
    [InlineData("//altro-sito.it")]
    [InlineData("Assenze")]
    [InlineData("/\\altro-sito.it")]
    public void Il_link_resta_dentro_il_portale(string link) =>
        Assert.Throws<DomainException>(() => new Notifica("utente", TipoNotifica.AssenzaDecisa, "Titolo", "Testo", link, DateTime.UtcNow));

    [Fact]
    public void La_prima_lettura_resta()
    {
        var n = new Notifica("utente", TipoNotifica.AssenzaDecisa, "  Ferie approvate ", "Testo", "/Assenze", DateTime.UtcNow);
        Assert.Equal("Ferie approvate", n.Titolo);
        Assert.False(n.Letta);
        var prima = new DateTime(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);
        n.SegnaLetta(prima);
        n.SegnaLetta(prima.AddHours(2));
        Assert.Equal(prima, n.LettaIl);
    }

    [Fact]
    public void Titolo_e_testo_troppo_lunghi_si_accorciano()
    {
        var n = new Notifica("utente", TipoNotifica.AssenzaDecisa, new string('a', 300), new string('b', 3000), null, DateTime.UtcNow);
        Assert.Equal(Notifica.LunghezzaTitolo, n.Titolo.Length);
        Assert.Equal(Notifica.LunghezzaTesto, n.Testo.Length);
        Assert.EndsWith("…", n.Testo);
    }
}
