using CL.Domain.Common;
using CL.Domain.Dipendenti;

namespace CL.Domain.Organizzazione;

/// <summary>
/// Team (reparto, ufficio, squadra) di un'azienda. I team formano un albero tramite <see cref="PadreId"/>;
/// il responsabile del team approva ferie e permessi dei suoi componenti.
/// </summary>
public sealed class Team : Entity
{
    public Guid AziendaId { get; private set; }
    public string Nome { get; private set; } = null!;
    public Guid? PadreId { get; private set; }
    /// <summary>Dipendente responsabile del team.</summary>
    public Guid? ResponsabileId { get; private set; }

    private Team() { } // EF Core

    public Team(Guid aziendaId, string nome)
    {
        if (aziendaId == Guid.Empty) throw new DomainException("Azienda obbligatoria.");
        AziendaId = aziendaId;
        Rinomina(nome);
    }

    public void Rinomina(string nome)
    {
        var n = Guard.NotEmpty(nome, "Nome del team");
        if (n.Length > 100) throw new DomainException("Nome del team troppo lungo (massimo 100 caratteri).");
        Nome = n;
    }

    /// <summary>Sposta il team sotto un altro team della stessa azienda, senza creare cicli.</summary>
    public void ImpostaPadre(Team? padre, IReadOnlyCollection<Team> teamAzienda)
    {
        if (padre is null) { PadreId = null; return; }
        if (padre.AziendaId != AziendaId) throw new DomainException("Il team superiore deve essere della stessa azienda.");
        var visti = new HashSet<Guid>();
        for (var t = padre; t is not null; t = t.PadreId is { } p ? teamAzienda.FirstOrDefault(x => x.Id == p) : null)
        {
            if (t.Id == Id) throw new DomainException("Un team non può stare sotto sé stesso o sotto un suo sotto-team.");
            if (!visti.Add(t.Id)) break;
        }
        PadreId = padre.Id;
    }

    public void ImpostaResponsabile(Dipendente? responsabile)
    {
        if (responsabile is not null && responsabile.AziendaId != AziendaId)
            throw new DomainException("Il responsabile deve essere un dipendente della stessa azienda.");
        ResponsabileId = responsabile?.Id;
    }
}

/// <summary>Dati organizzativi minimi di un dipendente, per calcolare chi approva.</summary>
public sealed record NodoDipendente(Guid Id, Guid? TeamId, Guid? ResponsabileId);

/// <summary>
/// Chi approva ferie e permessi di un dipendente:
/// 1) il responsabile diretto, se indicato;
/// 2) altrimenti il responsabile del suo team, risalendo ai team superiori quando il responsabile è lui stesso o manca;
/// 3) altrimenti nessuno: decide il referente dell'azienda.
/// </summary>
public static class Organigramma
{
    public static Guid? Approvatore(NodoDipendente d, IReadOnlyCollection<Team> team)
    {
        if (d.ResponsabileId is { } diretto && diretto != d.Id) return diretto;
        var visti = new HashSet<Guid>();
        for (var t = d.TeamId is { } id ? team.FirstOrDefault(x => x.Id == id) : null;
             t is not null && visti.Add(t.Id);
             t = t.PadreId is { } p ? team.FirstOrDefault(x => x.Id == p) : null)
        {
            if (t.ResponsabileId is { } r && r != d.Id) return r;
        }
        return null;
    }

    /// <summary>Approvatore di ciascun dipendente (null = referente dell'azienda).</summary>
    public static Dictionary<Guid, Guid?> Approvatori(IEnumerable<NodoDipendente> dipendenti, IReadOnlyCollection<Team> team) =>
        dipendenti.ToDictionary(d => d.Id, d => Approvatore(d, team));
}
