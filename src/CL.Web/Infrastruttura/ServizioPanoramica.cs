using CL.Domain.Assenze;
using CL.Domain.BustePaga;
using CL.Domain.Dipendenti;
using CL.Domain.Presenze;
using CL.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

public sealed record MeseAssenze(int Anno, int Mese, AssenzeDelMese Ore)
{
    public string Etichetta => ServizioBustePaga.NomeMese(Mese)[..3] + (Mese == 1 ? " " + (Anno % 100).ToString("00") : "");
    public string Periodo => $"{ServizioBustePaga.NomeMese(Mese)} {Anno}";
}

public sealed record FerieDipendente(Guid DipendenteId, string Nome, string Azienda, decimal Godute, decimal Disponibili, decimal InAttesa);

public sealed class DatiPanoramica
{
    public int AziendeAttive { get; init; }
    public int DipendentiInForza { get; init; }
    public int RichiesteInAttesa { get; init; }
    public decimal FerieGoduteAnno { get; init; }
    public decimal FerieDisponibili { get; init; }
    /// <summary>Presenze del mese ricevute dallo studio su aziende attive (solo per lo studio).</summary>
    public (int Ricevute, int Totale, int Anno, int Mese)? Presenze { get; init; }
    /// <summary>Cedolini pubblicati negli ultimi 3 mesi e quanti già aperti dai dipendenti.</summary>
    public (int Consegnati, int Pubblicati) Cedolini { get; init; }
    public required List<MeseAssenze> AssenzePerMese { get; init; }
    public required List<FerieDipendente> FeriePerDipendente { get; init; }
    public Guid? AziendaId { get; init; }
    public List<SelectListItem>? Aziende { get; init; }
    public int Anno { get; init; }
}

/// <summary>
/// Dati della panoramica nel perimetro dell'utente (studio: tutte le aziende o una; referente: la propria).
/// Poche query aggregate; i saldi si calcolano in memoria con le regole di dominio.
/// </summary>
public sealed class ServizioPanoramica(AppDbContext db, UtenteCorrente utente, TimeProvider clock)
{
    public const int Mesi = 12;

