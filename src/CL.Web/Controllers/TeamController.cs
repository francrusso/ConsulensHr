using CL.Domain.Common;
using CL.Domain.Organizzazione;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

/// <summary>Una persona nelle pagine dei team e nel grafico (Remoto = sede in smart working o contratto ibrido/full-remote).</summary>
public sealed record MembroTeam(Guid Id, string Nome, string? Posizione, Guid? TeamId, Guid? ResponsabileDirettoId, Guid? ApprovatoreId, bool InForza,
    bool HaFoto, string Segnaposto, bool Remoto);

/// <summary>Nodo del grafico: l'azienda in cima, i team come badge colorati, le persone come card.</summary>
public sealed record NodoGrafico(string Tipo, string Titolo, string? Sottotitolo, string Colore, string? Emoji, int? Conteggio,
    Guid? DipendenteId, bool Riferimento, List<NodoGrafico> Figli, bool HaFoto = false, string Segnaposto = "neutro", Guid? TeamId = null)
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
    /// <summary>Il consulente lavora per conto dell'azienda: link e percorsi indicano l'azienda.</summary>
    public bool PerConto { get; init; }

    /// <summary>Colore mostrato del team (i sotto-team ereditano quello del team madre, più chiaro).</summary>
    public string ColoreDi(Team t) => Organigramma.ColoreDi(t, Team);

    public string NomeDi(Guid? dipendenteId) => dipendenteId is { } id ? Dipendenti.FirstOrDefault(d => d.Id == id)?.Nome ?? "—" : "—";

    /// <summary>
    /// Albero del grafico: azienda → team di primo livello → (responsabile del team →) componenti, con i sotto-team
    /// sotto il responsabile e chi ha un responsabile diretto nello stesso team sotto di lui.
    /// </summary>
    public NodoGrafico Grafico()
    {
        var approvati = Dipendenti.Where(d => d.ApprovatoreId is not null).GroupBy(d => d.ApprovatoreId!.Value).ToDictionary(g => g.Key, g => g.Count());
        // Numero sulla card: quante persone approva (nessun numero se nessuna).
        int? Approva(Guid id) => approvati.GetValueOrDefault(id) is var n and > 0 ? n : null;
        // Persone già disegnate: evita i cicli tra responsabili diretti e i doppioni.
        var visti = new HashSet<Guid>();

        NodoGrafico Persona(MembroTeam m, Team? team, bool riferimento, List<MembroTeam> membri)
        {
            var figli = new List<NodoGrafico>();
            if (visti.Add(m.Id) || riferimento)
                figli.AddRange(membri.Where(x => x.ResponsabileDirettoId == m.Id && x.Id != m.Id && !visti.Contains(x.Id))
                    .Select(x => Persona(x, team, false, membri)));
            return new NodoGrafico("persona", m.Nome, m.Posizione, team is null ? "#9AA3AE" : ColoreDi(team), null,
                Approva(m.Id), m.Id, riferimento, figli, m.HaFoto, m.Segnaposto);
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
            // Il responsabile fa da capo al team; se appartiene a un altro team la sua card è segnata come "riferimento".
            var figli = resp is null ? sotto
                : [new NodoGrafico("persona", resp.Nome, resp.Posizione ?? "Responsabile", ColoreDi(t), null,
                    Approva(resp.Id), resp.Id, resp.TeamId != t.Id, sotto, resp.HaFoto, resp.Segnaposto)];
            return new NodoGrafico("team", t.Nome, null, ColoreDi(t), t.Emoji, membri.Count, null, false, figli, TeamId: t.Id);
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
/// Lo gestiscono il referente (la propria azienda) e il consulente (per conto delle aziende del suo perimetro).
/// Chi approva ferie e permessi di ciascuno si ricava da qui (vedi <see cref="ServizioOrganigramma"/>).
/// </summary>
[Authorize(Policy = Policy.GestionePersonale)]
public sealed class TeamController(AppDbContext db, UtenteCorrente utente, ServizioOrganigramma organigramma, TimeProvider clock) : AppController
{
    /// <summary>Elenco dei team dell'azienda, con la creazione di un nuovo team.</summary>
    public Task<IActionResult> Index(Guid? aziendaId, string? q)
    {
        ViewBag.Ricerca = q?.Trim();
        return Pagina(aziendaId, "Index");
    }

    /// <summary>Un team: dati (modificabili) e membri, da aggiungere o togliere.</summary>
    public async Task<IActionResult> Dettaglio(Guid id, bool modifica = false)
    {
        var aziendaTeam = await db.Team.AsNoTracking().Where(t => t.Id == id).Select(t => (Guid?)t.AziendaId).FirstOrDefaultAsync();
        if (aziendaTeam is not { } az || (utente.IsAzienda && az != utente.AziendaId)) return NotFound();
        ViewBag.TeamId = id;
        ViewBag.Modifica = modifica;
        return await Pagina(az, "Dettaglio");
    }

    /// <summary>Aggiunge una persona al team o la toglie (posizione e responsabile diretto restano).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Membro(Guid teamId, Guid dipendenteId, bool togli = false)
    {
        var team = await db.Team.FirstOrDefaultAsync(t => t.Id == teamId);
        var dip = await utente.Filtra(db.Dipendenti).FirstOrDefaultAsync(d => d.Id == dipendenteId);
        if (team is null || dip is null || dip.AziendaId != team.AziendaId) return NotFound();
        var resp = dip.ResponsabileId is { } r ? await db.Dipendenti.FirstOrDefaultAsync(x => x.Id == r) : null;
        if (ProvaOAvvisa(() => dip.ImpostaOrganizzazione(togli ? null : team, dip.Posizione, resp)))
        {
            await db.SaveChangesAsync();
            Conferma(togli ? $"{dip.NomeCompleto} tolto dal team {team.Nome}." : $"{dip.NomeCompleto} aggiunto al team {team.Nome}.");
        }
        return RedirectToAction(nameof(Dettaglio), new { id = teamId });
    }

    /// <summary>Albero grafico dell'azienda.</summary>
    public Task<IActionResult> Organigramma(Guid? aziendaId) => Pagina(aziendaId, "Organigramma");

    /// <summary>Le tre pagine condividono lo stesso modello: team e persone dell'azienda (in forza o assegnate a un team).</summary>
    private async Task<IActionResult> Pagina(Guid? aziendaId, string vista)
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
                d.FotoChiave is not null, d.Segnaposto, ServizioUfficio.LavoraADistanza(d, oggi)))
            .ToList();

        return View(vista, new OrganigrammaViewModel
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
            // Nuovo team senza colore scelto: il primo colore della tavolozza non ancora usato (finiti quelli, a rotazione).
            if (id is null && string.IsNullOrWhiteSpace(colore))
                colore = Team.Palette.Select(p => p.Hex).FirstOrDefault(h => tutti.All(t => !string.Equals(t.Colore, h, StringComparison.OrdinalIgnoreCase)))
                         ?? Team.Palette[tutti.Count % Team.Palette.Count].Hex;
            team.ImpostaAspetto(colore ?? team.Colore, emoji);
        }
        catch (DomainException ex)
        {
            Errore(ex.Message);
            return id is { } tornaA ? RedirectToAction(nameof(Dettaglio), new { id = tornaA, modifica = true }) : Torna(az.Value);
        }
        await db.SaveChangesAsync();
        Conferma(id is null ? $"Team \"{team.Nome}\" creato: aggiungi i membri." : "Team aggiornato.");
        return RedirectToAction(nameof(Dettaglio), new { id = team.Id });
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
        if (ProvaOAvvisa(() => dip.ImpostaOrganizzazione(team, posizione, resp)))
        {
            await db.SaveChangesAsync();
            Conferma($"Organizzazione di {dip.NomeCompleto} aggiornata.");
        }
        return Url.IsLocalUrl(ritorno) ? LocalRedirect(ritorno) : Torna(dip.AziendaId);
    }

    /// <summary>Il referente lavora sempre sulla propria azienda; il consulente su quella indicata (il perimetro lo limita il database).</summary>
    private Guid? Azienda(Guid? richiesta) => utente.IsAzienda ? utente.AziendaId : richiesta;

    /// <summary>Dopo una modifica si torna alla pagina dei team.</summary>
    private IActionResult Torna(Guid aziendaId) =>
        RedirectToAction(nameof(Index), new { aziendaId = utente.IsConsulente ? aziendaId : (Guid?)null });
}
