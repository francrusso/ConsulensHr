using System.ComponentModel.DataAnnotations;
using CL.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

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

public sealed class AccountController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users) : Controller
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
            ModelState.AddModelError("", "Utente disattivato. Contatta lo studio.");
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

    [AllowAnonymous]
    public IActionResult AccessoNegato() => View();
}
