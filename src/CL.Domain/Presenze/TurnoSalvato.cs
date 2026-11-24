using CL.Domain.Common;

namespace CL.Domain.Presenze;

public sealed class TurnoSalvato : Entity
{
    public Guid AziendaId { get; private set; }
    public string Nome { get; private set; } = null!;
    public TimeOnly Inizio { get; private set; }
    public TimeOnly Fine { get; private set; }
    public int GiorniSettimana { get; private set; } = 127;
    public Guid? TeamId { get; private set; }
    private readonly List<TeamAbilitatoTurno> _teamAbilitati = [];
    public IReadOnlyCollection<TeamAbilitatoTurno> TeamAbilitati => _teamAbilitati;
    public IReadOnlyList<Guid> TeamConsentiti => _teamAbilitati.Count > 0 ? _teamAbilitati.Select(t => t.TeamId).ToList() : TeamId is { } id ? [id] : [];
    private readonly List<FaseTurno> _fasi = [];
    public IReadOnlyCollection<FaseTurno> Fasi => _fasi;
    public bool Ciclico => _fasi.Count > 0;
    public bool Previsto(DayOfWeek giorno) => (GiorniSettimana & (1 << (int)giorno)) != 0;

    public void Configura(int giorniSettimana, Guid? teamId, IEnumerable<FaseTurno> fasi)
        => ConfiguraPerTeam(giorniSettimana, teamId is { } id ? [id] : [], fasi);

    public void ConfiguraPerTeam(int giorniSettimana, IEnumerable<Guid> teamIds, IEnumerable<FaseTurno> fasi)
    {
        if (giorniSettimana is < 1 or > 127) throw new DomainException("Seleziona almeno un giorno della settimana.");
        var elenco = fasi.ToList();
        if (elenco.Count > 31) throw new DomainException("Il ciclo può contenere al massimo 31 giornate.");
        if (elenco.Count > 0 && elenco.All(f => f.Tipo != TipoGiornataTurno.Lavoro)) throw new DomainException("Il ciclo deve contenere almeno una giornata lavorata.");
        var ids = teamIds.Distinct().ToList();
        if (ids.Contains(Guid.Empty) || ids.Count > 100) throw new DomainException("Seleziona al massimo 100 team validi.");
        GiorniSettimana = giorniSettimana; TeamId = ids.Count == 1 ? ids[0] : null;
        _teamAbilitati.Clear(); _teamAbilitati.AddRange(ids.Select(id => new TeamAbilitatoTurno(id)));
        _fasi.Clear();
        for (var i = 0; i < elenco.Count; i++) { elenco[i].Ordina(i); _fasi.Add(elenco[i]); }
    }

    public List<TurnoLavoro> Pianifica(Guid dipendenteId, Guid? teamId, DateOnly dal, DateOnly al, string? note)
    {
        if (TeamConsentiti.Count > 0 && (teamId is null || !TeamConsentiti.Contains(teamId.Value))) throw new DomainException("Il turno selezionato è riservato a un altro team.");
        PianificazioneTurni.ValidaPeriodo(dal, al);
        var fasi = _fasi.OrderBy(f => f.Ordine).ToList();
        var risultato = new List<TurnoLavoro>();
        for (var n = 0; n <= al.DayNumber - dal.DayNumber; n++)
        {
            var giorno = dal.AddDays(n);
            if (!Previsto(giorno.DayOfWeek)) continue;
            if (fasi.Count == 0) risultato.Add(Assegna(dipendenteId, giorno, note));
            else
            {
                var fase = fasi[n % fasi.Count];
                if (fase.Tipo == TipoGiornataTurno.Lavoro)
                {
                    var inizio = giorno.ToDateTime(fase.Inizio!.Value);
                    var fine = giorno.ToDateTime(fase.Fine!.Value);
                    if (fine <= inizio) fine = fine.AddDays(1);
                    risultato.Add(new TurnoLavoro(dipendenteId, inizio, fine, note, fase.Nome));
                }
                else risultato.Add(TurnoLavoro.NonLavorato(dipendenteId, giorno, fase.Tipo, fase.Nome, note));
            }
        }
        if (risultato.Count == 0) throw new DomainException("Nessuno dei giorni selezionati ricade nel periodo.");
        foreach (var turno in risultato) turno.VincolaTeamMultipli(TeamConsentiti);
        return risultato;
    }

