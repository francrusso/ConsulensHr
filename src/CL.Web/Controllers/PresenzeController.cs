using CL.Domain.Common;
using CL.Domain.Presenze;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Controllers;

public sealed class RigaPresenzeForm
{
    public Guid DipendenteId { get; set; }
    public decimal? OreStraordinario { get; set; }
    public decimal? OreStraordinarioFestivo { get; set; }
    public decimal? OreNonRetribuite { get; set; }
    public int? BuoniPasto { get; set; }
    public decimal? Premi { get; set; }
    public decimal? Rimborsi { get; set; }
    public decimal? Anticipi { get; set; }
    public string? Note { get; set; }

    public VociMensili Voci => new(OreStraordinario ?? 0, OreStraordinarioFestivo ?? 0, OreNonRetribuite ?? 0, BuoniPasto ?? 0,
        Premi ?? 0, Rimborsi ?? 0, Anticipi ?? 0, Note);
}

public sealed class CruscottoPresenzeViewModel
{
    public required int Anno { get; init; }
    public required int Mese { get; init; }
    public required List<StatoAziendaMese> Aziende { get; init; }
}

/// <summary>
/// Presenze e variazioni del mese: l'azienda compila e invia, il consulente controlla, esporta per il gestionale paghe,
/// sollecita chi non ha inviato e segna il mese come elaborato (o lo riapre per correzioni).
/// </summary>
[Authorize(Policy = Policy.GestionePersonale)]
public sealed class PresenzeController(AppDbContext db, UtenteCorrente utente, ServizioPresenze servizio, TimeProvider clock) : AppController
{
    private DateTime AdessoUtc => clock.GetUtcNow().UtcDateTime;

    /// <summary>Mese proposto: nei primi 10 giorni quello appena chiuso, poi quello in corso. Niente mesi futuri.</summary>
    private (int Anno, int Mese) Periodo(int? anno, int? mese)
    {
        var oggi = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var predefinito = oggi.Day <= 10 ? oggi.AddMonths(-1) : oggi;
        var p = new DateOnly(anno ?? predefinito.Year, Math.Clamp(mese ?? predefinito.Month, 1, 12), 1);
        var corrente = new DateOnly(oggi.Year, oggi.Month, 1);
        if (p > corrente) p = corrente;
        return (p.Year, p.Month);
    }

    /// <summary>Il referente vede sempre la propria azienda; il consulente quella indicata.</summary>
    private Guid? AziendaScelta(Guid? aziendaId) => utente.IsAzienda ? utente.AziendaId : aziendaId;

    private Task<ComunicazioneMensile?> Comunicazione(Guid aziendaId, int anno, int mese) =>
        db.ComunicazioniPresenze.FirstOrDefaultAsync(x => x.AziendaId == aziendaId && x.Anno == anno && x.Mese == mese);

    /// <summary>Cruscotto del consulente; il referente va direttamente al foglio della propria azienda.</summary>
    public async Task<IActionResult> Index(int? anno, int? mese)
    {
        var (a, m) = Periodo(anno, mese);
        if (!utente.IsConsulente) return RedirectToAction(nameof(Foglio), new { anno = a, mese = m });
        return View(new CruscottoPresenzeViewModel { Anno = a, Mese = m, Aziende = await servizio.StatoMeseAsync(a, m) });
    }

    public async Task<IActionResult> Foglio(Guid? aziendaId, int? anno, int? mese)
    {
        if (AziendaScelta(aziendaId) is not { } id) return RedirectToAction(nameof(Index));
        var (a, m) = Periodo(anno, mese);
        var foglio = await servizio.FoglioAsync(id, a, m);
        return foglio is null ? NotFound() : View(foglio);
    }

