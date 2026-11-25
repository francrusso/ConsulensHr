using System.ComponentModel.DataAnnotations;
using CL.Domain.Aziende;
using CL.Domain.Notifiche;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

#region Form dei passi

public sealed class SocietariForm
{
    /// <summary>Nuova attività o attività già operativa (che arriva da un altro consulente).</summary>
    public TipoAvvio TipoAvvio { get; set; } = TipoAvvio.NuovaAttivita;

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

    /// <summary>Unità locali (sedi in cui lavora il personale) con il loro referente; le righe vuote si ignorano.</summary>
    public List<UnitaForm> Unita { get; set; } = [];
}

public sealed class UnitaForm
{
    public Guid? Id { get; set; }
    public string? Nome { get; set; }
    public string? Via { get; set; }
    public string? Cap { get; set; }
    public string? Comune { get; set; }
    public string? Provincia { get; set; }
    public string? ReferenteNome { get; set; }
    public string? ReferenteEmail { get; set; }
    public string? ReferenteTelefono { get; set; }
    public bool Vuota => new[] { Nome, Via, Cap, Comune, Provincia, ReferenteNome, ReferenteEmail, ReferenteTelefono }.All(string.IsNullOrWhiteSpace);
}

public sealed class TitolareForm
{
    public string? Nome { get; set; }
    public string? Cognome { get; set; }
    public string? CodiceFiscale { get; set; }
    public decimal? Quota { get; set; }
    public bool Vuota => new[] { Nome, Cognome, CodiceFiscale }.All(string.IsNullOrWhiteSpace) && Quota is null;
}

public sealed class VoceInailForm
{
    public string? Voce { get; set; }
    public decimal? Tasso { get; set; }
    public bool Vuota => string.IsNullOrWhiteSpace(Voce) && Tasso is null;
}

public sealed class SubentroForm
{
    [Required(ErrorMessage = "Indica il consulente precedente."), StringLength(200), Display(Name = "Consulente o studio precedente")]
    public string? ConsulentePrecedente { get; set; }
    [StringLength(300), Display(Name = "Contatti (email, telefono)")]
    public string? ContattiPrecedente { get; set; }
    [Required(ErrorMessage = "Indica da quando passate al nuovo consulente."), Display(Name = "Data del cambio")]
    public DateOnly? DataSubentro { get; set; }
    /// <summary>Mese nel formato yyyy-MM (campo "month").</summary>
    [Display(Name = "Ultimo mese elaborato dal consulente precedente")]
    public string? UltimoMeseElaborato { get; set; }
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
    [Display(Name = "Il legale rappresentante è anche l'unico titolare effettivo")]
    public bool LegaleUnicoTitolare { get; set; } = true;
    public List<TitolareForm> Titolari { get; set; } = [];
}

public sealed class PosizioniForm
{
    [Display(Name = "L'azienda non ha ancora contratti attivi")]
    public bool NessunContratto { get; set; }
    [RegularExpression(@"^\d{5}$", ErrorMessage = "Il CSC è di 5 cifre."), Display(Name = "CSC (codice statistico contributivo)")]
    public string? Csc { get; set; }
    [StringLength(100), Display(Name = "Codici di autorizzazione (CA)")]
    public string? CodiciAutorizzazione { get; set; }
    [StringLength(500), Display(Name = "Contratti integrativi o aziendali")]
    public string? ContrattiIntegrativi { get; set; }
    public List<VoceInailForm> VociInail { get; set; } = [];
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
    [StringLength(2000), Display(Name = "Note per il consulente")]
    public string? NoteStudio { get; set; }
    public List<ReferenteForm> Referenti { get; set; } = [];
}

public sealed class ReferenteForm
{
    public AmbitoReferente Ambito { get; set; }
    public string? Nome { get; set; }
    public string? Ruolo { get; set; }
    public string? Email { get; set; }
    public string? Telefono { get; set; }
    public bool Vuota => new[] { Nome, Ruolo, Email, Telefono }.All(string.IsNullOrWhiteSpace);
}

