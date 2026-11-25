using CL.Domain.Assenze;
using CL.Domain.BustePaga;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Archivio;
using CL.Infrastructure.Persistence;
using CL.Tests.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CL.Tests.Infrastructure;

/// <summary>Verifica mapping e vincoli su SQLite in memoria (lo schema reale è PostgreSQL).</summary>
public sealed class PersistenzaTests : IDisposable
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");

    public PersistenzaTests()
    {
        _conn.Open();
        using var db = NuovoContesto();
        db.Database.EnsureCreated();
    }

    private AppDbContext NuovoContesto() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);

    public void Dispose() => _conn.Dispose();

    [Fact]
    public void Salva_e_rilegge_dipendente_con_contratti_e_value_object()
    {
        var azienda = Fixture.Azienda();
        var dip = Fixture.Assunto(azienda, new DateOnly(2026, 1, 1), partTime: 75);
        dip.ImpostaIban(Iban.Parse("IT60X0542811101000000123456"));

        using (var db = NuovoContesto())
        {
            db.AddRange(azienda, dip);
            db.SaveChanges();
        }

        using (var db = NuovoContesto())
        {
            var letto = db.Dipendenti.Single(d => d.Id == dip.Id); // Contratti in AutoInclude
            Assert.Equal("RSSMRA85T10A562S", letto.CodiceFiscale.Valore);
            Assert.Equal("IT60X0542811101000000123456", letto.Iban!.Valore);
            var contratto = Assert.Single(letto.Contratti);
            Assert.Equal(75m, contratto.PercentualePartTime);
            Assert.Equal(6m, contratto.OreGiornaliere);
        }
    }

    [Fact]
    public void Ricerca_per_codice_fiscale()
    {
        var azienda = Fixture.Azienda();
        var dip = Fixture.Dipendente(azienda);
        using (var db = NuovoContesto()) { db.AddRange(azienda, dip); db.SaveChanges(); }

        using var db2 = NuovoContesto();
        var cf = CodiceFiscale.Parse("RSSMRA85T10A562S");
        Assert.NotNull(db2.Dipendenti.SingleOrDefault(d => d.CodiceFiscale == cf));
    }

    [Fact]
    public void Stesso_codice_fiscale_due_volte_nella_stessa_azienda_viola_il_vincolo()
    {
        var azienda = Fixture.Azienda();
        using var db = NuovoContesto();
        db.AddRange(azienda, Fixture.Dipendente(azienda), Fixture.Dipendente(azienda));
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void Richieste_saldi_e_buste_paga_rispettano_il_flusso()
    {
        var azienda = Fixture.Azienda();
        var dip = Fixture.Assunto(azienda, new DateOnly(2026, 1, 1));
        var ferie = PianificatoreAssenze.Crea(dip, azienda, TipoAssenza.Ferie,
            new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 14), [], Fixture.Adesso);
        var busta = new BustaPaga(azienda.Id, dip.Id, 2026, 10, TipoCedolino.Ordinario, 2000m, 1500m,
            "cedolino.pdf", "k/1.pdf", new string('a', 64), 1234, Fixture.Adesso);

        using (var db = NuovoContesto())
        {
            db.AddRange(azienda, dip, ferie, busta, new SaldoIniziale(dip.Id, 2026, TipoAssenza.Ferie, 16m));
            db.SaveChanges();
        }

        using (var db = NuovoContesto())
        {
            var r = db.RichiesteAssenza.Single();
            r.Approva("consulente@consulente.test", Fixture.Adesso);
            db.BustePaga.Single().Pubblica(Fixture.Adesso);
            db.SaveChanges();
        }

        using (var db = NuovoContesto())
        {
            var d = db.Dipendenti.Single();
            var saldo = CalcoloSaldi.Calcola(d, TipoAssenza.Ferie, 2026, 12,
                db.RichiesteAssenza.Where(r => r.DipendenteId == d.Id).ToList(),
                db.SaldiIniziali.Single(s => s.DipendenteId == d.Id && s.Anno == 2026 && s.Tipo == TipoAssenza.Ferie));

            Assert.Equal(StatoRichiesta.Approvata, db.RichiesteAssenza.Single().Stato);
            Assert.Equal(16m + 160m - 40m, saldo.Residuo);
            Assert.Equal(StatoBustaPaga.Pubblicata, db.BustePaga.Single().Stato);
        }
    }

    [Fact]
    public void Non_si_cancella_un_dipendente_con_buste_paga()
    {
        var azienda = Fixture.Azienda();
        var dip = Fixture.Dipendente(azienda);
        using (var db = NuovoContesto())
        {
            db.AddRange(azienda, dip, new BustaPaga(azienda.Id, dip.Id, 2026, 1, TipoCedolino.Ordinario, 1m, 1m,
                "c.pdf", "k", new string('b', 64), 1, Fixture.Adesso));
            db.SaveChanges();
        }

        using var db2 = NuovoContesto();
        db2.Remove(db2.Dipendenti.Single());
        Assert.Throws<DbUpdateException>(() => db2.SaveChanges());
    }

    [Fact]
    public async Task Archivio_nel_database_salva_apre_ed_elimina_senza_sovrascrivere()
    {
        await using var servizi = new ServiceCollection().AddDbContext<AppDbContext>(o => o.UseSqlite(_conn)).BuildServiceProvider();
        var archivio = new ArchivioNelDatabase(servizi.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        const string chiave = "buste/2026/cedolino-1.pdf";

        await archivio.SalvaAsync(chiave, new MemoryStream([1, 2, 3]));
        await using (var letto = await archivio.ApriAsync(chiave))
        {
            Assert.NotNull(letto);
            using var copia = new MemoryStream();
            await letto.CopyToAsync(copia);
            Assert.Equal([1, 2, 3], copia.ToArray());
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => archivio.SalvaAsync(chiave, new MemoryStream([9])));
        await Assert.ThrowsAsync<ArgumentException>(() => archivio.ApriAsync("../segreti.pdf"));

        await archivio.EliminaAsync(chiave);
        Assert.Null(await archivio.ApriAsync(chiave));
    }
}
