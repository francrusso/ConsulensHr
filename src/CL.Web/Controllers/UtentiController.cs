using System.ComponentModel.DataAnnotations;
using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

public sealed class InvitoForm
{
    [Required(ErrorMessage = "Inserisci l'email."), EmailAddress(ErrorMessage = "Email non valida."), StringLength(256)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Inserisci il nome."), StringLength(100)]
    public string Nome { get; set; } = "";

    [Required(ErrorMessage = "Inserisci il cognome."), StringLength(100)]
    public string Cognome { get; set; } = "";
}

public sealed class UtentiViewModel
{
    public required List<UtenteRiga> Utenti { get; init; }
    public string? Ruolo { get; init; }
    public string? Ricerca { get; init; }
}

/// <summary>
/// Accessi al portale. Il consulente gestisce quelli delle aziende del suo perimetro; il referente di un'azienda
/// quelli dei colleghi e del personale della propria azienda (dalla scheda di ogni persona); i consulenti li crea
/// e gestisce il super amministratore. Nessuno può disattivare sé stesso.
/// </summary>
[Authorize(Policy = Policy.GestionePersonale)]
public sealed class UtentiController(
    AppDbContext db, UserManager<ApplicationUser> users, ServizioAccessi accessi, UtenteCorrente utente,
    PerimetroAziende perimetro) : AppController
{
    [Authorize(Policy = Policy.SoloConsulente)]
    public async Task<IActionResult> Index(string? ruolo, string? q)
    {
        var query = accessi.Elenco();
        // Il consulente vede gli accessi delle sue aziende (e il proprio); i consulenti li gestisce il super amministratore.
        if (perimetro.Consentite is { } consentite)
        {
            var mio = utente.UserId;
            query = query.Where(u => u.Id == mio || (u.AziendaId != null && consentite.Contains(u.AziendaId.Value)));
        }
        if (!string.IsNullOrEmpty(ruolo)) query = query.Where(u => u.Ruolo == ruolo);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim().ToLower();
            query = query.Where(u => u.Email.ToLower().Contains(t) || u.Nome.ToLower().Contains(t)
                                     || (u.Azienda != null && u.Azienda.ToLower().Contains(t)));
        }
        var elenco = await query.OrderBy(u => u.Nome).ThenBy(u => u.Email).ToListAsync();
        return View(new UtentiViewModel { Utenti = elenco, Ruolo = ruolo, Ricerca = q });
    }

    /// <summary>Solo il super amministratore crea i consulenti.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Ruoli.Amministratore)]
    public async Task<IActionResult> InvitaConsulente(InvitoForm form, string? ritorno = null) =>
        await Invita(form, Ruoli.Consulente, null, null, Url.IsLocalUrl(ritorno) ? ritorno! : Url.Action("Index", "Consulenti")!);

