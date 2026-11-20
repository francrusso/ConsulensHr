using CL.Domain.Common;
using CL.Domain.Dipendenti;
using CL.Domain.Organizzazione;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

public sealed record MembroTeam(Guid Id, string Nome, string? Posizione, Guid? TeamId, Guid? ResponsabileDirettoId, Guid? ApprovatoreId, bool InForza,
    bool HaFoto = false, string Segnaposto = "neutro");

/// <summary>Nodo del grafico: l'azienda in cima, i team come badge colorati, le persone come card.</summary>
public sealed record NodoGrafico(string Tipo, string Titolo, string? Sottotitolo, string Colore, string? Emoji, int? Conteggio,
    Guid? DipendenteId, bool Riferimento, List<NodoGrafico> Figli, bool HaFoto = false, string Segnaposto = "neutro")
{
    public string Iniziali => string.Concat(Titolo.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));
}

public sealed class OrganigrammaViewModel
{
    public required Guid AziendaId { get; init; }
    public required string Azienda { get; init; }
    public bool HaLogo { get; init; }
    public required List<Team> Team { get; init; }
    public required List<MembroTeam> Dipendenti { get; init; }
    public bool PerConto { get; init; }

    public string NomeDi(Guid? dipendenteId) => dipendenteId is { } id ? Dipendenti.FirstOrDefault(d => d.Id == id)?.Nome ?? "—" : "—";

    /// <summary>
    /// Albero del grafico: azienda → team di primo livello → (responsabile del team →) componenti, con i sotto-team
    /// sotto il responsabile e chi ha un responsabile diretto nello stesso team sotto di lui.
    /// </summary>
    public NodoGrafico Grafico()
    {
        var approvati = Dipendenti.Where(d => d.ApprovatoreId is not null).GroupBy(d => d.ApprovatoreId!.Value).ToDictionary(g => g.Key, g => g.Count());
        var visti = new HashSet<Guid>();

        NodoGrafico Persona(MembroTeam m, Team? team, bool riferimento, List<MembroTeam> membri)
        {
            var figli = new List<NodoGrafico>();
            if (visti.Add(m.Id) || riferimento)
                figli.AddRange(membri.Where(x => x.ResponsabileDirettoId == m.Id && x.Id != m.Id && !visti.Contains(x.Id))
                    .Select(x => Persona(x, team, false, membri)));
            return new NodoGrafico("persona", m.Nome, m.Posizione, team?.Colore ?? "#9AA3AE", null,
                approvati.GetValueOrDefault(m.Id) is var n and > 0 ? n : null, m.Id, riferimento, figli, m.HaFoto, m.Segnaposto);
        }

        NodoGrafico TeamNodo(Team t)
        {
            var membri = Dipendenti.Where(d => d.TeamId == t.Id).ToList();
            var resp = t.ResponsabileId is { } rid ? Dipendenti.FirstOrDefault(d => d.Id == rid) : null;
            // In cima al team chi non ha un responsabile diretto dentro il team stesso.
            var inCima = membri.Where(m => m.Id != resp?.Id && (m.ResponsabileDirettoId is null || m.ResponsabileDirettoId == resp?.Id
                                                                  || membri.All(x => x.Id != m.ResponsabileDirettoId))).ToList();
            var sotto = new List<NodoGrafico>();
            sotto.AddRange(inCima.Select(m => Persona(m, t, false, membri)));
            sotto.AddRange(Team.Where(x => x.PadreId == t.Id).OrderBy(x => x.Nome).Select(TeamNodo));
            var figli = resp is null ? sotto
                : [new NodoGrafico("persona", resp.Nome, resp.Posizione ?? "Responsabile", t.Colore, null,
                    approvati.GetValueOrDefault(resp.Id) is var n and > 0 ? n : null, resp.Id, resp.TeamId != t.Id, sotto, resp.HaFoto, resp.Segnaposto)];
            return new NodoGrafico("team", t.Nome, null, t.Colore, t.Emoji, membri.Count, null, false, figli);
        }

        var radice = Team.Where(t => t.PadreId is null || Team.All(x => x.Id != t.PadreId)).OrderBy(t => t.Nome).Select(TeamNodo).ToList();
        var senza = Dipendenti.Where(d => d.TeamId is null).ToList();
        if (senza.Count > 0)
            radice.Add(new NodoGrafico("team", "Senza team", null, "#9AA3AE", null, senza.Count, null, false,
                senza.Where(m => m.ResponsabileDirettoId is null || senza.All(x => x.Id != m.ResponsabileDirettoId))
                    .Select(m => Persona(m, null, false, senza)).ToList()));
        return new NodoGrafico("azienda", Azienda, $"{Dipendenti.Count(d => d.InForza)} persone", "#512D38", null, null, null, false, radice);
    }

