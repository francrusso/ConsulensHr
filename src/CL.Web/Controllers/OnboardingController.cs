using System.ComponentModel.DataAnnotations;
using System.Net;
using CL.Domain.Aziende;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Email;
using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

#region Form dei passi

public sealed class SocietariForm
{
    [Required(ErrorMessage = "Inserisci la ragione sociale."), StringLength(200), Display(Name = "Ragione sociale")]
    public string RagioneSociale { get; set; } = "";

    [Required(ErrorMessage = "Scegli la forma giuridica."), Display(Name = "Forma giuridica")]
    public string? FormaGiuridica { get; set; }

    [StringLength(16), Display(Name = "Codice fiscale dell'azienda")]
    public string? CodiceFiscale { get; set; }

    [StringLength(30), Display(Name = "Numero REA")]
    public string? NumeroRea { get; set; }

    [Required(ErrorMessage = "Inserisci il codice ATECO."), Display(Name = "Codice ATECO dell'attività principale")]
    [RegularExpression(@"^\d{2}(\.\d{1,2}){0,2}$", ErrorMessage = "Formato ATECO: ad esempio 47.11.10")]
    public string? CodiceAteco { get; set; }

    [Display(Name = "Data di inizio attività")]
    public DateOnly? DataInizioAttivita { get; set; }

    [Required(ErrorMessage = "Inserisci la PEC."), EmailAddress(ErrorMessage = "PEC non valida."), StringLength(256), Display(Name = "PEC")]
    public string? Pec { get; set; }

    [EmailAddress(ErrorMessage = "Email non valida."), StringLength(256), Display(Name = "Email ordinaria")]
    public string? Email { get; set; }

    [StringLength(30), Display(Name = "Telefono")]
    public string? Telefono { get; set; }

    [Display(Name = "Logo dell'azienda (facoltativo: PNG, JPEG o WebP, massimo 1 MB)")]
    public IFormFile? Logo { get; set; }
    public bool HaLogo { get; set; }

    public static readonly string[] Forme =
    [
        "Ditta individuale", "S.n.c.", "S.a.s.", "S.r.l.", "S.r.l. semplificata", "S.p.A.", "Società cooperativa",
        "Associazione / ente non commerciale", "Studio professionale", "Altro",
    ];
}

public sealed class SediForm
{
    [Required(ErrorMessage = "Inserisci l'indirizzo."), StringLength(300), Display(Name = "Indirizzo")]
    public string? LegaleVia { get; set; }
    [Required(ErrorMessage = "Inserisci il CAP."), RegularExpression(@"^\d{5}$", ErrorMessage = "CAP di 5 cifre."), Display(Name = "CAP")]
    public string? LegaleCap { get; set; }
    [Required(ErrorMessage = "Inserisci il comune."), StringLength(150), Display(Name = "Comune")]
    public string? LegaleComune { get; set; }
    [Required(ErrorMessage = "Inserisci la provincia."), RegularExpression(@"^[A-Za-z]{2}$", ErrorMessage = "Sigla di 2 lettere (es. MI)."), Display(Name = "Provincia")]
    public string? LegaleProvincia { get; set; }

    [Display(Name = "I dipendenti lavorano in una sede diversa da quella legale")]
    public bool OperativaDiversa { get; set; }
    [StringLength(300), Display(Name = "Indirizzo")]
    public string? OperativaVia { get; set; }
    [RegularExpression(@"^\d{5}$", ErrorMessage = "CAP di 5 cifre."), Display(Name = "CAP")]
    public string? OperativaCap { get; set; }
    [StringLength(150), Display(Name = "Comune")]
    public string? OperativaComune { get; set; }
    [RegularExpression(@"^[A-Za-z]{2}$", ErrorMessage = "Sigla di 2 lettere (es. MI)."), Display(Name = "Provincia")]
    public string? OperativaProvincia { get; set; }

}

