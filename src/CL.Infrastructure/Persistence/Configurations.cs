using CL.Domain.Assenze;
using CL.Domain.Aziende;
using CL.Domain.BustePaga;
using CL.Domain.Dipendenti;
using CL.Domain.Presenze;
using CL.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CL.Infrastructure.Persistence;

internal sealed class AziendaConfig : IEntityTypeConfiguration<Azienda>
{
    public void Configure(EntityTypeBuilder<Azienda> b)
    {
        b.ToTable("Aziende");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.RagioneSociale).HasMaxLength(200);
        b.Property(x => x.CodiceFiscale).HasMaxLength(16);
        b.Property(x => x.Indirizzo).HasMaxLength(300);
        b.Property(x => x.Comune).HasMaxLength(100);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Cap).HasMaxLength(5);
        b.Property(x => x.Provincia).HasMaxLength(2);
        b.Property(x => x.LogoChiave).HasMaxLength(500);
        b.Ignore(x => x.SedeCompleta);
        b.HasIndex(x => x.PartitaIva).IsUnique();
    }
}

internal sealed class ProfiloAziendaConfig : IEntityTypeConfiguration<ProfiloAzienda>
{
    public void Configure(EntityTypeBuilder<ProfiloAzienda> b)
    {
        b.ToTable("ProfiliAzienda");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasOne<Azienda>().WithOne().HasForeignKey<ProfiloAzienda>(x => x.AziendaId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.AziendaId).IsUnique();
        b.Ignore(x => x.Societari);
        b.Ignore(x => x.SedeLegale);
        b.Ignore(x => x.SedeOperativa);
        b.Ignore(x => x.Completato);

        foreach (var p in new[] { nameof(ProfiloAzienda.FormaGiuridica), nameof(ProfiloAzienda.Ccnl), nameof(ProfiloAzienda.CassaEdile),
                     nameof(ProfiloAzienda.EnteBilaterale), nameof(ProfiloAzienda.FondoPensione), nameof(ProfiloAzienda.SedeLegaleComune),
                     nameof(ProfiloAzienda.SedeOperativaComune), nameof(ProfiloAzienda.LegaleNome), nameof(ProfiloAzienda.LegaleCognome),
                     nameof(ProfiloAzienda.ReferenteNome) })
            b.Property(p).HasMaxLength(150);
        foreach (var p in new[] { nameof(ProfiloAzienda.SedeLegaleVia), nameof(ProfiloAzienda.SedeOperativaVia) })
            b.Property(p).HasMaxLength(300);
        foreach (var p in new[] { nameof(ProfiloAzienda.Pec), nameof(ProfiloAzienda.LegaleEmail), nameof(ProfiloAzienda.ReferenteEmail),
                     nameof(ProfiloAzienda.CompletatoDa) })
            b.Property(p).HasMaxLength(256);
        foreach (var p in new[] { nameof(ProfiloAzienda.NumeroRea), nameof(ProfiloAzienda.CodiceAteco), nameof(ProfiloAzienda.Telefono),
                     nameof(ProfiloAzienda.LegaleTelefono), nameof(ProfiloAzienda.ReferenteTelefono), nameof(ProfiloAzienda.MatricolaInps),
                     nameof(ProfiloAzienda.InailCodiceDitta), nameof(ProfiloAzienda.InailPat) })
            b.Property(p).HasMaxLength(30);
        b.Property(x => x.SedeLegaleCap).HasMaxLength(5);
        b.Property(x => x.SedeOperativaCap).HasMaxLength(5);
        b.Property(x => x.SedeLegaleProvincia).HasMaxLength(2);
        b.Property(x => x.SedeOperativaProvincia).HasMaxLength(2);
        b.Property(x => x.NoteStudio).HasMaxLength(2000);
        b.Property(x => x.OreSettimanali).HasPrecision(5, 2);
    }
}