    /// <summary>Team in ordine di albero (padre, poi figli) con il livello di rientro.</summary>
    public IEnumerable<(Team Team, int Livello)> Albero()
    {
        IEnumerable<(Team, int)> Visita(Guid? padre, int livello, HashSet<Guid> visti) =>
            Team.Where(t => t.PadreId == padre && visti.Add(t.Id))
                .SelectMany(t => new[] { (t, livello) }.Concat(Visita(t.Id, livello + 1, visti)));
        var visti = new HashSet<Guid>();
        var risultato = Visita(null, 0, visti).ToList();
        // Team con padre non trovato (non dovrebbe succedere): in fondo, al primo livello.
        risultato.AddRange(Team.Where(t => !visti.Contains(t.Id)).Select(t => (t, 0)));
        return risultato;
    }
}

/// <summary>
/// Organigramma dell'azienda: team con gerarchia e responsabile, posizione e responsabile diretto dei dipendenti.
/// Lo gestiscono il referente (la propria azienda) e lo studio (per conto di qualsiasi azienda).
/// </summary>
[Authorize(Policy = Policy.GestionePersonale)]
public sealed class TeamController(AppDbContext db, UtenteCorrente utente, ServizioOrganigramma organigramma, TimeProvider clock) : AppController
{
    public async Task<IActionResult> Index(Guid? aziendaId)
    {
        var id = Azienda(aziendaId);
        if (id is null) return RedirectToAction("Index", "Aziende");
        var azienda = await db.Aziende.AsNoTracking().Where(a => a.Id == id).Select(a => new { a.RagioneSociale, a.LogoChiave }).FirstOrDefaultAsync();
        if (azienda is null) return NotFound();

        var oggi = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var team = await organigramma.TeamAsync(id.Value);
        var approvatori = await organigramma.ApprovatoriAsync(id.Value);
        var dipendenti = (await db.Dipendenti.AsNoTracking().Where(d => d.AziendaId == id).OrderBy(d => d.Cognome).ThenBy(d => d.Nome).ToListAsync())
            .Where(d => d.InForzaIl(oggi) || d.TeamId is not null)
            .Select(d => new MembroTeam(d.Id, d.NomeCompleto, d.Posizione, d.TeamId, d.ResponsabileId, approvatori.GetValueOrDefault(d.Id), d.InForzaIl(oggi),
                d.FotoChiave is not null, d.Segnaposto))
            .ToList();

        return View(new OrganigrammaViewModel
        {
            AziendaId = id.Value, Azienda = azienda.RagioneSociale, HaLogo = azienda.LogoChiave is not null, Team = team, Dipendenti = dipendenti,
            PerConto = utente.IsConsulente,
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Salva(Guid aziendaId, Guid? id, string? nome, Guid? padreId, Guid? responsabileId, string? colore, string? emoji)
    {
        var az = Azienda(aziendaId);
        if (az is null || !await db.Aziende.AnyAsync(a => a.Id == az)) return NotFound();
        var tutti = await db.Team.Where(t => t.AziendaId == az).ToListAsync();

        Team? team;
        if (id is { } tid)
        {
            team = tutti.FirstOrDefault(t => t.Id == tid);
            if (team is null) return NotFound();
        }
        else db.Team.Add(team = new Team(az.Value, string.IsNullOrWhiteSpace(nome) ? "?" : nome));

        var responsabile = responsabileId is { } rid ? await db.Dipendenti.FirstOrDefaultAsync(d => d.Id == rid && d.AziendaId == az) : null;
        try
        {
            team.Rinomina(nome ?? "");
            if (tutti.Any(t => t.Id != team.Id && string.Equals(t.Nome, team.Nome, StringComparison.OrdinalIgnoreCase)))
                throw new DomainException($"Esiste già un team \"{team.Nome}\".");
            team.ImpostaPadre(padreId is { } pid ? tutti.FirstOrDefault(t => t.Id == pid) : null, tutti);
            team.ImpostaResponsabile(responsabile);
            // Nuovo team senza colore scelto: il primo colore della tavolozza non ancora usato.
            var predefinito = Team.Palette.Select(p => p.Hex).FirstOrDefault(h => tutti.All(t => !string.Equals(t.Colore, h, StringComparison.OrdinalIgnoreCase)))
                              ?? Team.Palette[tutti.Count % Team.Palette.Count].Hex;
            team.ImpostaAspetto(string.IsNullOrWhiteSpace(colore) && id is null ? predefinito : colore ?? team.Colore, emoji);
        }
        catch (DomainException ex)
        {
            Errore(ex.Message);
            return Torna(az.Value);
        }
        await db.SaveChangesAsync();
        Conferma(id is null ? $"Team \"{team.Nome}\" creato." : "Team aggiornato.");
        return Torna(az.Value);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Elimina(Guid aziendaId, Guid id)
    {
        var az = Azienda(aziendaId);
        var team = await db.Team.FirstOrDefaultAsync(t => t.Id == id && t.AziendaId == az);
        if (team is null) return NotFound();
        if (await db.Team.AnyAsync(t => t.PadreId == id))
            Errore("Il team contiene altri team: spostali o eliminali prima.");
        else if (await db.Dipendenti.AnyAsync(d => d.TeamId == id))
            Errore("Il team ha dei componenti: assegnali a un altro team prima di eliminarlo.");
        else
        {
            db.Team.Remove(team);
            await db.SaveChangesAsync();
            Conferma($"Team \"{team.Nome}\" eliminato.");
        }
        return Torna(team.AziendaId);
    }

    /// <summary>Team, posizione e responsabile diretto di un dipendente.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Assegna(Guid dipendenteId, Guid? teamId, string? posizione, Guid? responsabileId, string? ritorno)
    {
        var dip = await utente.Filtra(db.Dipendenti).FirstOrDefaultAsync(d => d.Id == dipendenteId);
        if (dip is null) return NotFound();
        var team = teamId is { } t ? await db.Team.FirstOrDefaultAsync(x => x.Id == t && x.AziendaId == dip.AziendaId) : null;
        var resp = responsabileId is { } r ? await db.Dipendenti.FirstOrDefaultAsync(x => x.Id == r && x.AziendaId == dip.AziendaId) : null;
        if (Prova(() => dip.ImpostaOrganizzazione(team, posizione, resp)))
        {
            await db.SaveChangesAsync();
            Conferma($"Organizzazione di {dip.NomeCompleto} aggiornata.");
        }
        else Errore(ModelState[""]!.Errors[0].ErrorMessage);
        return Url.IsLocalUrl(ritorno) ? LocalRedirect(ritorno) : Torna(dip.AziendaId);
    }

    private Guid? Azienda(Guid? richiesta) => utente.IsAzienda ? utente.AziendaId : richiesta;

    /// <summary>Dopo una modifica si torna alla scheda "Gestione" della pagina.</summary>
    private IActionResult Torna(Guid aziendaId) =>
        Redirect(Url.Action(nameof(Index), new { aziendaId = utente.IsConsulente ? aziendaId : (Guid?)null }) + "#gestione");
}
