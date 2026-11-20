using CL.Domain.Assenze;
using CL.Domain.Common;

namespace CL.Tests.Domain;

public class CalendarioTests
{
    [Theory]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    public void Pasqua(int anno, int mese, int giorno) =>
        Assert.Equal(new DateOnly(anno, mese, giorno), CalendarioLavorativo.Pasqua(anno));

    [Fact]
    public void San_Francesco_festivo_dal_2026()
    {
        Assert.False(CalendarioLavorativo.IsFestivo(new DateOnly(2025, 10, 4)));
        Assert.True(CalendarioLavorativo.IsFestivo(new DateOnly(2026, 10, 4)));
    }

    [Fact]
    public void Santo_patrono_e_festivo() =>
        Assert.False(CalendarioLavorativo.IsLavorativo(new DateOnly(2026, 6, 29), 5, patronoMese: 6, patronoGiorno: 29));

    [Fact]
    public void Sabato_lavorativo_solo_con_settimana_di_6_giorni()
    {
        var sabato = new DateOnly(2026, 7, 4);
        Assert.False(CalendarioLavorativo.IsLavorativo(sabato, 5));
        Assert.True(CalendarioLavorativo.IsLavorativo(sabato, 6));
    }
}

public class PianificatoreAssenzeTests
{
    private readonly CL.Domain.Aziende.Azienda _azienda = Fixture.Azienda();

    private RichiestaAssenza Crea(CL.Domain.Dipendenti.Dipendente d, TipoAssenza tipo, DateOnly dal, DateOnly al,
        IEnumerable<RichiestaAssenza>? esistenti = null, decimal? ore = null, decimal? saldo = null) =>
        PianificatoreAssenze.Crea(d, _azienda, tipo, dal, al, esistenti ?? [], Fixture.Adesso, ore, saldo);

    [Fact]
    public void Ferie_due_settimane_contano_solo_i_giorni_lavorativi()
    {
        var d = Fixture.Assunto(_azienda, new DateOnly(2026, 1, 1));
        var r = Crea(d, TipoAssenza.Ferie, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 23));

        Assert.Equal(80m, r.Ore); // 10 giorni × 8 ore; il 15/08 cade di sabato
        Assert.Equal(StatoRichiesta.InAttesa, r.Stato);
    }

    [Fact]
    public void Festivita_esclusa_dal_conteggio()
    {
        var d = Fixture.Assunto(_azienda, new DateOnly(2026, 1, 1));
        var r = Crea(d, TipoAssenza.Ferie, new DateOnly(2026, 12, 21), new DateOnly(2026, 12, 25));
        Assert.Equal(32m, r.Ore);
    }

    [Fact]
    public void Part_time_usa_orario_ridotto()
    {
        var d = Fixture.Assunto(_azienda, new DateOnly(2026, 1, 1), partTime: 50);
        var r = Crea(d, TipoAssenza.Ferie, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11));
        Assert.Equal(20m, r.Ore);
    }

    [Fact]
    public void Malattia_non_richiede_approvazione()
    {
        var d = Fixture.Assunto(_azienda, new DateOnly(2026, 1, 1));
        var r = Crea(d, TipoAssenza.Malattia, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 8));
        Assert.Equal(StatoRichiesta.Approvata, r.Stato);
    }

    [Fact]
    public void Rifiuta_sovrapposizione_con_richiesta_attiva()
    {
        var d = Fixture.Assunto(_azienda, new DateOnly(2026, 1, 1));
        var prima = Crea(d, TipoAssenza.Ferie, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 14));

        var ex = Assert.Throws<DomainException>(() =>
            Crea(d, TipoAssenza.Rol, new DateOnly(2026, 8, 14), new DateOnly(2026, 8, 14), [prima], ore: 2));
        Assert.Contains("si sovrappone", ex.Message);
    }

    [Fact]
    public void Ammette_sovrapposizione_con_richiesta_annullata()
    {
        var d = Fixture.Assunto(_azienda, new DateOnly(2026, 1, 1));
        var prima = Crea(d, TipoAssenza.Ferie, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 14));
        prima.Annulla(new DateOnly(2026, 7, 1));

        var r = Crea(d, TipoAssenza.Ferie, new DateOnly(2026, 8, 12), new DateOnly(2026, 8, 12), [prima]);
        Assert.Equal(8m, r.Ore);
    }

    [Fact]
    public void Piu_permessi_a_ore_nello_stesso_giorno_entro_orario()
    {
        var d = Fixture.Assunto(_azienda, new DateOnly(2026, 1, 1));
        var giorno = new DateOnly(2026, 9, 7);
        var mattina = Crea(d, TipoAssenza.Rol, giorno, giorno, ore: 4);
        var pomeriggio = Crea(d, TipoAssenza.Rol, giorno, giorno, [mattina], ore: 4);

        Assert.Equal(4m, pomeriggio.Ore);
        Assert.Throws<DomainException>(() => Crea(d, TipoAssenza.Rol, giorno, giorno, [mattina, pomeriggio], ore: 1));
    }

    [Fact]
    public void Rifiuta_se_saldo_insufficiente()
    {
        var d = Fixture.Assunto(_azienda, new DateOnly(2026, 1, 1));
        Assert.Throws<DomainException>(() =>
            Crea(d, TipoAssenza.Ferie, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 14), saldo: 39.5m));
    }

    [Fact]
    public void Rifiuta_periodo_fuori_contratto_o_a_cavallo_d_anno()
    {
        var d = Fixture.Assunto(_azienda, new DateOnly(2026, 9, 1));
        Assert.Throws<DomainException>(() => Crea(d, TipoAssenza.Ferie, new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 2)));
        Assert.Throws<DomainException>(() => Crea(d, TipoAssenza.Ferie, new DateOnly(2026, 12, 28), new DateOnly(2027, 1, 4)));
    }

    [Fact]
    public void Ciclo_di_vita_richiesta()
    {
        var d = Fixture.Assunto(_azienda, new DateOnly(2026, 1, 1));
        var r = Crea(d, TipoAssenza.Ferie, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 14));

        r.Approva("consulente@consulente.it", Fixture.Adesso);
        Assert.Equal(StatoRichiesta.Approvata, r.Stato);
        Assert.Throws<DomainException>(() => r.Rifiuta("x", "motivo", Fixture.Adesso));
        Assert.Throws<DomainException>(() => r.Annulla(new DateOnly(2026, 8, 11))); // già iniziata
        r.Annulla(new DateOnly(2026, 8, 1));
        Assert.Equal(StatoRichiesta.Annullata, r.Stato);
    }
}

