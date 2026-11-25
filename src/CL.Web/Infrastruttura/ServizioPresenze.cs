using System.Globalization;
using System.Net;
using System.Text;
using CL.Domain.Assenze;
using CL.Domain.Aziende;
using CL.Domain.Dipendenti;
using CL.Domain.Presenze;
using CL.Infrastructure.Email;
using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

public sealed record RigaFoglio(Guid DipendenteId, string Nome, string? Matricola, string CodiceFiscale,
    AssenzeDelMese Assenze, VociMensili Voci)
{
    public bool HaFoto { get; init; }
    public string Segnaposto { get; init; } = "neutro";
    public string? Mansione { get; init; }
    /// <summary>Giorni lavorativi del mese senza assenze di tutta la giornata, divisi tra ufficio e remoto (regola del calendario).</summary>
    public int GiorniLavorati => InUfficio + Remoto;
    public int InUfficio { get; init; }
    public int Remoto { get; init; }
    /// <summary>Ore di straordinario approvate nel mese (feriali e festive): precompilano le variazioni.</summary>
    public decimal StraordinarioApprovato { get; init; }
    public decimal StraordinarioFestivoApprovato { get; init; }
}

public sealed record FoglioPresenze(Azienda Azienda, int Anno, int Mese, ComunicazioneMensile? Comunicazione, List<RigaFoglio> Righe)
{
    public StatoComunicazione Stato => Comunicazione?.Stato ?? StatoComunicazione.Bozza;
    public string Periodo => $"{ServizioBustePaga.NomeMese(Mese)} {Anno}";
}

public sealed record StatoAziendaMese(Guid AziendaId, string RagioneSociale, StatoComunicazione? Stato, DateTime? InviataIl, int Dipendenti);

