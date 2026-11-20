using System.ComponentModel.DataAnnotations;
using System.Net;
using CL.Domain.Aziende;
using CL.Domain.Common;
using CL.Domain.Dipendenti;
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

public sealed class AnagraficaForm
{
    [Required(ErrorMessage = "Inserisci il comune di nascita."), StringLength(150), Display(Name = "Comune di nascita (o Stato estero)")]
    public string? ComuneNascita { get; set; }
    [Required(ErrorMessage = "Inserisci la provincia."), RegularExpression(@"^[A-Za-z]{2}$", ErrorMessage = "Sigla di 2 lettere (EE se all'estero)."), Display(Name = "Provincia")]
    public string? ProvinciaNascita { get; set; }
    [Required(ErrorMessage = "Inserisci la cittadinanza."), StringLength(150), Display(Name = "Cittadinanza")]
    public string? Cittadinanza { get; set; } = "Italiana";
    [StringLength(30), Display(Name = "Cellulare")]
    public string? Telefono { get; set; }
    [EmailAddress(ErrorMessage = "Email non valida."), StringLength(256), Display(Name = "Email personale (facoltativa)")]
    public string? EmailPersonale { get; set; }

    [Display(Name = "Genere (facoltativo)")]
    public Genere? Genere { get; set; }
    [Display(Name = "Pronomi (facoltativi)")]
    public Pronomi? Pronomi { get; set; }

    [Display(Name = "La tua foto (facoltativa: PNG, JPEG o WebP, massimo 2 MB)")]
    public IFormFile? Foto { get; set; }
    public bool HaFoto { get; set; }
}

public sealed class ResidenzaForm
{
    [Required(ErrorMessage = "Inserisci l'indirizzo."), StringLength(300), Display(Name = "Indirizzo")]
    public string? Via { get; set; }
    [Required(ErrorMessage = "Inserisci il CAP."), RegularExpression(@"^\d{5}$", ErrorMessage = "CAP di 5 cifre."), Display(Name = "CAP")]
    public string? Cap { get; set; }
    [Required(ErrorMessage = "Inserisci il comune."), StringLength(150), Display(Name = "Comune")]
    public string? Comune { get; set; }
    [Required(ErrorMessage = "Inserisci la provincia."), RegularExpression(@"^[A-Za-z]{2}$", ErrorMessage = "Sigla di 2 lettere (es. MI)."), Display(Name = "Provincia")]
    public string? Provincia { get; set; }

    [Display(Name = "Il domicilio è diverso dalla residenza")]
    public bool DomicilioDiverso { get; set; }
    [StringLength(300), Display(Name = "Indirizzo")]
    public string? DomicilioVia { get; set; }
    [RegularExpression(@"^\d{5}$", ErrorMessage = "CAP di 5 cifre."), Display(Name = "CAP")]
    public string? DomicilioCap { get; set; }
    [StringLength(150), Display(Name = "Comune")]
    public string? DomicilioComune { get; set; }
    [RegularExpression(@"^[A-Za-z]{2}$", ErrorMessage = "Sigla di 2 lettere."), Display(Name = "Provincia")]
    public string? DomicilioProvincia { get; set; }
}

public sealed class PagamentoForm
{
    [Required(ErrorMessage = "Inserisci l'IBAN."), Display(Name = "IBAN del conto su cui ricevere lo stipendio")]
    public string? Iban { get; set; }
    [StringLength(150), Display(Name = "Intestatario del conto (se non sei tu)")]
    public string? IntestatarioConto { get; set; }
    [Display(Name = "Destinazione del TFR")]
    public DestinazioneTfr Tfr { get; set; } = DestinazioneTfr.Azienda;
    [StringLength(150), Display(Name = "Fondo pensione")]
    public string? FondoPensione { get; set; }
}

public sealed class FamiliareForm
{
    public RelazioneFamiliare Relazione { get; set; } = RelazioneFamiliare.Figlio;
    public string? Nome { get; set; }
    public string? Cognome { get; set; }
    public string? CodiceFiscale { get; set; }
    public DateOnly? DataNascita { get; set; }
    public int PercentualeCarico { get; set; } = 100;
    public bool Disabile { get; set; }

