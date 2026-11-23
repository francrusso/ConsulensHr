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

/// <summary>Persona nella panoramica, con l'eventuale dettaglio (es. "Ferie", "ROL · 2 h").</summary>
public sealed record PersonaOggi(Guid Id, string Nome, bool HaFoto, string Segnaposto, string? Dettaglio = null);

/// <summary>Chi lavora e chi è assente nel giorno lavorativo mostrato (oggi, o il prossimo se oggi è festa).</summary>
public sealed record GiornataOggi(DateOnly Giorno, bool EOggi, List<PersonaOggi> InUfficio, List<PersonaOggi> DaRemoto, List<PersonaOggi> Assenti);

/// <summary>Richiesta in attesa da mostrare in panoramica (ferie, permessi o smart working).</summary>
public sealed record RichiestaInAttesa(Guid Id, PersonaOggi Persona, string Tipo, DateOnly Dal, DateOnly Al, bool AOre, decimal Ore, bool SmartWorking, bool Straordinario = false);

public sealed record ScadenzaContratto(PersonaOggi Persona, string Tipo, DateOnly DataFine, int Giorni);

public sealed record FerieDipendente(Guid DipendenteId, string Nome, string Azienda, decimal Godute, decimal Disponibili, decimal InAttesa);

public sealed class DatiPanoramica
{
    public int AziendeAttive { get; init; }
    public int DipendentiInForza { get; init; }
    public int RichiesteInAttesa { get; init; }
    public decimal FerieGoduteAnno { get; init; }
    public decimal FerieDisponibili { get; init; }
    /// <summary>Presenze del mese ricevute dal consulente su aziende attive (solo per il consulente).</summary>
    public (int Ricevute, int Totale, int Anno, int Mese)? Presenze { get; init; }
    /// <summary>Cedolini pubblicati negli ultimi 3 mesi e quanti già aperti dai dipendenti.</summary>
    public (int Consegnati, int Pubblicati) Cedolini { get; init; }
    public required List<MeseAssenze> AssenzePerMese { get; init; }
    public required List<FerieDipendente> FeriePerDipendente { get; init; }
    public Guid? AziendaId { get; init; }
    public List<SelectListItem>? Aziende { get; init; }
    public int Anno { get; init; }
    public GiornataOggi? Oggi { get; init; }
    /// <summary>Le prime richieste in attesa, dalla più vicina.</summary>
    public List<RichiestaInAttesa> DaApprovare { get; init; } = [];
    /// <summary>Contratti a termine che scadono nei prossimi 60 giorni senza un rinnovo già registrato.</summary>
    public List<ScadenzaContratto> Scadenze { get; init; } = [];
}

