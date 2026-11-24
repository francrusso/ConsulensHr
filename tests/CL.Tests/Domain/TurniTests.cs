using CL.Domain.Common;
using CL.Domain.Presenze;

namespace CL.Tests.Domain;

public sealed class TurniTests
{
    [Fact]
    public void Turno_notturno_e_confini_adiacenti()
    {
        var id = Guid.NewGuid();
        var inizio = new DateTime(2026, 11, 24, 22, 0, 0);
        var turno = new TurnoLavoro(id, inizio, inizio.AddHours(8), " Notte ");
        Assert.Equal("Notte", turno.Note);
        Assert.False(turno.Sovrapposto(new TurnoLavoro(id, turno.Fine, turno.Fine.AddHours(8), null)));
        Assert.True(turno.Sovrapposto(new TurnoLavoro(id, inizio.AddHours(2), inizio.AddHours(3), null)));
        Assert.False(turno.Sovrapposto(new TurnoLavoro(Guid.NewGuid(), inizio, turno.Fine, null)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(25)]
    public void Rifiuta_durate_non_valide(int ore)
    {
        var inizio = new DateTime(2026, 11, 24, 8, 0, 0);
        Assert.Throws<DomainException>(() => new TurnoLavoro(Guid.NewGuid(), inizio, inizio.AddHours(ore), null));
    }

    [Fact]
    public void Rotazione_settimanale_riparte_e_rispetta_i_giorni_scelti()
    {
        var azienda = Guid.NewGuid();
        var modelli = new[] {
            new TurnoSalvato(azienda, "Mattina", new TimeOnly(6, 0), new TimeOnly(14, 0)),
            new TurnoSalvato(azienda, "Pomeriggio", new TimeOnly(14, 0), new TimeOnly(22, 0)),
            new TurnoSalvato(azienda, "Notte", new TimeOnly(22, 0), new TimeOnly(6, 0)) };
        var turni = PianificazioneTurni.Crea(Guid.NewGuid(), new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 24), [DayOfWeek.Tuesday], modelli, null);
        Assert.Equal(new[] { "Mattina", "Pomeriggio", "Notte", "Mattina" }, turni.Select(t => t.Nome));
        Assert.All(turni, t => Assert.Equal(DayOfWeek.Tuesday, t.Inizio.DayOfWeek));
        Assert.Equal(new DateTime(2026, 11, 18, 6, 0, 0), turni[2].Fine);
        Assert.Throws<DomainException>(() => PianificazioneTurni.Crea(Guid.NewGuid(), new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 24), [], modelli, null));
    }

    [Fact]
    public void Ciclo_giornaliero_con_smonto_riposo_e_team()
    {
        var team = Guid.NewGuid(); var dip = Guid.NewGuid();
        var modello = new TurnoSalvato(Guid.NewGuid(), "Rotazione", new TimeOnly(6, 0), new TimeOnly(14, 0));
        modello.Configura(127, team, [
            new FaseTurno("Mattina", TipoGiornataTurno.Lavoro, new TimeOnly(6, 0), new TimeOnly(14, 0)),
            new FaseTurno("Pomeriggio", TipoGiornataTurno.Lavoro, new TimeOnly(14, 0), new TimeOnly(22, 0)),
            new FaseTurno("Notte", TipoGiornataTurno.Lavoro, new TimeOnly(22, 0), new TimeOnly(6, 0)),
            new FaseTurno("Smonto", TipoGiornataTurno.Smonto, null, null),
            new FaseTurno("Riposo", TipoGiornataTurno.Riposo, null, null)]);
        var turni = modello.Pianifica(dip, team, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10), null);
        Assert.Equal(10, turni.Count);
        Assert.Equal(new[] { "Mattina", "Pomeriggio", "Notte", "Smonto", "Riposo", "Mattina", "Pomeriggio", "Notte", "Smonto", "Riposo" }, turni.Select(t => t.Nome));
        Assert.Equal(48, turni.Sum(t => t.OreLavorate));
        Assert.False(turni[2].Sovrapposto(turni[3]));
        Assert.True(turni[3].Sovrapposto(new TurnoLavoro(dip, new DateTime(2026, 11, 4, 8, 0, 0), new DateTime(2026, 11, 4, 16, 0, 0), null)));
        Assert.Throws<DomainException>(() => modello.Pianifica(dip, null, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10), null));
        modello.Configura(1 << (int)DayOfWeek.Monday, null, []);
        var lunedi = modello.Pianifica(dip, null, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10), null);
        Assert.Equal(2, lunedi.Count);
        Assert.All(lunedi, t => Assert.Equal(DayOfWeek.Monday, t.Inizio.DayOfWeek));
    }
}
