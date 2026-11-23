using System.ComponentModel.DataAnnotations;
using System.Text;
using CL.Infrastructure.Identity;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace CL.Web.Controllers;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Inserisci l'email."), EmailAddress(ErrorMessage = "Email non valida.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Inserisci la password."), DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = "";

    [Display(Name = "Resta collegato")]
    public bool Ricordami { get; set; }

    public string? ReturnUrl { get; set; }
}

public sealed class ImpostaPasswordViewModel
{
    public string Uid { get; set; } = "";
    public string Token { get; set; } = "";
    public string Email { get; set; } = "";
    public bool PrimoAccesso { get; set; }

    [Required(ErrorMessage = "Scegli una password."), DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 10, ErrorMessage = "Almeno 10 caratteri.")]
    [Display(Name = "Nuova password")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Ripeti la password."), DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Le due password non coincidono.")]
    [Display(Name = "Ripeti la password")]
    public string Conferma { get; set; } = "";
}

public sealed class PasswordDimenticataViewModel
{
    [Required(ErrorMessage = "Inserisci l'email."), EmailAddress(ErrorMessage = "Email non valida.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

    public bool Inviata { get; set; }
}

public sealed class ProfiloViewModel
{
    public string Email { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Ruolo { get; set; } = "";
    /// <summary>Per il consulente: nome e cognome separati, modificabili.</summary>
    public string NomeProprio { get; set; } = "";
    public string Cognome { get; set; } = "";

    [Required(ErrorMessage = "Inserisci la password attuale."), DataType(DataType.Password)]
    [Display(Name = "Password attuale")]
    public string PasswordAttuale { get; set; } = "";

    [Required(ErrorMessage = "Scegli la nuova password."), DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 10, ErrorMessage = "Almeno 10 caratteri.")]
    [Display(Name = "Nuova password")]
    public string NuovaPassword { get; set; } = "";

    [Required(ErrorMessage = "Ripeti la nuova password."), DataType(DataType.Password)]
    [Compare(nameof(NuovaPassword), ErrorMessage = "Le due password non coincidono.")]
    [Display(Name = "Ripeti la nuova password")]
    public string Conferma { get; set; } = "";
}

public sealed class AccountController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users,
    ServizioAccessi accessi, UtenteCorrente corrente, ILogger<AccountController> logger) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var utente = await users.FindByEmailAsync(model.Email);
        if (utente is { Attivo: false })
        {
            ModelState.AddModelError("", "Utente disattivato. Contatta il consulente.");
            return View(model);
        }

        var esito = utente is null
            ? Microsoft.AspNetCore.Identity.SignInResult.Failed
            : await signIn.PasswordSignInAsync(utente, model.Password, model.Ricordami, lockoutOnFailure: true);

