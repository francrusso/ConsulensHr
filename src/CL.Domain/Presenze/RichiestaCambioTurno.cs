using CL.Domain.Assenze;
using CL.Domain.Common;

namespace CL.Domain.Presenze;

public sealed class RichiestaCambioTurno : Entity
{
    public Guid DipendenteId { get; private set; }
    public Guid TurnoId { get; private set; }
    public Guid? TurnoCollegaId { get; private set; }
    public Guid? CollegaId { get; private set; }
    public bool AccordoCollega { get; private set; }
    public int VersioneTurno { get; private set; }
    public int? VersioneCollega { get; private set; }
    public DateTime InizioOriginale { get; private set; }
    public DateTime FineOriginale { get; private set; }
    public DateTime NuovoInizio { get; private set; }
    public DateTime NuovaFine { get; private set; }
    public string Note { get; private set; } = null!;
    public StatoRichiesta Stato { get; private set; } = StatoRichiesta.InAttesa;
    public DateTime RichiestoIl { get; private set; }
    public string? DecisoDa { get; private set; }
    public string? MotivoRifiuto { get; private set; }
    private RichiestaCambioTurno() { }
    public RichiestaCambioTurno(TurnoLavoro turno, TurnoLavoro? collega, DateTime nuovoInizio, DateTime nuovaFine, bool accordo, string note, DateTime adesso)
    {
        if (turno.PubblicatoIl is null || turno.Tipo != TipoGiornataTurno.Lavoro) throw new DomainException("Seleziona un turno di lavoro pubblicato.");
        if (string.IsNullOrWhiteSpace(note) || note.Length > 1000) throw new DomainException("Indica il motivo della richiesta (massimo 1000 caratteri).");
        if (collega is not null && (!accordo || collega.DipendenteId == turno.DipendenteId || collega.PubblicatoIl is null || collega.Tipo != TipoGiornataTurno.Lavoro))
            throw new DomainException("Per lo scambio scegli un collega diverso e conferma l'accordo raggiunto.");
        var proposta = new TurnoLavoro(turno.DipendenteId, collega?.Inizio ?? nuovoInizio, collega?.Fine ?? nuovaFine, null);
        DipendenteId = turno.DipendenteId; TurnoId = turno.Id; VersioneTurno = turno.Versione;
        CollegaId = collega?.DipendenteId; TurnoCollegaId = collega?.Id; VersioneCollega = collega?.Versione;
        AccordoCollega = collega is not null && accordo;
        InizioOriginale = turno.Inizio; FineOriginale = turno.Fine;
        NuovoInizio = proposta.Inizio; NuovaFine = proposta.Fine; Note = note.Trim(); RichiestoIl = adesso;
    }
    public void Decidi(bool approva, string chi, string? motivo)
    {
        if (Stato != StatoRichiesta.InAttesa) throw new DomainException("La richiesta è già stata decisa.");
        if (string.IsNullOrWhiteSpace(chi)) throw new DomainException("Approvatore obbligatorio.");
        if (motivo?.Length > 1000) throw new DomainException("Motivo troppo lungo.");
        Stato = approva ? StatoRichiesta.Approvata : StatoRichiesta.Rifiutata;
        DecisoDa = chi; MotivoRifiuto = approva ? null : motivo;
    }
    public void Ritira()
    {
        if (Stato != StatoRichiesta.InAttesa) throw new DomainException("Si possono ritirare solo le richieste in attesa.");
        Stato = StatoRichiesta.Annullata;
    }
}
