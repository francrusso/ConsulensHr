using CL.Domain.Assenze;
using CL.Domain.Dipendenti;
using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

/// <summary>Carica richieste e residui dal database e calcola i saldi con le regole di dominio.</summary>
public sealed class ServizioSaldi(AppDbContext db)
{
    public static readonly TipoAssenza[] TipiConSaldo = [TipoAssenza.Ferie, TipoAssenza.Rol, TipoAssenza.ExFestivita];

    /// <summary>Saldi dell'anno, con il maturato fino al mese indicato.</summary>
    public async Task<IReadOnlyList<SaldoAssenza>> Calcola(Dipendente dipendente, int anno, int finoAlMese)
    {
        var richieste = await db.RichiesteAssenza.AsNoTracking()
            .Where(r => r.DipendenteId == dipendente.Id && r.Dal.Year == anno)
            .ToListAsync();
        var iniziali = await db.SaldiIniziali.AsNoTracking()
            .Where(s => s.DipendenteId == dipendente.Id && s.Anno == anno)
            .ToListAsync();

        return TipiConSaldo
            .Select(t => CalcoloSaldi.Calcola(dipendente, t, anno, finoAlMese, richieste, iniziali.FirstOrDefault(i => i.Tipo == t)))
            .ToList();
    }

    public async Task<SaldoAssenza> Calcola(Dipendente dipendente, TipoAssenza tipo, int anno, int finoAlMese) =>
        (await Calcola(dipendente, anno, finoAlMese)).Single(s => s.Tipo == tipo);
}
