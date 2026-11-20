using CL.Domain.ValueObjects;
using CL.Infrastructure.Indirizzi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace CL.Web.Controllers;

/// <summary>Suggerimenti di indirizzo per i moduli (via, CAP, comune, provincia) mentre si scrive.</summary>
[Authorize]
public sealed class IndirizziController(IRicercaIndirizzi ricerca, IMemoryCache cache) : Controller
{
    /// <summary>Comuni ISTAT per la select con ricerca, eventualmente solo quelli di una provincia.</summary>
    [HttpGet]
    public IActionResult Comuni(string? q, string? p) =>
        Json(CL.Domain.ValueObjects.Comuni.Cerca(q, p).Select(c => new { id = c.Nome, text = $"{c.Nome} ({c.Provincia})", provincia = c.Provincia }));

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