/// <summary>Foglio presenze del mese: righe per i dipendenti in forza, assenze dal portale, invio ed esportazione.</summary>
public sealed class ServizioPresenze(AppDbContext db, CodaEmail coda, ServizioAccessi accessi)
{
    public async Task<FoglioPresenze?> FoglioAsync(Guid aziendaId, int anno, int mese)
    {
        var azienda = await db.Aziende.AsNoTracking().FirstOrDefaultAsync(a => a.Id == aziendaId);
        if (azienda is null) return null;
        var comunicazione = await db.ComunicazioniPresenze.AsNoTracking()
            .FirstOrDefaultAsync(c => c.AziendaId == aziendaId && c.Anno == anno && c.Mese == mese);

        var inizio = new DateOnly(anno, mese, 1);
        var fine = inizio.AddMonths(1).AddDays(-1);
        var giaInRiga = comunicazione?.Righe.Select(r => r.DipendenteId).ToList() ?? [];

        // Lavoro dipendente in forza per almeno un giorno del mese (più chi è già nella comunicazione);
        // i professionisti esterni non hanno presenze da comunicare.
        var esterni = TipoContrattoExtensions.Esterni.ToList();
        var nelMese = Filtri.ContrattoAttivoNelPeriodo(inizio, fine);
        var dipendenti = await db.Dipendenti.AsNoTracking()
            .Where(d => d.AziendaId == aziendaId
                        && (d.Contratti.AsQueryable().Where(nelMese).Any(c => !esterni.Contains(c.Tipo))
                            || giaInRiga.Contains(d.Id)))
            .OrderBy(d => d.Cognome).ThenBy(d => d.Nome)
            .ToListAsync();

        // Assenze approvate che toccano il mese, di chi è nel foglio.
        var ids = dipendenti.Select(d => d.Id).ToList();
        var approvate = (await db.RichiesteAssenza.AsNoTracking()
                .Where(r => ids.Contains(r.DipendenteId) && r.Stato == StatoRichiesta.Approvata && r.Dal <= fine && r.Al >= inizio)
                .ToListAsync())
            .ToLookup(r => r.DipendenteId);

        // A comunicazione inviata le assenze sono quelle fotografate all'invio; in bozza si calcolano dal portale.
        var assenze = comunicazione is { Stato: not StatoComunicazione.Bozza }
            ? comunicazione.Righe.ToDictionary(r => r.DipendenteId, r => r.Assenze)
            : dipendenti.ToDictionary(d => d.Id, d => ComunicazioneMensile.AssenzeDi(approvate[d.Id], anno, mese,
                d.ContrattoAttivo(fine) ?? d.ContrattoAttivo(inizio) ?? d.Contratti.OrderByDescending(c => c.DataInizio).FirstOrDefault(),
                azienda.SantoPatronoMese, azienda.SantoPatronoGiorno));

        // Giorni del mese in ufficio e da remoto, come nel calendario: servono a precompilare (es. i buoni pasto).
        var ufficio = (await db.GiorniInUfficio.AsNoTracking().Where(g => ids.Contains(g.DipendenteId) && g.Giorno >= inizio && g.Giorno <= fine)
            .Select(g => new { g.DipendenteId, g.Giorno }).ToListAsync()).Select(x => (x.DipendenteId, x.Giorno)).ToHashSet();
        var smart = (await db.GiorniSmartWorking.AsNoTracking()
            .Where(g => ids.Contains(g.DipendenteId) && g.Stato == StatoSmartWorking.Approvato && g.Giorno >= inizio && g.Giorno <= fine)
            .Select(g => new { g.DipendenteId, g.Giorno }).ToListAsync()).Select(x => (x.DipendenteId, x.Giorno)).ToHashSet();
        // Si contano i giorni lavorativi del contratto senza un'assenza approvata di tutta la giornata: in sede chi ha
        // segnato l'ufficio o lavora in presenza senza smart working quel giorno, da remoto tutti gli altri.
        (int Ufficio, int Remoto) Giorni(Dipendente d)
        {
            int u = 0, rem = 0;
            var giornate = approvate[d.Id].Where(r => !r.AOre).ToList();
            for (var g = inizio; g <= fine; g = g.AddDays(1))
            {
                var c = d.ContrattoAttivo(g);
                var giorno = g;
                if (c is null || !CalendarioLavorativo.IsLavorativo(g, c.GiorniLavorativiSettimana, azienda.SantoPatronoMese, azienda.SantoPatronoGiorno)) continue;
                if (giornate.Any(r => r.Dal <= giorno && r.Al >= giorno)) continue;
                var inSede = ufficio.Contains((d.Id, g)) || (!ServizioUfficio.LavoraADistanza(d, g) && !smart.Contains((d.Id, g)));
                if (inSede) u++; else rem++;
            }
            return (u, rem);
        }

        var straordinari = await db.Straordinari.AsNoTracking()
            .Where(x => ids.Contains(x.DipendenteId) && x.Stato == StatoRichiesta.Approvata && x.Giorno >= inizio && x.Giorno <= fine).ToListAsync();

        var righe = dipendenti.Select(d =>
        {
            var (u, rem) = Giorni(d);
            var propri = straordinari.Where(x => x.DipendenteId == d.Id).ToList();
            return new RigaFoglio(d.Id, d.NomeCompleto, d.Matricola, d.CodiceFiscale.Valore,
                assenze.GetValueOrDefault(d.Id, AssenzeDelMese.Nessuna),
                comunicazione?.RigaDi(d.Id)?.Voci ?? VociMensili.Nessuna)
            {
                HaFoto = d.FotoChiave is not null, Segnaposto = d.Segnaposto,
                Mansione = (d.ContrattoAttivo(fine) ?? d.ContrattoAttivo(inizio))?.Mansione,
                InUfficio = u, Remoto = rem,
                StraordinarioApprovato = propri.Where(x => !x.Festivo(azienda.SantoPatronoMese, azienda.SantoPatronoGiorno)).Sum(x => x.Ore),
                StraordinarioFestivoApprovato = propri.Where(x => x.Festivo(azienda.SantoPatronoMese, azienda.SantoPatronoGiorno)).Sum(x => x.Ore),
            };
        }).ToList();
        return new FoglioPresenze(azienda, anno, mese, comunicazione, righe);
    }

    /// <summary>
    /// Stato del mese per ogni azienda attiva, per il cruscotto del consulente: stato e data d'invio della comunicazione
    /// (null se l'azienda non l'ha ancora iniziata) e quante persone hanno un contratto nel mese, esclusi i professionisti esterni.
    /// </summary>
    public async Task<List<StatoAziendaMese>> StatoMeseAsync(int anno, int mese)
    {
        var inizio = new DateOnly(anno, mese, 1);
        var fine = inizio.AddMonths(1).AddDays(-1);
        var esterni = TipoContrattoExtensions.Esterni.ToList();
        var nelMese = Filtri.ContrattoAttivoNelPeriodo(inizio, fine);
        return await db.Aziende.AsNoTracking().Where(a => a.Attiva).OrderBy(a => a.RagioneSociale)
            .Select(a => new StatoAziendaMese(a.Id, a.RagioneSociale,
                db.ComunicazioniPresenze.Where(c => c.AziendaId == a.Id && c.Anno == anno && c.Mese == mese).Select(c => (StatoComunicazione?)c.Stato).FirstOrDefault(),
                db.ComunicazioniPresenze.Where(c => c.AziendaId == a.Id && c.Anno == anno && c.Mese == mese).Select(c => c.InviataIl).FirstOrDefault(),
                db.Dipendenti.Count(d => d.AziendaId == a.Id && d.Contratti.AsQueryable().Where(nelMese).Any(c => !esterni.Contains(c.Tipo)))))
            .ToListAsync();
    }

