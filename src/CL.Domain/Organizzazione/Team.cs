using System.Globalization;
using System.Text.RegularExpressions;
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
    /// <summary>Colore del badge nell'organigramma (#RRGGBB).</summary>
    public string Colore { get; private set; } = ColorePredefinito;
    /// <summary>Emoji del team (facoltativa).</summary>
    public string? Emoji { get; private set; }

    public const string ColorePredefinito = "#A47FE6";

    /// <summary>Colori proposti (gli stessi toni pastello dei grafici di Consulens).</summary>
    public static readonly IReadOnlyList<(string Nome, string Hex)> Palette =
    [
        ("Viola", "#A47FE6"), ("Verde", "#8CC98A"), ("Arancio", "#F4A582"), ("Blu", "#6096FF"), ("Giallo", "#F2C14E"),
        ("Rosa", "#EC8FB5"), ("Turchese", "#4FBFC4"), ("Bordeaux", "#98494E"), ("Grigio", "#9AA3AE"),
    ];

    private Team() { } // EF Core

    public Team(Guid aziendaId, string nome)
    {
        if (aziendaId == Guid.Empty) throw new DomainException("Azienda obbligatoria.");
        AziendaId = aziendaId;
        Rinomina(nome);
    }

    /// <summary>Colore #RRGGBB ed emoji (un solo simbolo, niente lettere o codice).</summary>
    public void ImpostaAspetto(string? colore, string? emoji)
    {
        var c = string.IsNullOrWhiteSpace(colore) ? ColorePredefinito : colore.Trim();
        if (!Regex.IsMatch(c, "^#[0-9A-Fa-f]{6}$")) throw new DomainException("Colore non valido: usa il formato #RRGGBB.");
        var e = string.IsNullOrWhiteSpace(emoji) ? null : emoji.Trim();
        if (e is not null)
        {
            var simboli = new StringInfo(e).LengthInTextElements;
            if (simboli > 2 || e.Length > 16 || e.Any(ch => char.IsAsciiLetterOrDigit(ch) || ch is '<' or '>' or '&' or '"' or '\''))
                throw new DomainException("Scegli una sola emoji per il team.");
        }
        Colore = c.ToUpperInvariant();
        Emoji = e;
    }

    /// <summary>
    /// Colore del testo sul badge del team: bianco sui toni medi e scuri (blu, viola, grigio, bordeaux…),
    /// scuro solo sui toni molto chiari (giallo, verdi e aranci pastello) dove il bianco non si leggerebbe.
    /// </summary>
    public static string TestoSu(string hex)
    {
        if (!Regex.IsMatch(hex ?? "", "^#[0-9A-Fa-f]{6}$")) return "#ffffff";
        double Lin(int i)
        {
            var v = int.Parse(hex.AsSpan(i, 2), NumberStyles.HexNumber) / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        var l = 0.2126 * Lin(1) + 0.7152 * Lin(3) + 0.0722 * Lin(5);
        return 1.05 / (l + 0.05) >= 2.4 ? "#ffffff" : "#3a2830"; // badge in grassetto con ombra: il bianco resta leggibile
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
/// 2) altrimenti il responsabile del suo team; se il team non ha un responsabile decide il referente dell'azienda;
/// 3) se il responsabile del team è il dipendente stesso, si sale al team superiore con la stessa regola;
/// 4) senza team, o in cima alla gerarchia: decide il referente dell'azienda (null).
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
            if (t.ResponsabileId is not { } r) return null; // team senza responsabile: referente dell'azienda
            if (r != d.Id) return r;
            // È lui il responsabile del team: decide il responsabile del team superiore.
        }
        return null;
    }

    /// <summary>Approvatore di ciascun dipendente (null = referente dell'azienda).</summary>
    public static Dictionary<Guid, Guid?> Approvatori(IEnumerable<NodoDipendente> dipendenti, IReadOnlyCollection<Team> team) =>
        dipendenti.ToDictionary(d => d.Id, d => Approvatore(d, team));
}