public class CalcoloSaldiTests
{
    private readonly CL.Domain.Aziende.Azienda _azienda = Fixture.Azienda();

    [Fact]
    public void Matura_in_dodicesimi_e_scala_goduto_e_prenotato()
    {
        var d = Fixture.Assunto(_azienda, new DateOnly(2026, 1, 1));
        var approvata = PianificatoreAssenze.Crea(d, _azienda, TipoAssenza.Ferie,
            new DateOnly(2026, 6, 8), new DateOnly(2026, 6, 12), [], Fixture.Adesso); // lun–ven → 40h
        approvata.Approva("consulente", Fixture.Adesso);
        var inAttesa = PianificatoreAssenze.Crea(d, _azienda, TipoAssenza.Ferie,
            new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 11), [approvata], Fixture.Adesso); // 16h

        var saldo = CalcoloSaldi.Calcola(d, TipoAssenza.Ferie, 2026, 6, [approvata, inAttesa],
            new SaldoIniziale(d.Id, 2026, TipoAssenza.Ferie, 12m));

        Assert.Equal(80m, saldo.Maturato);
        Assert.Equal(40m, saldo.Goduto);
        Assert.Equal(16m, saldo.Prenotato);
        Assert.Equal(52m, saldo.Residuo);
        Assert.Equal(36m, saldo.Disponibile);
    }

    [Theory]
    [InlineData(16, true)]  // 16 giorni a marzo
    [InlineData(17, true)]  // 15 giorni
    [InlineData(18, false)] // 14 giorni
    public void Mese_di_assunzione_matura_con_almeno_15_giorni(int giornoAssunzione, bool matura)
    {
        var d = Fixture.Assunto(_azienda, new DateOnly(2026, 3, giornoAssunzione));
        var rol = CalcoloSaldi.MaturatoNelMese(d, TipoAssenza.Rol, 2026, 3);
        Assert.Equal(matura ? 6m : 0m, rol);
    }

    [Fact]
    public void Tipi_senza_saldo_non_calcolabili()
    {
        var d = Fixture.Assunto(_azienda, new DateOnly(2026, 1, 1));
        Assert.Throws<DomainException>(() => CalcoloSaldi.Calcola(d, TipoAssenza.Malattia, 2026, 12, []));
    }
}