internal sealed class ComunicazioneMensileConfig : IEntityTypeConfiguration<ComunicazioneMensile>
{
    public void Configure(EntityTypeBuilder<ComunicazioneMensile> b)
    {
        b.ToTable("ComunicazioniPresenze");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Note).HasMaxLength(2000);
        b.Property(x => x.InviataDa).HasMaxLength(256);
        b.Property(x => x.ElaborataDa).HasMaxLength(256);
        b.Ignore(x => x.PrimoGiorno);
        b.Ignore(x => x.UltimoGiorno);
        b.HasOne<Azienda>().WithMany().HasForeignKey(x => x.AziendaId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AziendaId, x.Anno, x.Mese }).IsUnique();
        b.HasIndex(x => new { x.Anno, x.Mese, x.Stato });
        b.HasMany(x => x.Righe).WithOne().HasForeignKey(r => r.ComunicazioneId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Righe).HasField("_righe").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

internal sealed class RigaPresenzeConfig : IEntityTypeConfiguration<RigaPresenze>
{
    public void Configure(EntityTypeBuilder<RigaPresenze> b)
    {
        b.ToTable("RighePresenze");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Note).HasMaxLength(500);
        b.Ignore(x => x.Voci);
        b.Ignore(x => x.Assenze);
        b.HasOne<Dipendente>().WithMany().HasForeignKey(x => x.DipendenteId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ComunicazioneId, x.DipendenteId }).IsUnique();
    }
}

internal sealed class ProfiloDipendenteConfig : IEntityTypeConfiguration<ProfiloDipendente>
{
    public void Configure(EntityTypeBuilder<ProfiloDipendente> b)
    {
        b.ToTable("ProfiliDipendente");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasOne<Dipendente>().WithOne().HasForeignKey<ProfiloDipendente>(x => x.DipendenteId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.DipendenteId).IsUnique();
        b.Ignore(x => x.Residenza);
        b.Ignore(x => x.Domicilio);
        b.Ignore(x => x.Completato);
        foreach (var p in new[] { nameof(ProfiloDipendente.ComuneNascita), nameof(ProfiloDipendente.Cittadinanza), nameof(ProfiloDipendente.ResidenzaComune),
                     nameof(ProfiloDipendente.DomicilioComune), nameof(ProfiloDipendente.IntestatarioConto), nameof(ProfiloDipendente.FondoPensione),
                     nameof(ProfiloDipendente.TipoDocumento) })
            b.Property(p).HasMaxLength(150);
        foreach (var p in new[] { nameof(ProfiloDipendente.ResidenzaVia), nameof(ProfiloDipendente.DomicilioVia) })
            b.Property(p).HasMaxLength(300);
        foreach (var p in new[] { nameof(ProfiloDipendente.EmailPersonale), nameof(ProfiloDipendente.AggiornatoDa) })
            b.Property(p).HasMaxLength(256);
        foreach (var p in new[] { nameof(ProfiloDipendente.Telefono), nameof(ProfiloDipendente.NumeroDocumento) })
            b.Property(p).HasMaxLength(30);
        foreach (var p in new[] { nameof(ProfiloDipendente.ProvinciaNascita), nameof(ProfiloDipendente.ResidenzaProvincia), nameof(ProfiloDipendente.DomicilioProvincia) })
            b.Property(p).HasMaxLength(2);
        b.Property(x => x.ResidenzaCap).HasMaxLength(5);
        b.Property(x => x.DomicilioCap).HasMaxLength(5);
        b.HasMany(x => x.Familiari).WithOne().HasForeignKey(f => f.ProfiloId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Familiari).HasField("_familiari").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

internal sealed class FamiliareACaricoConfig : IEntityTypeConfiguration<FamiliareACarico>
{
    public void Configure(EntityTypeBuilder<FamiliareACarico> b)
    {
        b.ToTable("FamiliariACarico");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Nome).HasMaxLength(100);
        b.Property(x => x.Cognome).HasMaxLength(100);
        b.Ignore(x => x.Descrizione);
    }
}

internal sealed class DipendenteConfig : IEntityTypeConfiguration<Dipendente>
{
    public void Configure(EntityTypeBuilder<Dipendente> b)
    {
        b.ToTable("Dipendenti");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Nome).HasMaxLength(100);
        b.Property(x => x.Cognome).HasMaxLength(100);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Telefono).HasMaxLength(30);
        b.Property(x => x.Indirizzo).HasMaxLength(300);
        b.Property(x => x.Matricola).HasMaxLength(30);
        b.Property(x => x.UserId).HasMaxLength(450);
        b.Ignore(x => x.NomeCompleto);