    /// <summary>CSV per il gestionale paghe: separatore ";" e virgola decimale, come lo apre Excel in italiano.</summary>
    public static byte[] Csv(FoglioPresenze f) => Csv([f], conAzienda: false);

    /// <summary>Più aziende nello stesso file (riepilogo del consulente): una colonna in più con l'azienda.</summary>
    public static byte[] Csv(IReadOnlyList<FoglioPresenze> fogli, bool conAzienda)
    {
        var it = CultureInfo.GetCultureInfo("it-IT");
        string N(decimal v) => v.ToString("0.##", it);
        static string T(string? s) => s is null ? "" : "\"" + s.Replace("\"", "\"\"").Replace('\n', ' ').Replace('\r', ' ') + "\"";

        var sb = new StringBuilder();
        sb.AppendLine((conAzienda ? "Azienda;Stato;" : "") + "Matricola;Dipendente;Codice fiscale;Giorni lavorati;Giorni in ufficio;Giorni da remoto;" +
                      "Ferie (ore);ROL e permessi (ore);Malattia (ore);Altre assenze (ore);" +
                      "Non retribuite (ore);Straordinario (ore);Straordinario festivo (ore);Buoni pasto;Premi (€);Rimborsi (€);Anticipi (€);Note");
        foreach (var f in fogli)
            foreach (var r in f.Righe)
            {
                var (a, v) = (r.Assenze, r.Voci);
                sb.AppendLine((conAzienda ? T(f.Azienda.RagioneSociale) + ";" + T(f.Stato.ToString()) + ";" : "") +
                    string.Join(';', T(r.Matricola), T(r.Nome), r.CodiceFiscale, r.GiorniLavorati, r.InUfficio, r.Remoto,
                    N(a.Ferie), N(a.Rol), N(a.Malattia), N(a.Altre),
                    N(v.OreNonRetribuite), N(v.OreStraordinario), N(v.OreStraordinarioFestivo), v.BuoniPasto, N(v.Premi), N(v.Rimborsi), N(v.Anticipi), T(v.Note)));
            }
        // Le note dell'azienda in fondo, solo nel file di una singola azienda.
        var f0 = fogli.Count == 1 ? fogli[0] : null;
        if (f0?.Comunicazione?.Note is { } note) sb.AppendLine().AppendLine("Note dell'azienda;" + T(note));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    /// <summary>Email ai consulenti dell'azienda (<see cref="QueryUtenti.EmailStudioAsync"/>): ha inviato le presenze del mese.</summary>
    public async Task AvvisaStudioAsync(Azienda azienda, ComunicazioneMensile c)
    {
        var link = WebUtility.HtmlEncode(accessi.ComponiUrl($"/Presenze/Foglio?aziendaId={azienda.Id}&anno={c.Anno}&mese={c.Mese}"));
        var periodo = $"{ServizioBustePaga.NomeMese(c.Mese)} {c.Anno}";
        foreach (var email in await db.EmailStudioAsync(azienda.Id))
            coda.Accoda(new MessaggioEmail(email, $"Presenze di {periodo.ToLower()} inviate: {azienda.RagioneSociale}",
                $"""
                <p>{WebUtility.HtmlEncode(azienda.RagioneSociale)} ha inviato presenze e variazioni di {periodo.ToLower()} ({c.Righe.Count} dipendenti).</p>
                <p><a href="{link}">{link}</a></p><p>Consulens HR</p>
                """));
    }

    /// <summary>Sollecito o riapertura: avvisa i referenti dell'azienda. Restituisce quanti sono stati avvisati.</summary>
    public async Task<int> AvvisaAziendaAsync(Azienda azienda, int anno, int mese, bool riaperta, string? motivo = null)
    {
        var periodo = $"{ServizioBustePaga.NomeMese(mese).ToLower()} {anno}";
        var link = WebUtility.HtmlEncode(accessi.ComponiUrl($"/Presenze/Foglio?anno={anno}&mese={mese}"));
        var destinatari = await db.EmailConRuoloAsync(Ruoli.Azienda, azienda.Id);
        foreach (var email in destinatari)
            coda.Accoda(new MessaggioEmail(email,
                riaperta ? $"Presenze di {periodo} da correggere" : $"Promemoria: presenze di {periodo}",
                $"""
                <p>{(riaperta
                    ? $"Il consulente ha riaperto la comunicazione delle presenze di {periodo} per una correzione."
                    : $"Il consulente attende presenze e variazioni di {periodo} per elaborare le buste paga.")}</p>
                {(string.IsNullOrWhiteSpace(motivo) ? "" : $"<p>Messaggio del consulente: {WebUtility.HtmlEncode(motivo)}</p>")}
                <p>Puoi compilarla e inviarla dal portale: <a href="{link}">{link}</a></p><p>Consulens HR</p>
                """));
        return destinatari.Count;
    }
}