public sealed class LegaleForm
{
    [Required(ErrorMessage = "Inserisci il nome."), StringLength(150), Display(Name = "Nome")]
    public string? Nome { get; set; }
    [Required(ErrorMessage = "Inserisci il cognome."), StringLength(150), Display(Name = "Cognome")]
    public string? Cognome { get; set; }
    [Required(ErrorMessage = "Inserisci il codice fiscale."), Display(Name = "Codice fiscale")]
    public string? CodiceFiscale { get; set; }
    [EmailAddress(ErrorMessage = "Email non valida."), StringLength(256), Display(Name = "Email")]
    public string? Email { get; set; }
    [StringLength(30), Display(Name = "Telefono")]
    public string? Telefono { get; set; }
}

public sealed class PosizioniForm
{
    [Required(ErrorMessage = "Indica il CCNL applicato."), StringLength(150), Display(Name = "CCNL applicato")]
    public string? Ccnl { get; set; }
    [Required(ErrorMessage = "Inserisci la matricola INPS."), StringLength(30), Display(Name = "Matricola aziendale INPS")]
    public string? MatricolaInps { get; set; }
    [Required(ErrorMessage = "Inserisci il codice ditta INAIL."), StringLength(30), Display(Name = "Codice ditta INAIL")]
    public string? InailCodiceDitta { get; set; }
    [Required(ErrorMessage = "Inserisci la PAT INAIL."), StringLength(30), Display(Name = "PAT (posizione assicurativa territoriale)")]
    public string? InailPat { get; set; }
    [StringLength(150), Display(Name = "Cassa Edile")]
    public string? CassaEdile { get; set; }
    [StringLength(150), Display(Name = "Ente bilaterale")]
    public string? EnteBilaterale { get; set; }
    [StringLength(150), Display(Name = "Fondo di previdenza complementare")]
    public string? FondoPensione { get; set; }
    [Range(5, 6, ErrorMessage = "5 o 6 giorni."), Display(Name = "Giorni lavorativi a settimana")]
    public int GiorniLavorativiSettimana { get; set; } = 5;
    [Range(1, 48, ErrorMessage = "Tra 1 e 48 ore."), Display(Name = "Orario settimanale a tempo pieno (ore)")]
    public decimal OreSettimanali { get; set; } = 40;
}

public sealed class PagheForm
{
    [Display(Name = "IBAN per l'addebito di stipendi e F24")]
    public string? Iban { get; set; }
    [Range(1, 31, ErrorMessage = "Giorno tra 1 e 31."), Display(Name = "Giorno del mese in cui paghi gli stipendi")]
    public int? GiornoPagamento { get; set; }
    [Required(ErrorMessage = "Indica chi seguirà le paghe."), StringLength(150), Display(Name = "Nome e cognome")]
    public string? ReferenteNome { get; set; }
    [Required(ErrorMessage = "Inserisci l'email."), EmailAddress(ErrorMessage = "Email non valida."), StringLength(256), Display(Name = "Email")]
    public string? ReferenteEmail { get; set; }
    [StringLength(30), Display(Name = "Telefono")]
    public string? ReferenteTelefono { get; set; }
    [StringLength(2000), Display(Name = "Note per lo studio")]
    public string? NoteStudio { get; set; }
}

#endregion

public sealed class OnboardingViewModel
{
    public required Guid AziendaId { get; init; }
    public required Azienda Azienda { get; init; }
    public required ProfiloAzienda Profilo { get; init; }
    public required int Passo { get; init; }
    public object? Form { get; init; }
    /// <summary>Lo studio che compila per conto dell'azienda (link e testi diversi).</summary>
    public bool PerContoAzienda { get; init; }

    public static readonly string[] Titoli = ["Dati societari", "Sedi", "Legale rappresentante", "Contratto e posizioni", "Paghe e referente", "Riepilogo"];
}