    public bool Vuoto => string.IsNullOrWhiteSpace(Nome) && string.IsNullOrWhiteSpace(Cognome) && string.IsNullOrWhiteSpace(CodiceFiscale) && DataNascita is null;
}

public sealed class FamigliaForm
{
    [Display(Name = "Chiedo che mi siano applicate le detrazioni per lavoro dipendente")]
    public bool RichiedeDetrazioni { get; set; } = true;
    public List<FamiliareForm> Familiari { get; set; } = [];
}

public sealed class AltriDatiForm
{
    [Display(Name = "Titolo di studio")]
    public TitoloStudio TitoloStudio { get; set; } = TitoloStudio.Diploma;
    [Required(ErrorMessage = "Scegli il documento."), Display(Name = "Documento d'identità")]
    public string? TipoDocumento { get; set; } = "Carta d'identità";
    [Required(ErrorMessage = "Inserisci il numero."), StringLength(30), Display(Name = "Numero")]
    public string? NumeroDocumento { get; set; }
    [Required(ErrorMessage = "Inserisci la scadenza."), Display(Name = "Scadenza")]
    public DateOnly? ScadenzaDocumento { get; set; }
    [Display(Name = "Scadenza del permesso di soggiorno (se previsto)")]
    public DateOnly? ScadenzaPermessoSoggiorno { get; set; }

    public static readonly string[] Documenti = ["Carta d'identità", "Passaporto", "Patente di guida"];
}

/// <summary>Tutti i dati per le paghe in un solo modulo: si modificano dalla scheda della persona.</summary>
public sealed class DatiPagheForm
{
    public AnagraficaForm Anagrafica { get; set; } = new();
    public ResidenzaForm Residenza { get; set; } = new();
    public PagamentoForm Pagamento { get; set; } = new();
    public FamigliaForm Famiglia { get; set; } = new();
    public AltriDatiForm Altri { get; set; } = new();

    public static DatiPagheForm Da(Dipendente d, ProfiloDipendente p) => new()
    {
        Anagrafica = (AnagraficaForm)DatiPersonaliController.FormDi(1, d, p)!,
        Residenza = (ResidenzaForm)DatiPersonaliController.FormDi(2, d, p)!,
        Pagamento = (PagamentoForm)DatiPersonaliController.FormDi(3, d, p)!,
        Famiglia = (FamigliaForm)DatiPersonaliController.FormDi(4, d, p)!,
        Altri = (AltriDatiForm)DatiPersonaliController.FormDi(5, d, p)!,
    };
}

#endregion

public sealed class DatiPersonaliViewModel
{
    public required Dipendente Dipendente { get; init; }
    public required ProfiloDipendente Profilo { get; init; }
    public required int Passo { get; init; }
    public object? Form { get; init; }
    /// <summary>Studio o referente che consulta/compila per conto del dipendente.</summary>
    public bool PerConto { get; init; }

    public static readonly string[] Titoli = ["Dati anagrafici", "Residenza", "Stipendio e TFR", "Detrazioni e familiari", "Altri dati", "Riepilogo"];
}