    /// <summary>"Aggiungi altro utente": referente di un'azienda, persona del personale o (super amministratore) consulente.</summary>
    [Authorize(Policy = Policy.SoloConsulente)]
    public async Task<IActionResult> Nuovo()
    {
        ViewBag.Aziende = await db.Aziende.AsNoTracking().Where(a => a.Attiva).OrderBy(a => a.RagioneSociale)
            .Select(a => new SelectListItem(a.RagioneSociale, a.Id.ToString())).ToListAsync();
        // Persone con email e ancora senza accesso al portale.
        ViewBag.Persone = (await (from d in db.Dipendenti.AsNoTracking().IgnoreAutoIncludes()
                                  join a in db.Aziende.AsNoTracking() on d.AziendaId equals a.Id
                                  where d.Email != null && d.Email != "" && !db.Users.Any(u => u.DipendenteId == d.Id)
                                  orderby d.Cognome, d.Nome
                                  select new { d.Id, Nome = d.Cognome + " " + d.Nome, d.Email, a.RagioneSociale }).ToListAsync())
            .Select(x => new SelectListItem($"{x.Nome} · {x.RagioneSociale} · {x.Email}", x.Id.ToString())).ToList();
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> InvitaReferente(Guid aziendaId, InvitoForm form, string? ritorno = null)
    {
        if (!utente.PuoGestireAzienda(aziendaId) || !await db.Aziende.AnyAsync(a => a.Id == aziendaId)) return Forbid();
        ritorno = Url.IsLocalUrl(ritorno) ? ritorno
            : utente.IsConsulente ? Url.Action("Dettaglio", "Aziende", new { id = aziendaId })! : Url.Action("Index", "Dipendenti")!;
        return await Invita(form, Ruoli.Azienda, aziendaId, null, ritorno);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> InvitaDipendente(Guid dipendenteId, bool comeAzienda = false, string? ritorno = null)
    {
        var dip = await utente.Filtra(db.Dipendenti.AsNoTracking()).FirstOrDefaultAsync(d => d.Id == dipendenteId);
        if (dip is null) return NotFound();
        ritorno = Url.IsLocalUrl(ritorno) ? ritorno! : Url.Action("Dettaglio", "Dipendenti", new { id = dipendenteId })!;

        if (string.IsNullOrWhiteSpace(dip.Email))
        {
            Errore("Per invitare il dipendente serve la sua email: aggiungila nell'anagrafica.");
            return LocalRedirect(ritorno);
        }
        if (await db.Users.AnyAsync(u => u.DipendenteId == dipendenteId))
        {
            Errore("Il dipendente ha già un accesso al portale.");
            return LocalRedirect(ritorno);
        }
        return await Invita(new InvitoForm { Email = dip.Email, Nome = dip.Nome, Cognome = dip.Cognome },
            comeAzienda ? Ruoli.Azienda : Ruoli.Dipendente, dip.AziendaId, dip.Id, ritorno, validaForm: false);
    }

    /// <summary>
    /// La persona gestisce (o smette di gestire) il portale per conto dell'azienda: il suo accesso passa da dipendente
    /// ad azienda e viceversa. Le sessioni aperte si chiudono, così al prossimo accesso vede il menù giusto.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AccessoAzienda(Guid dipendenteId, bool attivo, string? ritorno)
    {
        var dip = await utente.Filtra(db.Dipendenti.AsNoTracking().IgnoreAutoIncludes()).FirstOrDefaultAsync(d => d.Id == dipendenteId);
        if (dip is null || await users.Users.FirstOrDefaultAsync(x => x.DipendenteId == dipendenteId) is not { } u) return NotFound();
        if (!attivo && u.Id == utente.UserId)
        {
            Errore("Non puoi togliere a te stesso la gestione del portale.");
            return Torna(ritorno);
        }
        var (togli, metti) = attivo ? (Ruoli.Dipendente, Ruoli.Azienda) : (Ruoli.Azienda, Ruoli.Dipendente);
        if (await users.IsInRoleAsync(u, togli)) await users.RemoveFromRoleAsync(u, togli);
        if (!await users.IsInRoleAsync(u, metti)) await users.AddToRoleAsync(u, metti);
        u.AziendaId = dip.AziendaId; // serve al ruolo azienda per vedere la propria azienda
        await users.UpdateAsync(u);
        await users.UpdateSecurityStampAsync(u);
        Conferma(attivo ? $"{dip.NomeCompleto} ora può gestire il portale per conto dell'azienda." : $"{dip.NomeCompleto} accede di nuovo come dipendente.");
        return Torna(ritorno);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reinvia(string id, string? ritorno)
    {
        var u = await Gestibile(id);
        if (u is null) return NotFound();
        if (!u.Attivo)
        {
            Errore("L'accesso è disattivato: riattivalo prima di inviare un nuovo link.");
            return Torna(ritorno);
        }
        Esito(await accessi.InviaLinkAsync(u), u.Email!, nuovo: false);
        return Torna(ritorno);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiaEmail(string id, string? email, string? ritorno)
    {
        var u = await Gestibile(id);
        if (u is null) return NotFound();
        var vecchia = u.Email;
        if (await accessi.CambiaEmailAsync(u, email ?? "") is { } errore) Errore(errore);
        else if (!string.Equals(vecchia, u.Email, StringComparison.OrdinalIgnoreCase)) Conferma($"Email di accesso cambiata in {u.Email}.");
        return Torna(ritorno);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiaStato(string id, string? ritorno)
    {
        var u = await Gestibile(id);
        if (u is null) return NotFound();
        if (u.Id == utente.UserId)
        {
            Errore("Non puoi disattivare il tuo stesso accesso.");
            return Torna(ritorno);
        }
        if (await accessi.ImpostaAttivoAsync(u, !u.Attivo))
            Conferma(u.Attivo ? $"Accesso di {u.Email} riattivato." : $"Accesso di {u.Email} disattivato: non potrà più entrare.");
        else Errore("Operazione non riuscita.");
        return Torna(ritorno);
    }

    /// <summary>
    /// Vecchio indirizzo della pagina dei referenti: i referenti ora si gestiscono dalla scheda di ogni persona
    /// (accesso come azienda), quindi si va all'elenco del personale (o, per lo studio, a quello degli utenti).
    /// </summary>
    public IActionResult Azienda() => Torna(null);

    private async Task<IActionResult> Invita(InvitoForm form, string ruolo, Guid? aziendaId, Guid? dipendenteId, string ritorno,
        bool validaForm = true)
    {
        if (validaForm && !ModelState.IsValid)
        {
            Errore(string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
            return LocalRedirect(ritorno);
        }
        var esito = await accessi.InvitaAsync(form.Email, form.Nome, form.Cognome, ruolo, aziendaId, dipendenteId);
        if (!esito.Riuscito) Errore(esito.Errore!);
        else Esito(esito, form.Email.Trim(), nuovo: true);
        return LocalRedirect(ritorno);
    }

    private void Esito(EsitoInvito esito, string email, bool nuovo)
    {
        if (esito.EmailInviata)
        {
            Conferma(nuovo ? $"Invito inviato a {email}." : $"Nuovo link inviato a {email}.");
            return;
        }
        // Email non configurata (o invio fallito): chi invita comunica il link di persona (lo mostra _Messaggi).
        Conferma(nuovo ? $"Accesso creato per {email}." : $"Nuovo link generato per {email}.");
        TempData["LinkInvito"] = esito.Link;
        TempData["LinkInvitoEmail"] = email;
    }

    /// <summary>
    /// L'utente se rientra nel perimetro di chi agisce, altrimenti null: il super amministratore gestisce tutti;
    /// un consulente sé stesso e gli accessi delle aziende assegnate; un referente quelli della propria azienda.
    /// </summary>
    private async Task<ApplicationUser?> Gestibile(string id)
    {
        var u = await users.FindByIdAsync(id);
        if (u is null) return null;
        if (utente.IsAmministratore) return u;
        if (await users.IsInRoleAsync(u, Ruoli.Consulente)) return u.Id == utente.UserId ? u : null;
        if (utente.IsConsulente) return u.AziendaId is { } az && (perimetro.Consentite is null || perimetro.Consentite.Contains(az)) ? u : null;
        return u.AziendaId is { } a && a == utente.AziendaId ? u : null;
    }

    private IActionResult Torna(string? ritorno) =>
        Url.IsLocalUrl(ritorno) ? LocalRedirect(ritorno)
        : utente.IsConsulente ? RedirectToAction(nameof(Index)) : RedirectToAction("Index", "Dipendenti");
}
