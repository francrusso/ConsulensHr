using CL.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CL.Web.Infrastruttura;

/// <summary>Opzioni delle select di indirizzo (province ISTAT, comune già scelto).</summary>
public static class Elenchi
{
    public static List<SelectListItem> Province(string? scelta) =>
        Comuni.Province.Select(p => new SelectListItem($"{p.Key} – {p.Value}", p.Key,
            string.Equals(p.Key, scelta, StringComparison.OrdinalIgnoreCase))).ToList();

    /// <summary>La select del comune si popola cercando: all'apertura contiene solo il comune già salvato.</summary>
    public static List<SelectListItem> Comune(string? scelto, string? provincia) =>
        string.IsNullOrWhiteSpace(scelto) ? []
        : [new SelectListItem(provincia is { Length: > 0 } ? $"{scelto} ({provincia.ToUpperInvariant()})" : scelto, scelto, true)];
}