/// <summary>
/// Dati personali del dipendente per le paghe: percorso guidato obbligatorio al primo accesso,
/// poi consultabile e aggiornabile. Studio e referente dell'azienda li vedono e possono compilarli per conto del dipendente.
/// </summary>
[Authorize]
public sealed class DatiPersonaliController(
    AppDbContext db, UtenteCorrente utente, SignInManager<ApplicationUser> signIn, CodaEmail coda,
    ServizioAccessi accessi, FotoDipendenti foto, TimeProvider clock) : AppController
{
    private DateTime Adesso => clock.GetUtcNow().UtcDateTime;

    public async Task<IActionResult> Index(Guid? dipendenteId, int? passo)
    {
        var (dip, profilo) = await Carica(dipendenteId);
        if (dip is null) return utente.IsDipendente ? NotFound() : RedirectToAction("Index", "Dipendenti");
        var p = Math.Clamp(passo ?? profilo.Passo, 1, profilo.Passo);
        return Mostra(dip, profilo, p, FormDi(p, dip, profilo));
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(Immagini.DimensioneMassima + 256 * 1024)]
    public async Task<IActionResult> Anagrafica(Guid? dipendenteId, AnagraficaForm form)
    {
        var (dip, profilo) = await Carica(dipendenteId);
        if (dip is null) return NotFound();
        if (!ModelState.IsValid
            || !Prova(() => profilo.ImpostaAnagrafica(new DatiAnagrafici(form.ComuneNascita!, form.ProvinciaNascita!, form.Cittadinanza!,
                form.Telefono, form.EmailPersonale), utente.Email, Adesso)))
            return Mostra(dip, profilo, 1, form);
        dip.AggiornaRecapiti(dip.Email, form.Telefono ?? dip.Telefono);
        dip.ImpostaIdentita(form.Genere, form.Pronomi);
        if (form.Foto is { Length: > 0 } file && await foto.SalvaAsync(dip, file) is { } errore)
        {
            ModelState.AddModelError(nameof(form.Foto), errore);
            return Mostra(dip, profilo, 1, form);
        }
        return await SalvaEAvanza(dip, profilo, 1);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Residenza(Guid? dipendenteId, ResidenzaForm form)
    {
        var (dip, profilo) = await Carica(dipendenteId);
        if (dip is null) return NotFound();
        if (form.DomicilioDiverso)
            foreach (var (campo, valore, msg) in new[] { (nameof(form.DomicilioVia), form.DomicilioVia, "Inserisci l'indirizzo."),
                         (nameof(form.DomicilioCap), form.DomicilioCap, "Inserisci il CAP."), (nameof(form.DomicilioComune), form.DomicilioComune, "Inserisci il comune."),
                         (nameof(form.DomicilioProvincia), form.DomicilioProvincia, "Inserisci la provincia.") })
                if (string.IsNullOrWhiteSpace(valore)) ModelState.AddModelError(campo, msg);
        if (!ModelState.IsValid) return Mostra(dip, profilo, 2, form);

        var residenza = new Indirizzo(form.Via!, form.Cap!, form.Comune!, form.Provincia!);
        var domicilio = form.DomicilioDiverso ? new Indirizzo(form.DomicilioVia!, form.DomicilioCap!, form.DomicilioComune!, form.DomicilioProvincia!) : null;
        if (!Prova(() => profilo.ImpostaResidenza(residenza, domicilio, utente.Email, Adesso))) return Mostra(dip, profilo, 2, form);
        dip.ImpostaResidenza(profilo.Residenza);
        return await SalvaEAvanza(dip, profilo, 2);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Pagamento(Guid? dipendenteId, PagamentoForm form)
    {
        var (dip, profilo) = await Carica(dipendenteId);
        if (dip is null) return NotFound();
        if (!ModelState.IsValid) return Mostra(dip, profilo, 3, form);
        Iban? iban = null;
        if (!Prova(() => iban = Iban.Parse(form.Iban!), nameof(form.Iban))
            || !Prova(() => profilo.ImpostaPagamento(iban!, form.IntestatarioConto, form.Tfr, form.FondoPensione, utente.Email, Adesso), nameof(form.FondoPensione)))
            return Mostra(dip, profilo, 3, form);
        dip.ImpostaIban(iban); // l'IBAN dell'anagrafica è quello usato dal consulente
        return await SalvaEAvanza(dip, profilo, 3);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Famiglia(Guid? dipendenteId, FamigliaForm form)
    {
        var (dip, profilo) = await Carica(dipendenteId);
        if (dip is null) return NotFound();

        var familiari = new List<DatiFamiliare>();
        for (var i = 0; i < form.Familiari.Count; i++)
        {
            var f = form.Familiari[i];
            if (f.Vuoto) continue;
            var prefisso = $"Familiari[{i}].";
            if (string.IsNullOrWhiteSpace(f.Nome) || string.IsNullOrWhiteSpace(f.Cognome))
                ModelState.AddModelError(prefisso + nameof(f.Nome), "Nome e cognome del familiare.");
            if (f.DataNascita is null) ModelState.AddModelError(prefisso + nameof(f.DataNascita), "Data di nascita.");
            CodiceFiscale? cf = null;
            try { cf = CodiceFiscale.Parse(f.CodiceFiscale ?? ""); }
            catch (DomainException ex) { ModelState.AddModelError(prefisso + nameof(f.CodiceFiscale), ex.Message); }
            if (cf is not null && f.DataNascita is { } nascita && !string.IsNullOrWhiteSpace(f.Nome) && !string.IsNullOrWhiteSpace(f.Cognome))
                familiari.Add(new DatiFamiliare(f.Relazione, f.Nome, f.Cognome, cf, nascita, f.PercentualeCarico, f.Disabile));
        }
        if (!ModelState.IsValid
            || !Prova(() => profilo.ImpostaFamiglia(form.RichiedeDetrazioni, familiari, utente.Email, Adesso)))
            return Mostra(dip, profilo, 4, form);
        return await SalvaEAvanza(dip, profilo, 4);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AltriDati(Guid? dipendenteId, AltriDatiForm form)
    {
        var (dip, profilo) = await Carica(dipendenteId);
        if (dip is null) return NotFound();
        if (form.TipoDocumento is not null && !AltriDatiForm.Documenti.Contains(form.TipoDocumento))
            ModelState.AddModelError(nameof(form.TipoDocumento), "Scegli una voce dell'elenco.");
        if (form.ScadenzaDocumento is { } s && s < DateOnly.FromDateTime(clock.GetLocalNow().DateTime))
            ModelState.AddModelError(nameof(form.ScadenzaDocumento), "Il documento è scaduto: indica quello valido.");
        if (!ModelState.IsValid
            || !Prova(() => profilo.ImpostaAltriDati(new AltriDati(form.TitoloStudio, form.TipoDocumento!, form.NumeroDocumento!,
                form.ScadenzaDocumento!.Value, form.ScadenzaPermessoSoggiorno), utente.Email, Adesso)))
            return Mostra(dip, profilo, 5, form);
        return await SalvaEAvanza(dip, profilo, 5);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Conferma(Guid? dipendenteId, bool dichiarazione)
    {
        var (dip, profilo) = await Carica(dipendenteId);
        if (dip is null) return NotFound();
        var primaVolta = !profilo.Completato;
        if (!Prova(() => profilo.Completa(dichiarazione, utente.Email, Adesso)))
            return Mostra(dip, profilo, ProfiloDipendente.UltimoPasso, null);

        await Salva(profilo);
        if (primaVolta) await Avvisa(dip, "ha completato i dati personali per le paghe");
        if (utente.IsDipendente && await signIn.UserManager.GetUserAsync(User) is { } io)
            await signIn.RefreshSignInAsync(io); // toglie il blocco del percorso guidato
        Conferma(primaVolta ? "Grazie! I tuoi dati sono stati inviati al consulente." : "Dati aggiornati.");
        return utente.IsDipendente ? RedirectToAction("Index", "Home") : RedirectToAction("Dettaglio", "Dipendenti", new { id = dip.Id });
    }

    private async Task<(Dipendente? Dipendente, ProfiloDipendente Profilo)> Carica(Guid? dipendenteId)
    {
        var id = utente.IsDipendente ? utente.DipendenteId : dipendenteId;
        if (id is null) return (null, null!);
        var dip = await utente.Filtra(db.Dipendenti).FirstOrDefaultAsync(d => d.Id == id);
        if (dip is null) return (null, null!);
        var profilo = await db.ProfiliDipendente.FirstOrDefaultAsync(p => p.DipendenteId == dip.Id) ?? new ProfiloDipendente(dip.Id);
        return (dip, profilo);
    }

    private async Task Salva(ProfiloDipendente profilo)
    {
        if (db.Entry(profilo).State == EntityState.Detached) db.ProfiliDipendente.Add(profilo);
        await db.SaveChangesAsync();
    }

    private async Task<IActionResult> SalvaEAvanza(Dipendente dip, ProfiloDipendente profilo, int passo)
    {
        await Salva(profilo);
        if (profilo.Completato)
        {
            // Modifica dopo il completamento: il consulente deve saperlo per aggiornare le paghe.
            if (utente.IsDipendente) await Avvisa(dip, $"ha aggiornato i dati personali ({DatiPersonaliViewModel.Titoli[passo - 1].ToLower()})");
            Conferma("Modifica salvata" + (utente.IsDipendente ? ": il consulente è stato avvisato." : "."));
        }
        var prossimo = profilo.Completato ? ProfiloDipendente.UltimoPasso : passo + 1;
        return RedirectToAction(nameof(Index), new { dipendenteId = utente.IsDipendente ? (Guid?)null : dip.Id, passo = prossimo });
    }

    private ViewResult Mostra(Dipendente dip, ProfiloDipendente profilo, int passo, object? form) =>
        View("Index", new DatiPersonaliViewModel { Dipendente = dip, Profilo = profilo, Passo = passo, Form = form, PerConto = !utente.IsDipendente });

    internal static object? FormDi(int passo, Dipendente d, ProfiloDipendente p) => passo switch
    {
        1 => new AnagraficaForm
        {
            ComuneNascita = p.ComuneNascita, ProvinciaNascita = p.ProvinciaNascita, Cittadinanza = p.Cittadinanza ?? "Italiana",
            Telefono = p.Telefono ?? d.Telefono, EmailPersonale = p.EmailPersonale, HaFoto = d.FotoChiave is not null,
            Genere = d.Genere, Pronomi = d.Pronomi,
        },
        2 => new ResidenzaForm
        {
            Via = p.ResidenzaVia, Cap = p.ResidenzaCap, Comune = p.ResidenzaComune, Provincia = p.ResidenzaProvincia,
            DomicilioDiverso = p.DomicilioVia is not null, DomicilioVia = p.DomicilioVia, DomicilioCap = p.DomicilioCap,
            DomicilioComune = p.DomicilioComune, DomicilioProvincia = p.DomicilioProvincia,
        },
        3 => new PagamentoForm
        {
            Iban = (p.Iban ?? d.Iban)?.Valore, IntestatarioConto = p.IntestatarioConto, Tfr = p.Tfr ?? DestinazioneTfr.Azienda, FondoPensione = p.FondoPensione,
        },
        4 => new FamigliaForm
        {
            RichiedeDetrazioni = p.RichiedeDetrazioni ?? true,
            Familiari = p.Familiari.OrderBy(f => f.Relazione).ThenBy(f => f.DataNascita).Select(f => new FamiliareForm
            {
                Relazione = f.Relazione, Nome = f.Nome, Cognome = f.Cognome, CodiceFiscale = f.CodiceFiscale.Valore,
                DataNascita = f.DataNascita, PercentualeCarico = f.PercentualeCarico, Disabile = f.Disabile,
            }).ToList(),
        },
        5 => new AltriDatiForm
        {
            TitoloStudio = p.TitoloStudio ?? TitoloStudio.Diploma, TipoDocumento = p.TipoDocumento ?? "Carta d'identità",
            NumeroDocumento = p.NumeroDocumento, ScadenzaDocumento = p.ScadenzaDocumento, ScadenzaPermessoSoggiorno = p.ScadenzaPermessoSoggiorno,
        },
        _ => null,
    };

    /// <summary>Avviso al consulente e ai referenti dell'azienda (in background).</summary>
    private async Task Avvisa(Dipendente dip, string cosa)
    {
        var destinatari = await (
            from u in db.Users
            join ur in db.UserRoles on u.Id equals ur.UserId
            join r in db.Roles on ur.RoleId equals r.Id
            where u.Attivo && u.EmailConfirmed && (r.Name == Ruoli.Consulente || (r.Name == Ruoli.Azienda && u.AziendaId == dip.AziendaId))
            select u.Email).Distinct().ToListAsync();
        var link = WebUtility.HtmlEncode(accessi.ComponiUrl($"/DatiPersonali?dipendenteId={dip.Id}"));
        var chi = WebUtility.HtmlEncode(dip.NomeCompleto);
        foreach (var email in destinatari.OfType<string>())
            coda.Accoda(new MessaggioEmail(email, $"{dip.NomeCompleto} {cosa}",
                $"""<p>{chi} {WebUtility.HtmlEncode(cosa)}.</p><p><a href="{link}">{link}</a></p><p>Consulens HR</p>"""));
    }

    public static IEnumerable<SelectListItem> Generi =>
        Enum.GetValues<Genere>().Select(g => new SelectListItem(g.Descrizione(), g.ToString()));
    public static IEnumerable<SelectListItem> ElencoPronomi =>
        Enum.GetValues<Pronomi>().Select(p => new SelectListItem(p.Descrizione(), p.ToString()));

    public static IEnumerable<SelectListItem> Titoli =>
        Enum.GetValues<TitoloStudio>().Select(t => new SelectListItem(t.Descrizione(), t.ToString()));
}
