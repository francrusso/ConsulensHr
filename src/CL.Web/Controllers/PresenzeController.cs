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
/// Presenze e variazioni del mese: l'azienda compila e invia, lo studio controlla, esporta per il gestionale paghe,
/// sollecita chi non ha inviato e segna il mese come elaborato (o lo riapre per correzioni).
/// </summary>
[Authorize(Policy = Policy.GestionePersonale)]
public sealed class PresenzeController(AppDbContext db, UtenteCorrente utente, ServizioPresenze servizio, TimeProvider clock) : AppController
{
    private DateOnly Oggi => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    /// <summary>Mese proposto: nei primi 10 giorni quello appena chiuso, poi quello in corso.</summary>
    private (int Anno, int Mese) Periodo(int? anno, int? mese)
    {
        var predefinito = Oggi.Day <= 10 ? Oggi.AddMonths(-1) : Oggi;
        var p = new DateOnly(anno ?? predefinito.Year, Math.Clamp(mese ?? predefinito.Month, 1, 12), 1);
        if (p > new DateOnly(Oggi.Year, Oggi.Month, 1)) p = new DateOnly(Oggi.Year, Oggi.Month, 1); // niente mesi futuri
        return (p.Year, p.Month);
    }

    public async Task<IActionResult> Index(int? anno, int? mese)
    {
        var (a, m) = Periodo(anno, mese);
        if (!utente.IsConsulente) return RedirectToAction(nameof(Foglio), new { anno = a, mese = m });
        return View(new CruscottoPresenzeViewModel { Anno = a, Mese = m, Aziende = await servizio.StatoMeseAsync(a, m) });
    }

    public async Task<IActionResult> Foglio(Guid? aziendaId, int? anno, int? mese)
    {
        var id = utente.IsAzienda ? utente.AziendaId : aziendaId;
        if (id is null) return RedirectToAction(nameof(Index));
        var (a, m) = Periodo(anno, mese);
        var foglio = await servizio.FoglioAsync(id.Value, a, m);
        return foglio is null ? NotFound() : View(foglio);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Salva(int anno, int mese, List<RigaPresenzeForm> righe, string? note, bool invia)
    {
        if (!utente.IsAzienda || utente.AziendaId is not { } aziendaId) return Forbid();
        var (a, m) = Periodo(anno, mese);
        var ritorno = RedirectToAction(nameof(Foglio), new { anno = a, mese = m });

        var foglio = await servizio.FoglioAsync(aziendaId, a, m);
        if (foglio is null) return NotFound();
        var ammessi = foglio.Righe.Select(r => r.DipendenteId).ToHashSet();

        var c = await db.ComunicazioniPresenze.FirstOrDefaultAsync(x => x.AziendaId == aziendaId && x.Anno == a && x.Mese == m);
        if (c is null) db.ComunicazioniPresenze.Add(c = new ComunicazioneMensile(aziendaId, a, m));

        try
        {
            foreach (var r in righe.Where(r => ammessi.Contains(r.DipendenteId))) c.ImpostaVoci(r.DipendenteId, r.Voci);
            c.ImpostaNote(note);
            if (invia) c.Invia(foglio.Righe.ToDictionary(r => r.DipendenteId, r => r.Assenze), utente.Email, clock.GetUtcNow().UtcDateTime);
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
            Conferma($"Presenze di {foglio.Periodo.ToLower()} inviate allo studio.");
        }
        else Conferma("Bozza salvata: puoi completarla e inviarla quando è pronta.");
        return ritorno;
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.SoloConsulente)]
    public async Task<IActionResult> Elabora(Guid aziendaId, int anno, int mese)
    {
        var c = await db.ComunicazioniPresenze.FirstOrDefaultAsync(x => x.AziendaId == aziendaId && x.Anno == anno && x.Mese == mese);
        if (c is null) return NotFound();
        if (Prova(() => c.SegnaElaborata(utente.Email, clock.GetUtcNow().UtcDateTime)))
        {
            await db.SaveChangesAsync();
            Conferma("Mese segnato come elaborato.");
        }
        else Errore(ModelState[""]!.Errors[0].ErrorMessage);
        return RedirectToAction(nameof(Foglio), new { aziendaId, anno, mese });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Policy = Policy.SoloConsulente)]
    public async Task<IActionResult> Riapri(Guid aziendaId, int anno, int mese, string? motivo)
    {
        var c = await db.ComunicazioniPresenze.FirstOrDefaultAsync(x => x.AziendaId == aziendaId && x.Anno == anno && x.Mese == mese);
        var azienda = await db.Aziende.AsNoTracking().FirstOrDefaultAsync(x => x.Id == aziendaId);
        if (c is null || azienda is null) return NotFound();
        if (Prova(c.Riapri))
        {
            await db.SaveChangesAsync();
            var n = await servizio.AvvisaAziendaAsync(azienda, anno, mese, riaperta: true, motivo);
            Conferma(n > 0 ? "Comunicazione riaperta: l'azienda è stata avvisata per email." : "Comunicazione riaperta (l'azienda non ha referenti con accesso da avvisare).");
        }
        else Errore(ModelState[""]!.Errors[0].ErrorMessage);
        return RedirectToAction(nameof(Foglio), new { aziendaId, anno, mese });
    }

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

    [Authorize(Policy = Policy.SoloConsulente)]
    public async Task<IActionResult> Esporta(Guid aziendaId, int anno, int mese)
    {
        var foglio = await servizio.FoglioAsync(aziendaId, anno, mese);
        if (foglio is null) return NotFound();
        var nome = new string(foglio.Azienda.RagioneSociale.Where(ch => char.IsLetterOrDigit(ch) || ch == ' ').ToArray()).Trim().Replace(' ', '_');
        return File(ServizioPresenze.Csv(foglio), "text/csv; charset=utf-8", $"Presenze_{nome}_{anno}-{mese:00}.csv");
    }
}
