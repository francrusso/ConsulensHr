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

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, PerimetroAziende? perimetro = null) : IdentityDbContext<ApplicationUser>(options)
{
    /// <summary>Aziende visibili nella richiesta corrente (null = tutte). Letto a ogni query dai filtri globali.</summary>
    private IReadOnlyCollection<Guid>? Consentite => perimetro?.Consentite;
    public DbSet<AssegnazioneAzienda> AssegnazioniAziende => Set<AssegnazioneAzienda>();

    public const string Schema = "hr";

    public DbSet<Azienda> Aziende => Set<Azienda>();
    public DbSet<Dipendente> Dipendenti => Set<Dipendente>();
    public DbSet<Contratto> Contratti => Set<Contratto>();
    public DbSet<RichiestaAssenza> RichiesteAssenza => Set<RichiestaAssenza>();
    public DbSet<SaldoIniziale> SaldiIniziali => Set<SaldoIniziale>();
    public DbSet<BustaPaga> BustePaga => Set<BustaPaga>();
    public DbSet<ChiusuraAziendale> ChiusureAziendali => Set<ChiusuraAziendale>();
    public DbSet<CL.Domain.Presenze.GiornoInUfficio> GiorniInUfficio => Set<CL.Domain.Presenze.GiornoInUfficio>();
    public DbSet<CL.Domain.Presenze.GiornoSmartWorking> GiorniSmartWorking => Set<CL.Domain.Presenze.GiornoSmartWorking>();
    public DbSet<CL.Domain.Presenze.Straordinario> Straordinari => Set<CL.Domain.Presenze.Straordinario>();
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

        builder.Entity<AssegnazioneAzienda>(b =>
        {
            b.ToTable("AziendeConsulenti");
            b.HasKey(x => new { x.AziendaId, x.UserId });
            b.Property(x => x.UserId).HasMaxLength(450);
            b.HasOne<Azienda>().WithMany().HasForeignKey(x => x.AziendaId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.UserId);
        });

        // Ogni consulente vede solo le aziende assegnate (o create da lui) e tutto ciò che vi appartiene.
        builder.Entity<Azienda>().HasQueryFilter(a => Consentite == null || Consentite.Contains(a.Id));
        builder.Entity<Dipendente>().HasQueryFilter(d => Consentite == null || Consentite.Contains(d.AziendaId));
        builder.Entity<BustaPaga>().HasQueryFilter(x => Consentite == null || Consentite.Contains(x.AziendaId));
        builder.Entity<ChiusuraAziendale>().HasQueryFilter(x => Consentite == null || Consentite.Contains(x.AziendaId));
        builder.Entity<CL.Domain.Documenti.CertificazioneUnica>().HasQueryFilter(x => Consentite == null || Consentite.Contains(x.AziendaId));
        builder.Entity<ComunicazioneMensile>().HasQueryFilter(x => Consentite == null || Consentite.Contains(x.AziendaId));
        builder.Entity<ProfiloAzienda>().HasQueryFilter(x => Consentite == null || Consentite.Contains(x.AziendaId));
        builder.Entity<Team>().HasQueryFilter(x => Consentite == null || Consentite.Contains(x.AziendaId));
        // Dati della persona: visibili se lo è la persona.
        builder.Entity<Contratto>().HasQueryFilter(x => Consentite == null || Set<Dipendente>().Any(d => d.Id == x.DipendenteId));
        builder.Entity<RichiestaAssenza>().HasQueryFilter(x => Consentite == null || Set<Dipendente>().Any(d => d.Id == x.DipendenteId));
        builder.Entity<SaldoIniziale>().HasQueryFilter(x => Consentite == null || Set<Dipendente>().Any(d => d.Id == x.DipendenteId));
        builder.Entity<GiornoInUfficio>().HasQueryFilter(x => Consentite == null || Set<Dipendente>().Any(d => d.Id == x.DipendenteId));
        builder.Entity<GiornoSmartWorking>().HasQueryFilter(x => Consentite == null || Set<Dipendente>().Any(d => d.Id == x.DipendenteId));
        builder.Entity<Straordinario>().HasQueryFilter(x => Consentite == null || Set<Dipendente>().Any(d => d.Id == x.DipendenteId));
        builder.Entity<ProfiloDipendente>().HasQueryFilter(x => Consentite == null || Set<Dipendente>().Any(d => d.Id == x.DipendenteId));
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