#endregion

public sealed record ReferentePagheRiga(Guid Id, string Nome, string? Email, string? Telefono);

public sealed class OnboardingViewModel
{
    public required Guid AziendaId { get; init; }
    public required Azienda Azienda { get; init; }
    public required ProfiloAzienda Profilo { get; init; }
    public required int Passo { get; init; }
    public object? Form { get; init; }
    /// <summary>Il consulente che compila per conto dell'azienda (link e testi diversi).</summary>
    public bool PerContoAzienda { get; init; }
    public ReferentePagheRiga? ReferentePaghe { get; init; }

    public static readonly string[] Titoli = ["Dati societari", "Sedi e unità locali", "Legale rappresentante e titolari", "Contratto e posizioni", "Paghe e referenti", "Cambio consulente", "Riepilogo"];
}

/// <summary>
/// Percorso guidato con cui l'azienda fornisce al consulente i dati per gestire le pratiche di lavoro.
/// Obbligatorio per il referente al primo accesso (vedi <see cref="FiltroOnboarding"/>); il consulente può consultarlo
/// e compilarlo per conto dell'azienda. Passi: 1 dati societari, 2 sedi, 3 legale rappresentante, 4 contratto e
/// posizioni INPS/INAIL, 5 paghe, 6 riepilogo e conferma. Ogni passo si salva da sé; a profilo completo i passi
/// diventano schede modificabili, più la 7 (tipi di richiesta offerti ai dipendenti).
/// </summary>
[Authorize(Policy = Policy.GestionePersonale)]
public sealed class OnboardingController(
    AppDbContext db, UtenteCorrente utente, SignInManager<ApplicationUser> signIn, ServizioNotifiche notifiche,
    LoghiAziende loghi, TimeProvider clock) : AppController
{
    /// <summary>Scheda "Tipi di richiesta" del profilo completo.</summary>
    public const int PassoRichieste = 8;

    public async Task<IActionResult> Index(Guid? aziendaId, int? passo)
    {
        var (azienda, profilo) = await Carica(aziendaId);
        if (azienda is null) return utente.IsConsulente ? RedirectToAction("Index", "Aziende") : NotFound();

        // Profilo completo: sezioni a schede (si parte dai dati societari).
        // Durante il percorso guidato si può tornare ai passi già fatti, non saltare in avanti.
        var p = profilo.Completato ? Math.Clamp(passo ?? 1, 1, PassoRichieste) : Math.Clamp(passo ?? profilo.Passo, 1, profilo.Passo);
        return await Mostra(azienda, profilo, p, FormDi(p, azienda, profilo));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Societari(Guid? aziendaId, SocietariForm form)
    {
        var (azienda, profilo) = await Carica(aziendaId);
        if (azienda is null) return NotFound();
        if (form.FormaGiuridica is not null && !SocietariForm.Forme.Contains(form.FormaGiuridica))
            ModelState.AddModelError(nameof(form.FormaGiuridica), "Scegli una voce dell'elenco.");
        if (!ModelState.IsValid) return await Mostra(azienda, profilo, 1, form);

        if (!Prova(() =>
            {
                azienda.AggiornaAnagrafica(form.RagioneSociale, form.CodiceFiscale, azienda.Indirizzo, azienda.Cap, azienda.Comune, azienda.Provincia,
                    string.IsNullOrWhiteSpace(form.Email) ? azienda.Email : form.Email);
                profilo.ImpostaDatiSocietari(new DatiSocietari(form.FormaGiuridica!, form.NumeroRea, form.CodiceAteco!,
                    form.DataInizioAttivita, form.Pec!, form.Telefono));
                azienda.ImpostaTipoAvvio(form.TipoAvvio);
                profilo.ImpostaSubentro(form.TipoAvvio == TipoAvvio.CambioConsulente);
            })) return await Mostra(azienda, profilo, 1, form);

        return await SalvaEAvanza(azienda, profilo, 1);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Sedi(Guid? aziendaId, SediForm form)
    {
        var (azienda, profilo) = await Carica(aziendaId);
        if (azienda is null) return NotFound();
        form.Unita = form.Unita.Where(u => !u.Vuota).ToList();
        if (!ModelState.IsValid) return await Mostra(azienda, profilo, 2, form);

        var legale = new Indirizzo(form.LegaleVia!, form.LegaleCap!, form.LegaleComune!, form.LegaleProvincia!);
        if (!Prova(() =>
            {
                var unita = form.Unita.Select(u => new UnitaLocale(u.Id, u.Nome, new Indirizzo(u.Via ?? "", u.Cap ?? "", u.Comune ?? "", u.Provincia ?? ""),
                    u.ReferenteNome, u.ReferenteEmail, u.ReferenteTelefono)).ToList();
                profilo.ImpostaSedi(legale, unita);
                // L'indirizzo dell'anagrafica è quello dove si lavora (la prima unità locale, altrimenti la sede legale).
                var dove = profilo.UnitaLocali.FirstOrDefault()?.Indirizzo ?? profilo.SedeLegale!;
                azienda.AggiornaAnagrafica(azienda.RagioneSociale, azienda.CodiceFiscale, dove.Via, dove.Cap, dove.Comune, dove.Provincia, azienda.Email);
            })) return await Mostra(azienda, profilo, 2, form);

        return await SalvaEAvanza(azienda, profilo, 2);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Legale(Guid? aziendaId, LegaleForm form)
    {
        var (azienda, profilo) = await Carica(aziendaId);
        if (azienda is null) return NotFound();
        if (!ModelState.IsValid) return await Mostra(azienda, profilo, 3, form);

        CodiceFiscale? cf = null;
        if (!Prova(() => cf = CodiceFiscale.Parse(form.CodiceFiscale!), nameof(form.CodiceFiscale))
            || !Prova(() => profilo.ImpostaLegaleRappresentante(new LegaleRappresentante(form.Nome!, form.Cognome!, cf!, form.Email, form.Telefono))))
            return await Mostra(azienda, profilo, 3, form);
        form.Titolari = form.Titolari.Where(t => !t.Vuota).ToList();
        if (!form.LegaleUnicoTitolare && form.Titolari.Count == 0)
            ModelState.AddModelError("", "Indica almeno un titolare effettivo, o segna che è il legale rappresentante.");
        if (!ModelState.IsValid
            || !Prova(() => profilo.ImpostaTitolariEffettivi(form.LegaleUnicoTitolare,
                form.Titolari.Select(t => new TitolareEffettivo(t.Nome ?? "", t.Cognome ?? "", CodiceFiscale.Parse(t.CodiceFiscale), t.Quota)))))
            return await Mostra(azienda, profilo, 3, form);

        return await SalvaEAvanza(azienda, profilo, 3);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Posizioni(Guid? aziendaId, PosizioniForm form)
    {
        var (azienda, profilo) = await Carica(aziendaId);
        if (azienda is null) return NotFound();
        if (form.NessunContratto)
        {
            if (!Prova(profilo.ImpostaSenzaContratti)) return await Mostra(azienda, profilo, 4, form);
            return await SalvaEAvanza(azienda, profilo, 4);
        }
        if (!ModelState.IsValid
            || !Prova(() => profilo.ImpostaPosizioni(new PosizioniLavoro(form.Ccnl!, form.MatricolaInps!, form.InailCodiceDitta!, form.InailPat!,
                form.CassaEdile, form.EnteBilaterale, form.FondoPensione, form.GiorniLavorativiSettimana, form.OreSettimanali)
            {
                Csc = form.Csc, CodiciAutorizzazione = form.CodiciAutorizzazione, ContrattiIntegrativi = form.ContrattiIntegrativi,
                VociInail = form.VociInail.Where(v => !v.Vuota).Select(v => new VoceInail(v.Voce ?? "", v.Tasso ?? 0)).ToList(),
            })))
            return await Mostra(azienda, profilo, 4, form);

        return await SalvaEAvanza(azienda, profilo, 4);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Paghe(Guid? aziendaId, PagheForm form)
    {
        var (azienda, profilo) = await Carica(aziendaId);
        if (azienda is null) return NotFound();
        if (!ModelState.IsValid) return await Mostra(azienda, profilo, 5, form);

        Iban? iban = null;
        if ((!string.IsNullOrWhiteSpace(form.Iban) && !Prova(() => iban = Iban.Parse(form.Iban), nameof(form.Iban)))
            || !Prova(() => profilo.ImpostaReferenti(form.Referenti.Where(r => !r.Vuota)
                .Select(r => new ReferenteAzienda(r.Ambito, r.Nome ?? "", r.Ruolo, r.Email, r.Telefono)).ToList()))
            || !Prova(() => profilo.ImpostaPaghe(new DatiPaghe(iban, form.GiornoPagamento, form.NoteStudio))))
            return await Mostra(azienda, profilo, 5, form);

        return await SalvaEAvanza(azienda, profilo, 5);
    }

    /// <summary>Cambio consulente: chi seguiva le paghe prima, da quando e fino a che mese ha elaborato.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Subentro(Guid? aziendaId, SubentroForm form)
    {
        var (azienda, profilo) = await Carica(aziendaId);
        if (azienda is null) return NotFound();
        DateOnly? ultimo = DateOnly.TryParseExact((form.UltimoMeseElaborato ?? "") + "-01", "yyyy-MM-dd", out var u) ? u : null;
        if (!ModelState.IsValid
            || !Prova(() => profilo.ImpostaDatiSubentro(new DatiSubentro(form.ConsulentePrecedente!, form.ContattiPrecedente, form.DataSubentro!.Value, ultimo))))
            return await Mostra(azienda, profilo, ProfiloAzienda.PassoSubentro, form);
        return await SalvaEAvanza(azienda, profilo, ProfiloAzienda.PassoSubentro);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Conferma(Guid? aziendaId, bool privacy)
    {
        var (azienda, profilo) = await Carica(aziendaId);
        if (azienda is null) return NotFound();
        var primaVolta = !profilo.Completato;
        if (!Prova(() => profilo.Completa(privacy, utente.Email, clock.GetUtcNow().UtcDateTime)))
            return await Mostra(azienda, profilo, ProfiloAzienda.UltimoPasso, null);

        await Salva(profilo);
        if (primaVolta) await AvvisaStudio(azienda, profilo);

        if (utente.IsAzienda && await signIn.UserManager.GetUserAsync(User) is { } io)
            await signIn.RefreshSignInAsync(io); // toglie il blocco del percorso guidato dalla sessione
        Conferma(primaVolta ? "Grazie! Il profilo dell'azienda è completo e il consulente è stato avvisato." : "Profilo aggiornato.");
        return utente.IsConsulente ? RedirectToAction("Dettaglio", "Aziende", new { id = azienda.Id }) : RedirectToAction("Index", "Home");
    }

    /// <summary>Logo dall'intestazione del profilo: si salva subito.</summary>
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(LoghiAziende.DimensioneMassima + 256 * 1024)]
    public async Task<IActionResult> CaricaLogo(Guid? aziendaId, IFormFile? immagine, int? passo)
    {
        var (azienda, _) = await Carica(aziendaId);
        if (azienda is null) return NotFound();
        if (immagine is not { Length: > 0 }) Errore("Scegli un'immagine.");
        else if (await loghi.SalvaAsync(azienda, immagine) is { } errore) Errore(errore);
        else { await db.SaveChangesAsync(); Conferma("Logo aggiornato."); }
        return TornaAlPasso(azienda, passo);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TogliLogo(Guid? aziendaId, int? passo)
    {
        var (azienda, _) = await Carica(aziendaId);
        if (azienda is null) return NotFound();
        await loghi.RimuoviAsync(azienda);
        await db.SaveChangesAsync();
        Conferma("Logo rimosso.");
        return TornaAlPasso(azienda, passo);
    }

    /// <summary>Tipi di richiesta offerti ai dipendenti: quelli non scelti spariscono da moduli e calendari.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RichiesteAbilitate(Guid? aziendaId, List<string>? abilitate)
    {
        var (azienda, _) = await Carica(aziendaId);
        if (azienda is null) return NotFound();
        azienda.ImpostaRichiesteAbilitate(abilitate ?? []);
        await db.SaveChangesAsync();
        Conferma("Tipi di richiesta aggiornati.");
        return TornaAlPasso(azienda, PassoRichieste);
    }

    /// <summary>
    /// L'azienda su cui si lavora: per il referente sempre la sua, per il consulente quella indicata
    /// (fuori dal suo perimetro non si trova). Senza profilo salvato se ne prepara uno nuovo.
    /// </summary>
    private async Task<(Azienda? Azienda, ProfiloAzienda Profilo)> Carica(Guid? aziendaId)
    {
        var id = utente.IsAzienda ? utente.AziendaId : aziendaId;
        var azienda = id is { } a ? await db.Aziende.FirstOrDefaultAsync(x => x.Id == a) : null;
        if (azienda is null) return (null, null!);
        var profilo = await db.ProfiliAzienda.FirstOrDefaultAsync(p => p.AziendaId == azienda.Id) ?? new ProfiloAzienda(azienda.Id);
        // Nuova attività o cambio consulente (scelto quando si crea l'azienda): decide le sezioni del percorso.
        profilo.ImpostaSubentro(azienda.TipoAvvio == TipoAvvio.CambioConsulente);
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
        // Profilo completo: si resta sulla scheda appena salvata; durante il percorso si va al passo successivo.
        if (profilo.Completato) Conferma("Modifiche salvate.");
        // Il passo "Cambio consulente" c'è solo per chi arriva da un altro consulente.
        var prossimo = passo + 1 == ProfiloAzienda.PassoSubentro && !profilo.Subentro ? ProfiloAzienda.UltimoPasso : passo + 1;
        return TornaAlPasso(azienda, profilo.Completato ? passo : prossimo);
    }

    /// <summary>Il consulente lavora per conto di un'azienda: l'indirizzo la indica; il referente non ne ha bisogno.</summary>
    private RedirectToActionResult TornaAlPasso(Azienda azienda, int? passo) =>
        RedirectToAction(nameof(Index), new { aziendaId = utente.IsConsulente ? azienda.Id : (Guid?)null, passo });

    private async Task<ViewResult> Mostra(Azienda azienda, ProfiloAzienda profilo, int passo, object? form) =>
        View("Index", new OnboardingViewModel
        {
            AziendaId = azienda.Id, Azienda = azienda, Profilo = profilo, Passo = passo, Form = form,
            PerContoAzienda = utente.IsConsulente,
            // Referente paghe (serve alla scheda Paghe e al riepilogo): la persona segnata come tale nella sua scheda del personale.
            ReferentePaghe = passo is 5 or ProfiloAzienda.UltimoPasso
                ? await db.Dipendenti.AsNoTracking().IgnoreAutoIncludes().Where(d => d.AziendaId == azienda.Id && d.ReferentePaghe)
                    .Select(d => new ReferentePagheRiga(d.Id, d.Cognome + " " + d.Nome, d.Email, d.Telefono)).FirstOrDefaultAsync()
                : null,
        });

    /// <summary>Il form del passo precompilato con i dati salvati (null per riepilogo e tipi di richiesta, che non ne hanno).</summary>
    private static object? FormDi(int passo, Azienda a, ProfiloAzienda p) => passo switch
    {
        1 => new SocietariForm
        {
            TipoAvvio = a.TipoAvvio,
            RagioneSociale = a.RagioneSociale, CodiceFiscale = a.CodiceFiscale, Email = a.Email, FormaGiuridica = p.FormaGiuridica,
            NumeroRea = p.NumeroRea, CodiceAteco = p.CodiceAteco, DataInizioAttivita = p.DataInizioAttivita, Pec = p.Pec, Telefono = p.Telefono,
        },
        2 => new SediForm
        {
            LegaleVia = p.SedeLegaleVia, LegaleCap = p.SedeLegaleCap, LegaleComune = p.SedeLegaleComune, LegaleProvincia = p.SedeLegaleProvincia,
            // Profili vecchi: la sede operativa diventa la prima unità locale.
            Unita = p.UnitaLocali.Count > 0
                ? p.UnitaLocali.Select(u => new UnitaForm { Id = u.Id, Nome = u.Nome, Via = u.Via, Cap = u.Cap, Comune = u.Comune, Provincia = u.Provincia,
                    ReferenteNome = u.ReferenteNome, ReferenteEmail = u.ReferenteEmail, ReferenteTelefono = u.ReferenteTelefono }).ToList()
                : p.SedeOperativaVia is null ? []
                : [new UnitaForm { Nome = "Sede operativa", Via = p.SedeOperativaVia, Cap = p.SedeOperativaCap, Comune = p.SedeOperativaComune, Provincia = p.SedeOperativaProvincia }],
        },
        3 => new LegaleForm
        {
            Nome = p.LegaleNome, Cognome = p.LegaleCognome, CodiceFiscale = p.LegaleCodiceFiscale?.Valore, Email = p.LegaleEmail, Telefono = p.LegaleTelefono,
            LegaleUnicoTitolare = p.LegaleCodiceFiscale is null || p.LegaleUnicoTitolare || p.TitolariEffettivi.Count == 0,
            Titolari = p.TitolariEffettivi.Select(t => new TitolareForm { Nome = t.Nome, Cognome = t.Cognome, CodiceFiscale = t.CodiceFiscale.Valore, Quota = t.Quota }).ToList(),
        },
        4 => new PosizioniForm
        {
            Ccnl = p.Ccnl, MatricolaInps = p.MatricolaInps, InailCodiceDitta = p.InailCodiceDitta, InailPat = p.InailPat,
            CassaEdile = p.CassaEdile, EnteBilaterale = p.EnteBilaterale, FondoPensione = p.FondoPensione,
            GiorniLavorativiSettimana = p.GiorniLavorativiSettimana ?? 5, OreSettimanali = p.OreSettimanali ?? 40,
            NessunContratto = p.SenzaContratti,
            Csc = p.Csc, CodiciAutorizzazione = p.CodiciAutorizzazione, ContrattiIntegrativi = p.ContrattiIntegrativi,
            VociInail = p.VociInail.Select(v => new VoceInailForm { Voce = v.Voce, Tasso = v.Tasso }).ToList(),
        },
        ProfiloAzienda.PassoSubentro => new SubentroForm
        {
            ConsulentePrecedente = p.ConsulentePrecedente, ContattiPrecedente = p.ContattiPrecedente, DataSubentro = p.DataSubentro,
            UltimoMeseElaborato = p.UltimoMeseElaborato?.ToString("yyyy-MM"),
        },
        5 => new PagheForm
        {
            Iban = p.IbanAddebito?.Valore, GiornoPagamento = p.GiornoPagamento, NoteStudio = p.NoteStudio,
            Referenti = p.Referenti.Select(r => new ReferenteForm { Ambito = r.Ambito, Nome = r.Nome, Ruolo = r.Ruolo, Email = r.Email, Telefono = r.Telefono }).ToList(),
        },
        _ => null,
    };

    /// <summary>Notifica ai consulenti dell'azienda, con il link alla sua scheda.</summary>
    private async Task AvvisaStudio(Azienda azienda, ProfiloAzienda profilo) =>
        await notifiche.InviaAsync(TipoNotifica.ProfiloAziendaCompletato, await db.StudioAsync(azienda.Id),
            $"Profilo completato: {azienda.RagioneSociale}",
            NotificheRichieste.Righe(
                $"{azienda.RagioneSociale} ha completato il profilo per il consulente (compilato da {profilo.CompletatoDa}).",
                "Dati societari, sedi, legale rappresentante, posizioni INPS/INAIL e referente paghe sono nella scheda dell'azienda."),
            $"/Aziende/Dettaglio/{azienda.Id}");
}