/// <summary>
/// Percorso guidato con cui l'azienda fornisce allo studio i dati per gestire le pratiche di lavoro.
/// Obbligatorio per il referente al primo accesso; lo studio può consultarlo e compilarlo per conto dell'azienda.
/// </summary>
[Authorize(Policy = Policy.GestionePersonale)]
public sealed class OnboardingController(
    AppDbContext db, UtenteCorrente utente, SignInManager<ApplicationUser> signIn, CodaEmail coda,
    ServizioAccessi accessi, LoghiAziende loghi, TimeProvider clock) : AppController
{
    public async Task<IActionResult> Index(Guid? aziendaId, int? passo)
    {
        var (azienda, profilo) = await Carica(aziendaId);
        if (azienda is null) return utente.IsConsulente ? RedirectToAction("Index", "Aziende") : NotFound();

        // Si può tornare ai passi già fatti, non saltare in avanti.
        var p = Math.Clamp(passo ?? profilo.Passo, 1, profilo.Passo);
        return Mostra(azienda, profilo, p, FormDi(p, azienda, profilo));
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(LoghiAziende.DimensioneMassima + 256 * 1024)]
    public async Task<IActionResult> Societari(Guid? aziendaId, SocietariForm form)
    {
        var (azienda, profilo) = await Carica(aziendaId);
        if (azienda is null) return NotFound();
        if (form.FormaGiuridica is not null && !SocietariForm.Forme.Contains(form.FormaGiuridica))
            ModelState.AddModelError(nameof(form.FormaGiuridica), "Scegli una voce dell'elenco.");
        if (!ModelState.IsValid) return Mostra(azienda, profilo, 1, form);

        if (!Prova(() =>
            {
                azienda.AggiornaAnagrafica(form.RagioneSociale, form.CodiceFiscale, azienda.Indirizzo, azienda.Cap, azienda.Comune, azienda.Provincia,
                    string.IsNullOrWhiteSpace(form.Email) ? azienda.Email : form.Email);
                profilo.ImpostaDatiSocietari(new DatiSocietari(form.FormaGiuridica!, form.NumeroRea, form.CodiceAteco!,
                    form.DataInizioAttivita, form.Pec!, form.Telefono));
            })) return Mostra(azienda, profilo, 1, form);
        if (form.Logo is { Length: > 0 } logo && await loghi.SalvaAsync(azienda, logo) is { } errore)
        {
            ModelState.AddModelError(nameof(form.Logo), errore);
            return Mostra(azienda, profilo, 1, form);
        }

        return await SalvaEAvanza(azienda, profilo, 1);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Sedi(Guid? aziendaId, SediForm form)
    {
        var (azienda, profilo) = await Carica(aziendaId);
        if (azienda is null) return NotFound();
        if (form.OperativaDiversa)
        {
            if (string.IsNullOrWhiteSpace(form.OperativaVia)) ModelState.AddModelError(nameof(form.OperativaVia), "Inserisci l'indirizzo.");
            if (string.IsNullOrWhiteSpace(form.OperativaCap)) ModelState.AddModelError(nameof(form.OperativaCap), "Inserisci il CAP.");
            if (string.IsNullOrWhiteSpace(form.OperativaComune)) ModelState.AddModelError(nameof(form.OperativaComune), "Inserisci il comune.");
            if (string.IsNullOrWhiteSpace(form.OperativaProvincia)) ModelState.AddModelError(nameof(form.OperativaProvincia), "Inserisci la provincia.");
        }
        if (!ModelState.IsValid) return Mostra(azienda, profilo, 2, form);

        var legale = new Indirizzo(form.LegaleVia!, form.LegaleCap!, form.LegaleComune!, form.LegaleProvincia!);
        var operativa = form.OperativaDiversa ? new Indirizzo(form.OperativaVia!, form.OperativaCap!, form.OperativaComune!, form.OperativaProvincia!) : null;
        if (!Prova(() =>
            {
                profilo.ImpostaSedi(legale, operativa);
                // L'indirizzo dell'anagrafica è quello dove si lavora.
                var dove = profilo.SedeOperativa ?? profilo.SedeLegale!;
                azienda.AggiornaAnagrafica(azienda.RagioneSociale, azienda.CodiceFiscale, dove.Via, dove.Cap, dove.Comune, dove.Provincia, azienda.Email);
            })) return Mostra(azienda, profilo, 2, form);

        return await SalvaEAvanza(azienda, profilo, 2);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Legale(Guid? aziendaId, LegaleForm form)
    {
        var (azienda, profilo) = await Carica(aziendaId);
        if (azienda is null) return NotFound();
        if (!ModelState.IsValid) return Mostra(azienda, profilo, 3, form);

        CodiceFiscale? cf = null;
        if (!Prova(() => cf = CL.Domain.ValueObjects.CodiceFiscale.Parse(form.CodiceFiscale!), nameof(form.CodiceFiscale))
            || !Prova(() => profilo.ImpostaLegaleRappresentante(new LegaleRappresentante(form.Nome!, form.Cognome!, cf!, form.Email, form.Telefono))))
            return Mostra(azienda, profilo, 3, form);

        return await SalvaEAvanza(azienda, profilo, 3);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Posizioni(Guid? aziendaId, PosizioniForm form)
    {
        var (azienda, profilo) = await Carica(aziendaId);
        if (azienda is null) return NotFound();
        if (!ModelState.IsValid
            || !Prova(() => profilo.ImpostaPosizioni(new PosizioniLavoro(form.Ccnl!, form.MatricolaInps!, form.InailCodiceDitta!, form.InailPat!,
                form.CassaEdile, form.EnteBilaterale, form.FondoPensione, form.GiorniLavorativiSettimana, form.OreSettimanali))))
            return Mostra(azienda, profilo, 4, form);

        return await SalvaEAvanza(azienda, profilo, 4);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Paghe(Guid? aziendaId, PagheForm form)
    {
        var (azienda, profilo) = await Carica(aziendaId);
        if (azienda is null) return NotFound();
        if (!ModelState.IsValid) return Mostra(azienda, profilo, 5, form);

        Iban? iban = null;
        if ((!string.IsNullOrWhiteSpace(form.Iban) && !Prova(() => iban = Iban.Parse(form.Iban), nameof(form.Iban)))
            || !Prova(() => profilo.ImpostaPaghe(new DatiPaghe(iban, form.GiornoPagamento, form.ReferenteNome!, form.ReferenteEmail!,
                form.ReferenteTelefono, form.NoteStudio))))
            return Mostra(azienda, profilo, 5, form);

        return await SalvaEAvanza(azienda, profilo, 5);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Conferma(Guid? aziendaId, bool privacy)
    {
        var (azienda, profilo) = await Carica(aziendaId);
        if (azienda is null) return NotFound();
        var primaVolta = !profilo.Completato;
        if (!Prova(() => profilo.Completa(privacy, utente.Email, clock.GetUtcNow().UtcDateTime)))
            return Mostra(azienda, profilo, ProfiloAzienda.UltimoPasso, null);

        await Salva(profilo);
        if (primaVolta) await AvvisaStudio(azienda, profilo);

        if (utente.IsAzienda && await signIn.UserManager.GetUserAsync(User) is { } io)
            await signIn.RefreshSignInAsync(io); // toglie il blocco del percorso guidato dalla sessione
        Conferma(primaVolta ? "Grazie! Il profilo dell'azienda è completo e lo studio è stato avvisato." : "Profilo aggiornato.");
        return utente.IsConsulente ? RedirectToAction("Dettaglio", "Aziende", new { id = azienda.Id }) : RedirectToAction("Index", "Home");
    }

    private async Task<(Azienda? Azienda, ProfiloAzienda Profilo)> Carica(Guid? aziendaId)
    {
        var id = utente.IsAzienda ? utente.AziendaId : aziendaId;
        var azienda = id is { } a ? await db.Aziende.FirstOrDefaultAsync(x => x.Id == a) : null;
        if (azienda is null) return (null, null!);
        var profilo = await db.ProfiliAzienda.FirstOrDefaultAsync(p => p.AziendaId == azienda.Id) ?? new ProfiloAzienda(azienda.Id);
        return (azienda, profilo);
    }

    private async Task Salva(ProfiloAzienda profilo)
    {
        if (db.Entry(profilo).State == EntityState.Detached) db.ProfiliAzienda.Add(profilo);
        await db.SaveChangesAsync();
    }

    private async Task<IActionResult> SalvaEAvanza(Azienda azienda, ProfiloAzienda profilo, int passo)
    {
        await Salva(profilo);
        // Dopo il completamento una modifica riporta al riepilogo; durante il percorso si va al passo successivo.
        var prossimo = profilo.Completato ? ProfiloAzienda.UltimoPasso : passo + 1;
        return RedirectToAction(nameof(Index), new { aziendaId = utente.IsConsulente ? azienda.Id : (Guid?)null, passo = prossimo });
    }

    private ViewResult Mostra(Azienda azienda, ProfiloAzienda profilo, int passo, object? form) =>
        View("Index", new OnboardingViewModel
        {
            AziendaId = azienda.Id, Azienda = azienda, Profilo = profilo, Passo = passo, Form = form,
            PerContoAzienda = utente.IsConsulente,
        });

    private static object? FormDi(int passo, Azienda a, ProfiloAzienda p) => passo switch
    {
        1 => new SocietariForm
        {
            RagioneSociale = a.RagioneSociale, CodiceFiscale = a.CodiceFiscale, Email = a.Email, FormaGiuridica = p.FormaGiuridica,
            NumeroRea = p.NumeroRea, CodiceAteco = p.CodiceAteco, DataInizioAttivita = p.DataInizioAttivita, Pec = p.Pec, Telefono = p.Telefono,
            HaLogo = a.LogoChiave is not null,
        },
        2 => new SediForm
        {
            LegaleVia = p.SedeLegaleVia, LegaleCap = p.SedeLegaleCap, LegaleComune = p.SedeLegaleComune, LegaleProvincia = p.SedeLegaleProvincia,
            OperativaDiversa = p.SedeOperativaVia is not null, OperativaVia = p.SedeOperativaVia, OperativaCap = p.SedeOperativaCap,
            OperativaComune = p.SedeOperativaComune, OperativaProvincia = p.SedeOperativaProvincia,
        },
        3 => new LegaleForm
        {
            Nome = p.LegaleNome, Cognome = p.LegaleCognome, CodiceFiscale = p.LegaleCodiceFiscale?.Valore, Email = p.LegaleEmail, Telefono = p.LegaleTelefono,
        },
        4 => new PosizioniForm
        {
            Ccnl = p.Ccnl, MatricolaInps = p.MatricolaInps, InailCodiceDitta = p.InailCodiceDitta, InailPat = p.InailPat,
            CassaEdile = p.CassaEdile, EnteBilaterale = p.EnteBilaterale, FondoPensione = p.FondoPensione,
            GiorniLavorativiSettimana = p.GiorniLavorativiSettimana ?? 5, OreSettimanali = p.OreSettimanali ?? 40,
        },
        5 => new PagheForm
        {
            Iban = p.IbanAddebito?.Valore, GiornoPagamento = p.GiornoPagamento, ReferenteNome = p.ReferenteNome,
            ReferenteEmail = p.ReferenteEmail, ReferenteTelefono = p.ReferenteTelefono, NoteStudio = p.NoteStudio,
        },
        _ => null,
    };

    private async Task AvvisaStudio(Azienda azienda, ProfiloAzienda profilo)
    {
        var consulenti = await (
            from u in db.Users
            join ur in db.UserRoles on u.Id equals ur.UserId
            join r in db.Roles on ur.RoleId equals r.Id
            where r.Name == Ruoli.Consulente && u.Attivo && u.EmailConfirmed
            select u.Email).ToListAsync();
        var link = WebUtility.HtmlEncode(accessi.ComponiUrl($"/Aziende/Dettaglio/{azienda.Id}"));
        var nome = WebUtility.HtmlEncode(azienda.RagioneSociale);
        foreach (var email in consulenti.OfType<string>())
            coda.Accoda(new MessaggioEmail(email, $"Profilo completato: {azienda.RagioneSociale}",
                $"""
                <p>{nome} ha completato il profilo per lo studio (compilato da {WebUtility.HtmlEncode(profilo.CompletatoDa)}).</p>
                <p>Dati societari, sedi, legale rappresentante, posizioni INPS/INAIL e referente paghe sono nella scheda dell'azienda:</p>
                <p><a href="{link}">{link}</a></p>
                <p>Consulens HR</p>
                """));
    }
}
