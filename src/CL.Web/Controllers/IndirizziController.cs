using CL.Infrastructure.Indirizzi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace CL.Web.Controllers;

/// <summary>
/// Suggerimenti di indirizzo per i moduli mentre si scrive (usati da wwwroot/js/indirizzi.js): comuni dall'elenco ISTAT
/// e vie dal servizio esterno di ricerca indirizzi.
/// </summary>
public sealed class IndirizziController(IRicercaIndirizzi ricerca, IMemoryCache cache) : Controller
{
    /// <summary>Comuni ISTAT per la select con ricerca, eventualmente solo quelli di una provincia.</summary>
    [HttpGet]
    public IActionResult Comuni(string? q, string? p) =>
        // Nome completo del tipo: dentro il controller "Comuni" è questa azione.
        Json(CL.Domain.ValueObjects.Comuni.Cerca(q, p).Select(c => new { id = c.Nome, text = $"{c.Nome} ({c.Provincia})", provincia = c.Provincia }));

    /// <summary>Vie che corrispondono al testo scritto, con CAP, comune e provincia da compilare in automatico.</summary>
    [HttpGet]
    public async Task<IActionResult> Cerca(string? q, CancellationToken ct)
    {
        var testo = (q ?? "").Trim();
        if (testo.Length < 4 || testo.Length > 200) return Json(Array.Empty<IndirizzoTrovato>());
        // Stessa ricerca ripetuta (correzioni, più persone del consulente): risposta in memoria per qualche minuto.
        var chiave = "indirizzi:" + testo.ToLowerInvariant();
        if (!cache.TryGetValue(chiave, out IReadOnlyList<IndirizzoTrovato>? risultati))
        {
            risultati = await ricerca.CercaAsync(testo, ct);
            if (risultati.Count > 0) cache.Set(chiave, risultati, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10), Size = 1 });
        }
        return Json(risultati ?? []);
    }
}