    private TurnoSalvato() { }
    public TurnoSalvato(Guid aziendaId, string nome, TimeOnly inizio, TimeOnly fine)
    {
        if (aziendaId == Guid.Empty) throw new DomainException("Azienda obbligatoria.");
        AziendaId = aziendaId;
        Modifica(nome, inizio, fine);
    }
    public void Modifica(string nome, TimeOnly inizio, TimeOnly fine)
    {
        if (string.IsNullOrWhiteSpace(nome) || nome.Trim().Length > 80) throw new DomainException("Indica un nome di massimo 80 caratteri.");
        if (inizio == fine) throw new DomainException("Gli orari di inizio e fine devono essere diversi.");
        Nome = nome.Trim(); Inizio = inizio; Fine = fine;
    }
    public TurnoLavoro Assegna(Guid dipendenteId, DateOnly giorno, string? note)
    {
        var inizio = giorno.ToDateTime(Inizio);
        var fine = giorno.ToDateTime(Fine);
        if (Fine < Inizio) fine = fine.AddDays(1);
        var turno = new TurnoLavoro(dipendenteId, inizio, fine, note, Nome);
        turno.VincolaTeamMultipli(TeamConsentiti);
        return turno;
    }
}

public static class PianificazioneTurni
{
    public static void ValidaPeriodo(DateOnly dal, DateOnly al)
    {
        if (dal.Year < 2 || al.Year > 9998 || al < dal || al.DayNumber - dal.DayNumber > 365)
            throw new DomainException("Scegli un periodo di massimo un anno.");
    }
    public static List<TurnoLavoro> Crea(Guid dipendenteId, DateOnly dal, DateOnly al,
        IReadOnlyCollection<DayOfWeek> giorni, IReadOnlyList<TurnoSalvato> rotazione, string? note)
    {
        ValidaPeriodo(dal, al);
        if (giorni.Count == 0 || giorni.Any(g => !Enum.IsDefined(g))) throw new DomainException("Seleziona almeno un giorno della settimana.");
        if (rotazione.Count == 0) throw new DomainException("Seleziona un turno salvato.");
        var lunedi = dal.AddDays(-(((int)dal.DayOfWeek + 6) % 7));
        var risultato = new List<TurnoLavoro>();
        for (var giorno = dal; giorno <= al; giorno = giorno.AddDays(1))
            if (giorni.Contains(giorno.DayOfWeek))
                risultato.Add(rotazione[((giorno.DayNumber - lunedi.DayNumber) / 7) % rotazione.Count].Assegna(dipendenteId, giorno, note));
        if (risultato.Count == 0) throw new DomainException("Nessuno dei giorni selezionati ricade nel periodo.");
        return risultato;
    }
}


public enum TipoGiornataTurno { Lavoro, Smonto, Riposo }

public sealed class FaseTurno : Entity
{
    public int Ordine { get; private set; }
    public string Nome { get; private set; } = null!;
    public TipoGiornataTurno Tipo { get; private set; }
    public TimeOnly? Inizio { get; private set; }
    public TimeOnly? Fine { get; private set; }
    private FaseTurno() { }
    public FaseTurno(string nome, TipoGiornataTurno tipo, TimeOnly? inizio, TimeOnly? fine)
    {
        if (string.IsNullOrWhiteSpace(nome) || nome.Trim().Length > 80) throw new DomainException("Ogni giornata del ciclo deve avere un nome di massimo 80 caratteri.");
        if (!Enum.IsDefined(tipo)) throw new DomainException("Tipo di giornata non valido.");
        if (tipo == TipoGiornataTurno.Lavoro && (inizio is null || fine is null || inizio == fine)) throw new DomainException("Indica orari diversi per ogni giornata lavorata.");
        Nome = nome.Trim(); Tipo = tipo;
        Inizio = tipo == TipoGiornataTurno.Lavoro ? inizio : null;
        Fine = tipo == TipoGiornataTurno.Lavoro ? fine : null;
    }
    internal void Ordina(int ordine) => Ordine = ordine;
}


public sealed class TeamAbilitatoTurno : Entity
{
    public Guid TeamId { get; private set; }
    private TeamAbilitatoTurno() { }
    public TeamAbilitatoTurno(Guid teamId) => TeamId = teamId;
}