/// <summary>
/// Dati della panoramica nel perimetro dell'utente (consulente: tutte le aziende o una; referente: la propria).
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
                totale = new AssenzeDelMese(totale.Ferie + x.Ferie, totale.Rol + x.Rol,
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
        var inAttesa = await db.RichiesteAssenza.CountAsync(r => idsInPerimetro.Contains(r.DipendenteId) && r.Stato == StatoRichiesta.InAttesa)
                       + await db.GiorniSmartWorking.CountAsync(g => idsInPerimetro.Contains(g.DipendenteId) && g.Stato == StatoSmartWorking.InAttesa)
                       + await db.Straordinari.CountAsync(g => idsInPerimetro.Contains(g.DipendenteId) && g.Stato == StatoRichiesta.InAttesa);
        var persone = dipendenti.ToDictionary(d => d.Id);
        PersonaOggi P(Dipendente d, string? dettaglio = null) => new(d.Id, d.NomeCompleto, d.FotoChiave is not null, d.Segnaposto, dettaglio);

        // Giornata di lavoro: oggi, o il prossimo giorno lavorativo se oggi è festa o fine settimana.
        var giorno = oggi;
        while (!CalendarioLavorativo.IsLavorativo(giorno, 5)) giorno = giorno.AddDays(1);
        var inForza = dipendenti.Where(d => d.InForzaIl(giorno)
            && CalendarioLavorativo.IsLavorativo(giorno, d.ContrattoAttivo(giorno)!.GiorniLavorativiSettimana,
                aziende[d.AziendaId].SantoPatronoMese, aziende[d.AziendaId].SantoPatronoGiorno)).ToList();
        var richiesteGiorno = await db.RichiesteAssenza.AsNoTracking()
            .Where(r => ids.Contains(r.DipendenteId) && r.Stato == StatoRichiesta.Approvata && r.Dal <= giorno && r.Al >= giorno).ToListAsync();
        var segnatiUfficio = (await db.GiorniInUfficio.AsNoTracking().Where(g => ids.Contains(g.DipendenteId) && g.Giorno == giorno).Select(g => g.DipendenteId).ToListAsync()).ToHashSet();
        var smartOggi = (await db.GiorniSmartWorking.AsNoTracking().Where(g => ids.Contains(g.DipendenteId) && g.Giorno == giorno && g.Stato == StatoSmartWorking.Approvato)
            .Select(g => g.DipendenteId).ToListAsync()).ToHashSet();
        var assenti = new List<PersonaOggi>();
        var fuori = new HashSet<Guid>();
        foreach (var d in inForza.OrderBy(x => x.Cognome).ThenBy(x => x.Nome))
        {
            if (richiesteGiorno.Where(r => r.DipendenteId == d.Id).OrderBy(r => r.AOre).FirstOrDefault() is not { } ass) continue;
            assenti.Add(P(d, ass.AOre ? $"{ass.Tipo.Descrizione()} · {ass.Ore:0.##} h" : ass.Tipo.Descrizione()));
            if (!ass.AOre) fuori.Add(d.Id);
        }
        var alLavoro = inForza.Where(d => !fuori.Contains(d.Id)).OrderBy(d => d.Cognome).ThenBy(d => d.Nome).ToList();
        bool InSede(Dipendente d) => segnatiUfficio.Contains(d.Id) || (!ServizioUfficio.LavoraADistanza(d, giorno) && !smartOggi.Contains(d.Id));
        var giornata = new GiornataOggi(giorno, giorno == oggi,
            alLavoro.Where(InSede).Select(d => P(d)).ToList(),
            alLavoro.Where(d => !InSede(d)).Select(d => P(d, smartOggi.Contains(d.Id) ? "Smart working" : null)).ToList(),
            assenti);

        // Richieste in attesa: ferie e permessi più lo smart working, dalla più vicina.
        var attesa = (await db.RichiesteAssenza.AsNoTracking().Where(r => ids.Contains(r.DipendenteId) && r.Stato == StatoRichiesta.InAttesa)
                .OrderBy(r => r.Dal).Take(8).ToListAsync())
            .Select(r => new RichiestaInAttesa(r.Id, P(persone[r.DipendenteId]), r.Tipo.Descrizione(), r.Dal, r.Al, r.AOre, r.Ore, false))
            .Concat((await db.GiorniSmartWorking.AsNoTracking().Where(g => ids.Contains(g.DipendenteId) && g.Stato == StatoSmartWorking.InAttesa)
                    .OrderBy(g => g.Giorno).Take(8).ToListAsync())
                .Select(g => new RichiestaInAttesa(g.Id, P(persone[g.DipendenteId]), "Smart working", g.Giorno, g.Giorno, false, 0, true)))
            .Concat((await db.Straordinari.AsNoTracking().Where(g => ids.Contains(g.DipendenteId) && g.Stato == StatoRichiesta.InAttesa)
                    .OrderBy(g => g.Giorno).Take(8).ToListAsync())
                .Select(g => new RichiestaInAttesa(g.Id, P(persone[g.DipendenteId]), "Straordinario", g.Giorno, g.Giorno, true, g.Ore, false, true)))
            .OrderBy(x => x.Dal).ThenBy(x => x.Persona.Nome).Take(6).ToList();

        // Contratti a termine in scadenza entro 60 giorni, se non c'è già un contratto successivo.
        var scadenze = dipendenti
            .SelectMany(d => d.Contratti.Where(c => c.DataFine is { } f && f >= oggi && f <= oggi.AddDays(60)
                                                    && !d.Contratti.Any(x => x.DataInizio > f))
                .Select(c => new ScadenzaContratto(P(d), c.Tipo.Descrizione(), c.DataFine!.Value, c.DataFine!.Value.DayNumber - oggi.DayNumber)))
            .OrderBy(x => x.DataFine).ToList();
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
            Oggi = giornata,
            DaApprovare = attesa,
            Scadenze = scadenze,
        };
    }
}
