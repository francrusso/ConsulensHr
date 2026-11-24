using CL.Domain.Common;

namespace CL.Domain.Presenze;

public sealed class TurnoLavoro : Entity
{
    public DateTime? PubblicatoIl { get; private set; }
    public int Versione { get; private set; }
    public Guid? TeamVincolatoId { get; private set; }
    public void Pubblica(DateTime adesso) => PubblicatoIl = adesso;
    public string? VincoliTeam { get; private set; }
    public IReadOnlyList<Guid> TeamConsentiti => !string.IsNullOrWhiteSpace(VincoliTeam) ? VincoliTeam.Split(',').Select(Guid.Parse).ToList() : TeamVincolatoId is { } id ? [id] : [];
    public void VincolaTeam(Guid? teamId) => VincolaTeamMultipli(teamId is { } id ? [id] : []);
    public void VincolaTeamMultipli(IEnumerable<Guid> teamIds)
    {
        var ids = teamIds.Distinct().ToList();
        TeamVincolatoId = ids.Count == 1 ? ids[0] : null;
        VincoliTeam = ids.Count == 0 ? null : string.Join(",", ids);
    }
    public void Riassegna(Guid dipendenteId)
    {
        if (dipendenteId == Guid.Empty) throw new DomainException("Dipendente obbligatorio.");
        DipendenteId = dipendenteId; Versione++;
    }
    public Guid DipendenteId { get; private set; }
    public DateTime Inizio { get; private set; }
    public DateTime Fine { get; private set; }
    public TipoGiornataTurno Tipo { get; private set; } = TipoGiornataTurno.Lavoro;
    public double OreLavorate => Tipo == TipoGiornataTurno.Lavoro ? (Fine - Inizio).TotalHours : 0;
    public string? Nome { get; private set; }
    public string? Note { get; private set; }
    private TurnoLavoro() { }

    public TurnoLavoro(Guid dipendenteId, DateTime inizio, DateTime fine, string? note, string? nome = null)
    {
        if (dipendenteId == Guid.Empty) throw new DomainException("Dipendente obbligatorio.");
        DipendenteId = dipendenteId;
        Nome = nome;
        Modifica(inizio, fine, note);
    }

    public void Modifica(DateTime inizio, DateTime fine, string? note)
    {
        if (fine <= inizio || fine - inizio > TimeSpan.FromHours(24))
            throw new DomainException("La fine deve seguire l'inizio e il turno non può superare 24 ore.");
        if (note?.Length > 500) throw new DomainException("Le note non possono superare 500 caratteri.");
        // Orari locali di lavoro, senza conversioni di fuso orario.
        Inizio = DateTime.SpecifyKind(inizio, DateTimeKind.Unspecified);
        Fine = DateTime.SpecifyKind(fine, DateTimeKind.Unspecified);
        Versione++; PubblicatoIl = null;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public static TurnoLavoro NonLavorato(Guid dipendenteId, DateOnly giorno, TipoGiornataTurno tipo, string nome, string? note)
    {
        if (tipo is not (TipoGiornataTurno.Smonto or TipoGiornataTurno.Riposo)) throw new DomainException("Tipo di giornata non valido.");
        return new TurnoLavoro(dipendenteId, giorno.ToDateTime(TimeOnly.MinValue), giorno.AddDays(1).ToDateTime(TimeOnly.MinValue), note, nome) { Tipo = tipo };
    }
    public bool Sovrapposto(TurnoLavoro altro)
    {
        if (DipendenteId != altro.DipendenteId) return false;
        if (Tipo != TipoGiornataTurno.Lavoro || altro.Tipo != TipoGiornataTurno.Lavoro)
            return Inizio.Date == altro.Inizio.Date;
        return Inizio < altro.Fine && Fine > altro.Inizio;
    }
}
