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
    AssenzeDelMese Assenze, VociMensili Voci);

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
        var dipendenti = await db.Dipendenti.AsNoTracking()
            .Where(d => d.AziendaId == aziendaId
                        && (d.Contratti.Any(c => c.DataInizio <= fine && (c.DataFine == null || c.DataFine >= inizio) && !esterni.Contains(c.Tipo))
                            || giaInRiga.Contains(d.Id)))
            .OrderBy(d => d.Cognome).ThenBy(d => d.Nome)
            .ToListAsync();

        // A comunicazione inviata le assenze sono quelle fotografate all'invio; in bozza si leggono dal portale.
        Dictionary<Guid, AssenzeDelMese> assenze;
        if (comunicazione is { Stato: not StatoComunicazione.Bozza })
            assenze = comunicazione.Righe.ToDictionary(r => r.DipendenteId, r => r.Assenze);
        else
        {
            var ids = dipendenti.Select(d => d.Id).ToList();
            var richieste = await db.RichiesteAssenza.AsNoTracking()
                .Where(r => ids.Contains(r.DipendenteId) && r.Stato == StatoRichiesta.Approvata && r.Dal <= fine && r.Al >= inizio)
                .ToListAsync();
            assenze = dipendenti.ToDictionary(d => d.Id, d => ComunicazioneMensile.AssenzeDi(
                richieste.Where(r => r.DipendenteId == d.Id), anno, mese,
                d.ContrattoAttivo(fine) ?? d.ContrattoAttivo(inizio) ?? d.Contratti.OrderByDescending(c => c.DataInizio).FirstOrDefault(),
                azienda.SantoPatronoMese, azienda.SantoPatronoGiorno));
        }

        var righe = dipendenti.Select(d => new RigaFoglio(d.Id, d.NomeCompleto, d.Matricola, d.CodiceFiscale.Valore,
            assenze.GetValueOrDefault(d.Id, AssenzeDelMese.Nessuna),
            comunicazione?.RigaDi(d.Id)?.Voci ?? VociMensili.Nessuna)).ToList();
        return new FoglioPresenze(azienda, anno, mese, comunicazione, righe);
    }

    /// <summary>Stato del mese per ogni azienda attiva, per il cruscotto dello studio.</summary>
    public async Task<List<StatoAziendaMese>> StatoMeseAsync(int anno, int mese)
    {
        var inizio = new DateOnly(anno, mese, 1);
        var fine = inizio.AddMonths(1).AddDays(-1);
        var esterni = TipoContrattoExtensions.Esterni.ToList();
        return await db.Aziende.AsNoTracking().Where(a => a.Attiva).OrderBy(a => a.RagioneSociale)
            .Select(a => new StatoAziendaMese(a.Id, a.RagioneSociale,
                db.ComunicazioniPresenze.Where(c => c.AziendaId == a.Id && c.Anno == anno && c.Mese == mese).Select(c => (StatoComunicazione?)c.Stato).FirstOrDefault(),
                db.ComunicazioniPresenze.Where(c => c.AziendaId == a.Id && c.Anno == anno && c.Mese == mese).Select(c => c.InviataIl).FirstOrDefault(),
                db.Dipendenti.Count(d => d.AziendaId == a.Id && d.Contratti.Any(c => c.DataInizio <= fine && (c.DataFine == null || c.DataFine >= inizio) && !esterni.Contains(c.Tipo)))))
            .ToListAsync();
    }

    /// <summary>CSV per il gestionale paghe: separatore ";" e virgola decimale, come lo apre Excel in italiano.</summary>
    public static byte[] Csv(FoglioPresenze f)
    {
        var it = CultureInfo.GetCultureInfo("it-IT");
        string N(decimal v) => v.ToString("0.##", it);
        static string T(string? s) => s is null ? "" : "\"" + s.Replace("\"", "\"\"").Replace('\n', ' ').Replace('\r', ' ') + "\"";

        var sb = new StringBuilder();
        sb.AppendLine("Matricola;Dipendente;Codice fiscale;Ferie (ore);ROL (ore);Ex-festività (ore);Malattia (ore);Altre assenze (ore);" +
                      "Non retribuite (ore);Straordinario (ore);Straordinario festivo (ore);Buoni pasto;Premi (€);Rimborsi (€);Anticipi (€);Note");
        foreach (var r in f.Righe)
        {
            var (a, v) = (r.Assenze, r.Voci);
            sb.AppendLine(string.Join(';', T(r.Matricola), T(r.Nome), r.CodiceFiscale, N(a.Ferie), N(a.Rol), N(a.ExFestivita), N(a.Malattia), N(a.Altre),
                N(v.OreNonRetribuite), N(v.OreStraordinario), N(v.OreStraordinarioFestivo), v.BuoniPasto, N(v.Premi), N(v.Rimborsi), N(v.Anticipi), T(v.Note)));
        }
        if (f.Comunicazione?.Note is { } note) sb.AppendLine().AppendLine("Note dell'azienda;" + T(note));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    public async Task AvvisaStudioAsync(Azienda azienda, ComunicazioneMensile c)
    {
        var link = WebUtility.HtmlEncode(accessi.ComponiUrl($"/Presenze/Foglio?aziendaId={azienda.Id}&anno={c.Anno}&mese={c.Mese}"));
        var periodo = $"{ServizioBustePaga.NomeMese(c.Mese)} {c.Anno}";
        foreach (var email in await EmailConRuolo(Ruoli.Consulente, null))
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
        var destinatari = await EmailConRuolo(Ruoli.Azienda, azienda.Id);
        foreach (var email in destinatari)
            coda.Accoda(new MessaggioEmail(email,
                riaperta ? $"Presenze di {periodo} da correggere" : $"Promemoria: presenze di {periodo}",
                $"""
                <p>{(riaperta
                    ? $"Lo studio ha riaperto la comunicazione delle presenze di {periodo} per una correzione."
                    : $"Lo studio attende presenze e variazioni di {periodo} per elaborare le buste paga.")}</p>
                {(string.IsNullOrWhiteSpace(motivo) ? "" : $"<p>Messaggio dello studio: {WebUtility.HtmlEncode(motivo)}</p>")}
                <p>Puoi compilarla e inviarla dal portale: <a href="{link}">{link}</a></p><p>Consulens HR</p>
                """));
        return destinatari.Count;
    }

    private async Task<List<string>> EmailConRuolo(string ruolo, Guid? aziendaId) =>
        (await (from u in db.Users
                join ur in db.UserRoles on u.Id equals ur.UserId
                join r in db.Roles on ur.RoleId equals r.Id
                where r.Name == ruolo && u.Attivo && u.EmailConfirmed && (aziendaId == null || u.AziendaId == aziendaId)
                select u.Email).ToListAsync()).OfType<string>().ToList();
}
