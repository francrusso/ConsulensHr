using CL.Domain.Assenze;
using CL.Domain.Dipendenti;
using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

/// <summary>Carica richieste e residui dal database e calcola i saldi con le regole di dominio.</summary>
public sealed class ServizioSaldi(AppDbContext db)
{
    // Le assenze con un conto ore: ferie e ROL.
    public static readonly TipoAssenza[] TipiConSaldo = [TipoAssenza.Ferie, TipoAssenza.Rol];

    /// <summary>Richieste del dipendente che iniziano nell'anno indicato.</summary>
    public Task<List<RichiestaAssenza>> RichiesteAnno(Guid dipendenteId, int anno)
    {
        var (inizio, fine) = Filtri.Anno(anno);
        return db.RichiesteAssenza.AsNoTracking()
            .Where(r => r.DipendenteId == dipendenteId && r.Dal >= inizio && r.Dal <= fine)
            .ToListAsync();
    }

    /// <summary>
    /// Saldi dell'anno, con il maturato fino al mese indicato.
    /// Se il chiamante ha già caricato le richieste dell'anno (<see cref="RichiesteAnno"/>) le passa, risparmiando una query.
    /// </summary>
    public async Task<IReadOnlyList<SaldoAssenza>> Calcola(Dipendente dipendente, int anno, int finoAlMese,
        IReadOnlyList<RichiestaAssenza>? richiesteAnno = null)
    {
        var richieste = richiesteAnno ?? await RichiesteAnno(dipendente.Id, anno);
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