        if (esito.Succeeded)
            return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl) : RedirectToAction("Index", "Home");

        ModelState.AddModelError("", esito.IsLockedOut
            ? "Troppi tentativi falliti: account bloccato per 15 minuti."
            : "Email o password non corretti.");
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    /// <summary>Dal link di invito o di recupero: l'utente sceglie la password ed entra.</summary>
    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> ImpostaPassword(string? uid, string? token)
    {
        var utente = await UtenteDaLink(uid, token);
        if (utente is null) return View("LinkNonValido");
        return View(new ImpostaPasswordViewModel { Uid = uid!, Token = token!, Email = utente.Email!, PrimoAccesso = !utente.EmailConfirmed });
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ImpostaPassword(ImpostaPasswordViewModel model)
    {
        var utente = await UtenteDaLink(model.Uid, model.Token);
        if (utente is null) return View("LinkNonValido");
        model.Email = utente.Email!;
        model.PrimoAccesso = !utente.EmailConfirmed;
        if (!ModelState.IsValid) return View(model);

        var esito = await users.ResetPasswordAsync(utente, Decodifica(model.Token)!, model.Password);
        if (!esito.Succeeded)
        {
            // Token scaduto o già usato, oppure password non conforme alle regole.
            foreach (var e in esito.Errors)
                ModelState.AddModelError(e.Code == "InvalidToken" ? "" : nameof(model.Password),
                    e.Code == "InvalidToken" ? "Il link è scaduto o è già stato usato: chiedine uno nuovo." : e.Description);
            return View(model);
        }

        if (!utente.EmailConfirmed)
        {
            utente.EmailConfirmed = true; // aver ricevuto il link dimostra il possesso dell'indirizzo
            await users.UpdateAsync(utente);
        }
        await users.ResetAccessFailedCountAsync(utente);
        await users.SetLockoutEndDateAsync(utente, null);
        await signIn.SignInAsync(utente, isPersistent: false);
        TempData["Messaggio"] = model.PrimoAccesso ? "Benvenuto in Consulens HR: il tuo accesso è attivo." : "Password aggiornata.";
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous, HttpGet]
    public IActionResult PasswordDimenticata() => View(new PasswordDimenticataViewModel());

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PasswordDimenticata(PasswordDimenticataViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        // Stessa risposta che l'utente esista o no: la pagina non rivela quali email sono registrate.
        if (await users.FindByEmailAsync(model.Email) is { Attivo: true } utente)
        {
            var esito = await accessi.InviaLinkAsync(utente);
            if (!esito.EmailInviata)
                logger.LogWarning("Email non configurata: link di reimpostazione per {Email} non inviato. In sviluppo: {Link}",
                    utente.Email, esito.Link);
        }
        return View(new PasswordDimenticataViewModel { Email = model.Email, Inviata = true });
    }

    [HttpGet]
    public async Task<IActionResult> Profilo() => View(await ProfiloDi(new ProfiloViewModel()));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Profilo(ProfiloViewModel model)
    {
        var utente = await users.GetUserAsync(User);
        if (utente is null) return Challenge();
        if (!ModelState.IsValid) return View(await ProfiloDi(model));

        var esito = await users.ChangePasswordAsync(utente, model.PasswordAttuale, model.NuovaPassword);
        if (!esito.Succeeded)
        {
            foreach (var e in esito.Errors)
                ModelState.AddModelError(e.Code == "PasswordMismatch" ? nameof(model.PasswordAttuale) : nameof(model.NuovaPassword),
                    e.Code == "PasswordMismatch" ? "La password attuale non è corretta." : e.Description);
            return View(await ProfiloDi(model));
        }

        await signIn.RefreshSignInAsync(utente); // il nuovo security stamp chiude le altre sessioni, non questa
        TempData["Messaggio"] = "Password aggiornata.";
        return RedirectToAction(nameof(Profilo));
    }

    /// <summary>Il consulente cambia da sé nome, cognome ed email di accesso (gli altri li chiedono al consulente).</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Ruoli.Consulente)]
    public async Task<IActionResult> DatiProfilo(string? nome, string? cognome, string? email)
    {
        var utente = await users.GetUserAsync(User);
        if (utente is null) return Challenge();
        if (string.IsNullOrWhiteSpace(nome) || string.IsNullOrWhiteSpace(cognome))
        {
            TempData["Errore"] = "Inserisci nome e cognome.";
            return RedirectToAction(nameof(Profilo));
        }
        utente.Nome = nome.Trim();
        utente.Cognome = cognome.Trim();
        await users.UpdateAsync(utente);
        if (!string.IsNullOrWhiteSpace(email) && await accessi.CambiaEmailAsync(utente, email) is { } errore)
        {
            TempData["Errore"] = errore;
            return RedirectToAction(nameof(Profilo));
        }
        await signIn.RefreshSignInAsync(utente); // nome ed email aggiornati anche in alto a destra
        TempData["Messaggio"] = "Dati aggiornati.";
        return RedirectToAction(nameof(Profilo));
    }

    private async Task<ProfiloViewModel> ProfiloDi(ProfiloViewModel model)
    {
        var utente = await users.GetUserAsync(User);
        model.Email = utente?.Email ?? "";
        model.NomeProprio = utente?.Nome ?? "";
        model.Cognome = utente?.Cognome ?? "";
        model.Nome = corrente.Nome;
        model.Ruolo = corrente.IsConsulente ? "Consulente" : corrente.IsAzienda ? "Referente azienda" : "Dipendente";
        return model;
    }

    private async Task<ApplicationUser?> UtenteDaLink(string? uid, string? token)
    {
        if (string.IsNullOrEmpty(uid) || Decodifica(token) is not { } t) return null;
        if (await users.FindByIdAsync(uid) is not { Attivo: true } u) return null;
        // Verifica subito il token (scaduto o già usato) invece di scoprirlo solo all'invio del form.
        return await users.VerifyUserTokenAsync(u, users.Options.Tokens.PasswordResetTokenProvider,
            UserManager<ApplicationUser>.ResetPasswordTokenPurpose, t) ? u : null;
    }

    private static string? Decodifica(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
        catch (FormatException) { return null; }
    }

    [AllowAnonymous]
    public IActionResult AccessoNegato() => View();
}