    public async Task<DatiPanoramica> CalcolaAsync(Guid? aziendaId)
    {
        var oggi = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var inizio = new DateOnly(oggi.Year, oggi.Month, 1).AddMonths(-(Mesi - 1));
        var fine = new DateOnly(oggi.Year, oggi.Month, 1).AddMonths(1).AddDays(-1);
        var inizioAnno = new DateOnly(oggi.Year, 1, 1);
        var da = inizio < inizioAnno ? inizio : inizioAnno;

        var perimetro = utente.Filtra(db.Dipendenti.AsNoTracking());
        if (utente.IsConsulente && aziendaId is { } az) perimetro = perimetro.Where(d => d.AziendaId == az);

        // Dipendenti con rapporto nel periodo osservato (con contratti), aziende per patrono e nome.
        var dipendenti = await perimetro
            .Where(d => d.Contratti.Any(c => c.DataInizio <= fine && (c.DataFine == null || c.DataFine >= da)))
            .ToListAsync();
        var ids = dipendenti.Select(d => d.Id).ToList();
        var aziendeIds = dipendenti.Select(d => d.AziendaId).Distinct().ToList();
        var aziende = await db.Aziende.AsNoTracking().Where(a => aziendeIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id);
        var richieste = await db.RichiesteAssenza.AsNoTracking()
            .Where(r => ids.Contains(r.DipendenteId) && r.Al >= da && r.Dal <= fine
                        && (r.Stato == StatoRichiesta.Approvata || r.Stato == StatoRichiesta.InAttesa))
            .ToListAsync();
        var iniziali = await db.SaldiIniziali.AsNoTracking()
            .Where(s => ids.Contains(s.DipendenteId) && s.Anno == oggi.Year && s.Tipo == TipoAssenza.Ferie)
            .ToListAsync();
        var perDipendente = richieste.ToLookup(r => r.DipendenteId);

        // Ore di assenza approvate per mese (ripartite sui giorni lavorativi del mese).
        var mesi = new List<MeseAssenze>();
        for (var m = inizio; m <= fine; m = m.AddMonths(1))
        {
            var fineMese = m.AddMonths(1).AddDays(-1);
            var totale = AssenzeDelMese.Nessuna;
            foreach (var d in dipendenti)
            {
                var proprie = perDipendente[d.Id];
                if (!proprie.Any(r => r.Dal <= fineMese && r.Al >= m)) continue;
                var a = aziende[d.AziendaId];
                var x = ComunicazioneMensile.AssenzeDi(proprie, m.Year, m.Month, d.ContrattoAttivo(fineMese) ?? d.ContrattoAttivo(m),
                    a.SantoPatronoMese, a.SantoPatronoGiorno);
                totale = new AssenzeDelMese(totale.Ferie + x.Ferie, totale.Rol + x.Rol, totale.ExFestivita + x.ExFestivita,
                    totale.Malattia + x.Malattia, totale.Altre + x.Altre);
            }
            mesi.Add(new MeseAssenze(m.Year, m.Month, totale));
        }

        // Ferie dell'anno per dipendente in forza oggi: godute, disponibili, in attesa di approvazione.
        // Solo il lavoro dipendente: i professionisti esterni non maturano ferie.
        var ferie = dipendenti.Where(d => d.ContrattoAttivo(oggi) is { } c && c.Tipo.IsSubordinato())
            .Select(d =>
            {
                var s = CalcoloSaldi.Calcola(d, TipoAssenza.Ferie, oggi.Year, oggi.Month,
                    perDipendente[d.Id].Where(r => r.Dal.Year == oggi.Year), iniziali.FirstOrDefault(i => i.DipendenteId == d.Id));
                return new FerieDipendente(d.Id, d.NomeCompleto, aziende[d.AziendaId].RagioneSociale, s.Goduto, Math.Max(0, s.Disponibile), s.Prenotato);
            })
            .OrderByDescending(f => f.Disponibili).ThenBy(f => f.Nome)
            .ToList();

        var idsInPerimetro = perimetro.Select(d => d.Id);
        var inAttesa = await db.RichiesteAssenza.CountAsync(r => idsInPerimetro.Contains(r.DipendenteId) && r.Stato == StatoRichiesta.InAttesa);
        var tre = new DateOnly(oggi.Year, oggi.Month, 1).AddMonths(-2);
        var recenti = db.BustePaga.Where(b => idsInPerimetro.Contains(b.DipendenteId) && b.Stato == StatoBustaPaga.Pubblicata
                                              && (b.Anno > tre.Year || (b.Anno == tre.Year && b.Mese >= tre.Month)));
        var cedolini = new { Pubblicati = await recenti.CountAsync(), Consegnati = await recenti.CountAsync(b => b.VisualizzataIl != null) };

        (int, int, int, int)? presenze = null;
        int aziendeAttive = 0;
        if (utente.IsConsulente)
        {
            var mesePresenze = oggi.Day <= 10 ? oggi.AddMonths(-1) : oggi;
            var attive = db.Aziende.Where(a => a.Attiva && (aziendaId == null || a.Id == aziendaId));
            aziendeAttive = await db.Aziende.CountAsync(a => a.Attiva);
            var ricevute = await db.ComunicazioniPresenze.CountAsync(c => attive.Any(a => a.Id == c.AziendaId)
                && c.Anno == mesePresenze.Year && c.Mese == mesePresenze.Month && c.Stato != StatoComunicazione.Bozza);
            presenze = (ricevute, await attive.CountAsync(), mesePresenze.Year, mesePresenze.Month);
        }

        return new DatiPanoramica
        {
            AziendeAttive = aziendeAttive,
            DipendentiInForza = dipendenti.Count(d => d.InForzaIl(oggi)),
            RichiesteInAttesa = inAttesa,
            FerieGoduteAnno = ferie.Sum(f => f.Godute),
            FerieDisponibili = ferie.Sum(f => f.Disponibili),
            Presenze = presenze,
            Cedolini = (cedolini.Consegnati, cedolini.Pubblicati),
            AssenzePerMese = mesi,
            FeriePerDipendente = ferie,
            AziendaId = aziendaId,
            Aziende = utente.IsConsulente
                ? await db.Aziende.AsNoTracking().Where(a => a.Attiva).OrderBy(a => a.RagioneSociale)
                    .Select(a => new SelectListItem(a.RagioneSociale, a.Id.ToString(), a.Id == aziendaId)).ToListAsync()
                : null,
            Anno = oggi.Year,
        };
    }
}
