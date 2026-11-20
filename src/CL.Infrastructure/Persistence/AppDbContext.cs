using CL.Domain.Assenze;
using CL.Domain.Aziende;
using CL.Domain.BustePaga;
using CL.Domain.Dipendenti;
using CL.Domain.Organizzazione;
using CL.Domain.Presenze;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CL.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public const string Schema = "hr";

    public DbSet<Azienda> Aziende => Set<Azienda>();
    public DbSet<Dipendente> Dipendenti => Set<Dipendente>();
    public DbSet<Contratto> Contratti => Set<Contratto>();
    public DbSet<RichiestaAssenza> RichiesteAssenza => Set<RichiestaAssenza>();
    public DbSet<SaldoIniziale> SaldiIniziali => Set<SaldoIniziale>();
    public DbSet<BustaPaga> BustePaga => Set<BustaPaga>();
    public DbSet<CL.Domain.Documenti.CertificazioneUnica> CertificazioniUniche => Set<CL.Domain.Documenti.CertificazioneUnica>();
    public DbSet<ProfiloAzienda> ProfiliAzienda => Set<ProfiloAzienda>();
    public DbSet<ComunicazioneMensile> ComunicazioniPresenze => Set<ComunicazioneMensile>();
    public DbSet<ProfiloDipendente> ProfiliDipendente => Set<ProfiloDipendente>();
    public DbSet<Team> Team => Set<Team>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // tabelle Identity
        // Schema dedicato: su Supabase lo schema "public" è esposto dall'API REST (chiave anon),
        // "hr" invece non è esposto e non riceve i grant automatici ai ruoli anon/authenticated.
        builder.HasDefaultSchema(Schema);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<decimal>().HavePrecision(12, 2);
        c.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);

        c.Properties<CodiceFiscale>().HaveConversion<CodiceFiscaleConverter>().HaveMaxLength(16);
        c.Properties<PartitaIva>().HaveConversion<PartitaIvaConverter>().HaveMaxLength(11);
        c.Properties<Iban>().HaveConversion<IbanConverter>().HaveMaxLength(34);
    }
}