    /// <summary>
    /// Salva le variazioni del mese (bozza) e, con <paramref name="invia"/>, manda la comunicazione al consulente.
    /// Righe di persone che non sono nel foglio vengono ignorate.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Salva(int anno, int mese, List<RigaPresenzeForm> righe, string? note, bool invia)
    {
        if (!utente.IsAzienda || utente.AziendaId is not { } aziendaId) return Forbid();
        var (a, m) = Periodo(anno, mese);
        var ritorno = RedirectToAction(nameof(Foglio), new { anno = a, mese = m });

        var foglio = await servizio.FoglioAsync(aziendaId, a, m);
        if (foglio is null) return NotFound();
        var ammessi = foglio.Righe.Select(r => r.DipendenteId).ToHashSet();

        var c = await Comunicazione(aziendaId, a, m);
        if (c is null) db.ComunicazioniPresenze.Add(c = new ComunicazioneMensile(aziendaId, a, m));

        try
        {
            foreach (var r in righe.Where(r => ammessi.Contains(r.DipendenteId))) c.ImpostaVoci(r.DipendenteId, r.Voci);
            c.ImpostaNote(note);
            // All'invio si fotografano le assenze del foglio: il consulente elabora esattamente ciò che ha ricevuto.
            if (invia) c.Invia(foglio.Righe.ToDictionary(r => r.DipendenteId, r => r.Assenze), utente.Email, AdessoUtc);
        }
        catch (DomainException ex)
        {
            Errore(ex.Message);
            return ritorno;
        }

        await db.SaveChangesAsync();
        if (invia)
        {
            await servizio.AvvisaStudioAsync(foglio.Azienda, c);
            Conferma($"Presenze di {foglio.Periodo.ToLower()} inviate al consulente.");
        }
        else Conferma("Bozza salvata: puoi completarla e inviarla quando è pronta.");
        return ritorno;
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.SoloConsulente)]
    public async Task<IActionResult> Elabora(Guid aziendaId, int anno, int mese)
    {
        var c = await Comunicazione(aziendaId, anno, mese);
        if (c is null) return NotFound();
        if (ProvaOAvvisa(() => c.SegnaElaborata(utente.Email, AdessoUtc)))
        {
            await db.SaveChangesAsync();
            await servizio.AvvisaElaborataAsync(aziendaId, c);
            Conferma("Mese segnato come elaborato.");
        }
        return RedirectToAction(nameof(Foglio), new { aziendaId, anno, mese });
    }

    /// <summary>Il consulente rimette la comunicazione in bozza e avvisa per email i referenti dell'azienda.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.SoloConsulente)]
    public async Task<IActionResult> Riapri(Guid aziendaId, int anno, int mese, string? motivo)
    {
        var c = await Comunicazione(aziendaId, anno, mese);
        var azienda = await db.Aziende.AsNoTracking().FirstOrDefaultAsync(x => x.Id == aziendaId);
        if (c is null || azienda is null) return NotFound();
        if (ProvaOAvvisa(c.Riapri))
        {
            await db.SaveChangesAsync();
            var n = await servizio.AvvisaAziendaAsync(azienda, anno, mese, riaperta: true, motivo);
            Conferma(n > 0 ? "Comunicazione riaperta: l'azienda è stata avvisata." : "Comunicazione riaperta (l'azienda non ha referenti con accesso da avvisare).");
        }
        return RedirectToAction(nameof(Foglio), new { aziendaId, anno, mese });
    }

    /// <summary>Promemoria per email ai referenti di un'azienda che non ha ancora inviato il mese.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.SoloConsulente)]
    public async Task<IActionResult> Sollecita(Guid aziendaId, int anno, int mese)
    {
        var azienda = await db.Aziende.AsNoTracking().FirstOrDefaultAsync(x => x.Id == aziendaId);
        if (azienda is null) return NotFound();
        var n = await servizio.AvvisaAziendaAsync(azienda, anno, mese, riaperta: false);
        if (n > 0) Conferma($"Promemoria inviato a {azienda.RagioneSociale} ({n} {(n == 1 ? "referente" : "referenti")}).");
        else Errore($"{azienda.RagioneSociale} non ha referenti con accesso attivo: invita un referente dalla scheda dell'azienda.");
        return RedirectToAction(nameof(Index), new { anno, mese });
    }

    /// <summary>Riepilogo del mese in CSV: l'azienda il proprio, il consulente quello di un'azienda.</summary>
    public async Task<IActionResult> Esporta(Guid? aziendaId, int anno, int mese)
    {
        if (AziendaScelta(aziendaId) is not { } id) return NotFound();
        var foglio = await servizio.FoglioAsync(id, anno, mese);
        if (foglio is null) return NotFound();
        // Nome del file: ragione sociale senza simboli, spazi come trattini bassi.
        var nome = new string(foglio.Azienda.RagioneSociale.Where(ch => char.IsLetterOrDigit(ch) || ch == ' ').ToArray()).Trim().Replace(' ', '_');
        return File(ServizioPresenze.Csv(foglio), "text/csv; charset=utf-8", $"Riepilogo_presenze_{nome}_{anno}-{mese:00}.csv");
    }

    /// <summary>Riepilogo del mese di tutte le aziende attive in un solo file (consulente).</summary>
    [Authorize(Policy = Policy.SoloConsulente)]
    public async Task<IActionResult> EsportaTutto(int anno, int mese)
    {
        var fogli = new List<FoglioPresenze>();
        foreach (var a in await servizio.StatoMeseAsync(anno, mese))
            if (await servizio.FoglioAsync(a.AziendaId, anno, mese) is { } f) fogli.Add(f);
        return File(ServizioPresenze.Csv(fogli, conAzienda: true), "text/csv; charset=utf-8", $"Riepilogo_presenze_{anno}-{mese:00}.csv");
    }
}