        b.HasOne<Azienda>().WithMany().HasForeignKey(x => x.AziendaId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.AziendaId, x.CodiceFiscale }).IsUnique();
        b.HasIndex(x => new { x.AziendaId, x.Cognome, x.Nome });

        b.HasMany(x => x.Contratti).WithOne().HasForeignKey(c => c.DipendenteId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Contratti).HasField("_contratti").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

internal sealed class ContrattoConfig : IEntityTypeConfiguration<Contratto>
{
    public void Configure(EntityTypeBuilder<Contratto> b)
    {
        b.ToTable("Contratti");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Ccnl).HasMaxLength(100);
        b.Property(x => x.Livello).HasMaxLength(20);
        b.Property(x => x.Mansione).HasMaxLength(150);
        b.Property(x => x.OreSettimanaliFullTime).HasPrecision(5, 2);
        b.Property(x => x.PercentualePartTime).HasPrecision(5, 2);
        b.Property(x => x.OreFerieAnnue).HasPrecision(7, 2);
        b.Property(x => x.OreRolAnnue).HasPrecision(7, 2);
        b.Property(x => x.OreExFestivitaAnnue).HasPrecision(7, 2);
        b.Ignore(x => x.OreSettimanali);
        b.Ignore(x => x.OreGiornaliere);
        b.Ignore(x => x.RetribuzioneMensileLorda);
    }
}

internal sealed class RichiestaAssenzaConfig : IEntityTypeConfiguration<RichiestaAssenza>
{
    public void Configure(EntityTypeBuilder<RichiestaAssenza> b)
    {
        b.ToTable("RichiesteAssenza");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Ore).HasPrecision(7, 2);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.ProtocolloCertificato).HasMaxLength(50);
        b.Property(x => x.DecisaDa).HasMaxLength(256);
        b.Property(x => x.MotivoRifiuto).HasMaxLength(1000);
        b.Ignore(x => x.Attiva);

        b.HasOne<Dipendente>().WithMany().HasForeignKey(x => x.DipendenteId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.DipendenteId, x.Dal, x.Al });
        b.HasIndex(x => x.Stato);
    }
}

internal sealed class SaldoInizialeConfig : IEntityTypeConfiguration<SaldoIniziale>
{
    public void Configure(EntityTypeBuilder<SaldoIniziale> b)
    {
        b.ToTable("SaldiIniziali");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Ore).HasPrecision(7, 2);
        b.HasOne<Dipendente>().WithMany().HasForeignKey(x => x.DipendenteId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.DipendenteId, x.Anno, x.Tipo }).IsUnique();
    }
}

internal sealed class BustaPagaConfig : IEntityTypeConfiguration<BustaPaga>
{
    public void Configure(EntityTypeBuilder<BustaPaga> b)
    {
        b.ToTable("BustePaga");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.NomeFile).HasMaxLength(255);
        b.Property(x => x.ChiaveArchivio).HasMaxLength(500);
        b.Property(x => x.HashSha256).HasMaxLength(64);
        b.Ignore(x => x.Periodo);

        // Cancellare un dipendente non deve far sparire i cedolini (obbligo di conservazione).
        b.HasOne<Dipendente>().WithMany().HasForeignKey(x => x.DipendenteId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Azienda>().WithMany().HasForeignKey(x => x.AziendaId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.DipendenteId, x.Anno, x.Mese });
        b.HasIndex(x => new { x.AziendaId, x.Anno, x.Mese });
        b.HasIndex(x => x.HashSha256).IsUnique();
    }
}

internal sealed class ApplicationUserConfig : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.Property(x => x.Nome).HasMaxLength(100);
        b.Property(x => x.Cognome).HasMaxLength(100);
        b.HasOne<Azienda>().WithMany().HasForeignKey(x => x.AziendaId).OnDelete(DeleteBehavior.Restrict);
        // DipendenteId senza FK: evita il ciclo con Dipendenti.UserId; la coerenza è garantita dal servizio di provisioning.
        b.HasIndex(x => x.DipendenteId).IsUnique();
    }
}
