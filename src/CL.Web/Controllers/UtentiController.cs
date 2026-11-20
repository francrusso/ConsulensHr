using System.ComponentModel.DataAnnotations;
using CL.Infrastructure.Email;
using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
/// Accessi al portale. Il consulente gestisce tutti; il referente di un'azienda gestisce i colleghi referenti
/// e i dipendenti della propria azienda. Nessuno può disattivare sé stesso.
/// </summary>
[Authorize(Policy = Policy.GestionePersonale)]
public sealed class UtentiController(
    AppDbContext db, UserManager<ApplicationUser> users, ServizioAccessi accessi, UtenteCorrente utente, IInvioEmail email) : AppController
{
    [Authorize(Policy = Policy.SoloConsulente)]
    public async Task<IActionResult> Index(string? ruolo, string? q)
    {
        var query = accessi.Elenco();
        if (!string.IsNullOrEmpty(ruolo)) query = query.Where(u => u.Ruolo == ruolo);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim().ToLower();
            query = query.Where(u => u.Email.ToLower().Contains(t) || u.Nome.ToLower().Contains(t)
                                     || (u.Azienda != null && u.Azienda.ToLower().Contains(t)));
        }
        var elenco = await query.OrderBy(u => u.Nome).ThenBy(u => u.Email).ToListAsync();
        ViewBag.EmailConfigurata = email.Configurato;
        return View(new UtentiViewModel { Utenti = elenco, Ruolo = ruolo, Ricerca = q });
    }

    /// <summary>Verifica la configurazione SMTP inviando un messaggio all'utente collegato.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.SoloConsulente)]
    public async Task<IActionResult> EmailProva()
    {
        var destinatario = utente.Email;
        if (!email.Configurato)
            Errore("L'invio delle email non è configurato: mancano server, mittente o password negli user-secrets.");
        else if (await email.InviaAsync(destinatario, "Prova di invio da Consulens HR",
                     "<p>Se leggi questo messaggio, l'invio delle email di Consulens HR funziona.</p>"))
            Conferma($"Email di prova inviata a {destinatario}: controlla la casella (anche lo spam).");
        else
            Errore("Invio non riuscito: il server ha rifiutato il messaggio. Il dettaglio è nel log del terminale (di solito password per app errata).");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.SoloConsulente)]
    public async Task<IActionResult> InvitaConsulente(InvitoForm form) =>
        await Invita(form, Ruoli.Consulente, null, null, Url.Action(nameof(Index))!);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> InvitaReferente(Guid aziendaId, InvitoForm form)
    {
        if (!utente.PuoGestireAzienda(aziendaId) || !await db.Aziende.AnyAsync(a => a.Id == aziendaId)) return Forbid();
        var ritorno = utente.IsConsulente ? Url.Action("Dettaglio", "Aziende", new { id = aziendaId })! : Url.Action(nameof(Azienda))!;
        return await Invita(form, Ruoli.Azienda, aziendaId, null, ritorno);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> InvitaDipendente(Guid dipendenteId)
    {
        var dip = await utente.Filtra(db.Dipendenti.AsNoTracking()).FirstOrDefaultAsync(d => d.Id == dipendenteId);
        if (dip is null) return NotFound();
        var ritorno = Url.Action("Dettaglio", "Dipendenti", new { id = dipendenteId })!;

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
            Ruoli.Dipendente, dip.AziendaId, dip.Id, ritorno, validaForm: false);
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
    public async Task<IActionResult> CambiaStato(string id, string? ritorno)
    {
        var u = await Gestibile(id);
        if (u is null) return NotFound();
        if (u.Id == users.GetUserId(User))
        {
            Errore("Non puoi disattivare il tuo stesso accesso.");
            return Torna(ritorno);
        }
        if (await accessi.ImpostaAttivoAsync(u, !u.Attivo))
            Conferma(u.Attivo ? $"Accesso di {u.Email} riattivato." : $"Accesso di {u.Email} disattivato: non potrà più entrare.");
        else Errore("Operazione non riuscita.");
        return Torna(ritorno);
    }

    /// <summary>Per il referente: i colleghi con accesso alla propria azienda.</summary>
    public async Task<IActionResult> Azienda()
    {
        if (utente.IsConsulente) return RedirectToAction(nameof(Index));
        var aziendaId = utente.AziendaId;
        ViewBag.AziendaId = aziendaId;
        var elenco = await accessi.Elenco().Where(u => u.AziendaId == aziendaId && u.Ruolo == Ruoli.Azienda)
            .OrderBy(u => u.Nome).ToListAsync();
        return View(elenco);
    }

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
        // Email non configurata (o invio fallito): chi invita comunica il link di persona.
        Conferma(nuovo ? $"Accesso creato per {email}." : $"Nuovo link generato per {email}.");
        TempData["LinkInvito"] = esito.Link;
        TempData["LinkInvitoEmail"] = email;
    }

    /// <summary>L'utente se rientra nel perimetro di chi agisce, altrimenti null.</summary>
    private async Task<ApplicationUser?> Gestibile(string id)
    {
        var u = await users.FindByIdAsync(id);
        if (u is null) return null;
        if (utente.IsConsulente) return u;
        if (await users.IsInRoleAsync(u, Ruoli.Consulente)) return null;
        return u.AziendaId is { } a && a == utente.AziendaId ? u : null;
    }

    private IActionResult Torna(string? ritorno) =>
        Url.IsLocalUrl(ritorno) ? LocalRedirect(ritorno) : RedirectToAction(utente.IsConsulente ? nameof(Index) : nameof(Azienda));
}
