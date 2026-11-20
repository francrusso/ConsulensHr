using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CL.Domain.BustePaga;
using CL.Domain.Common;
using CL.Infrastructure.Archivio;
using CL.Infrastructure.Email;
using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

public static class TipoCedolinoExtensions
{
    public static string Descrizione(this TipoCedolino t) => t switch
    {
        TipoCedolino.Ordinario => "Mensile",
        TipoCedolino.Tredicesima => "Tredicesima",
        TipoCedolino.Quattordicesima => "Quattordicesima",
        TipoCedolino.Conguaglio => "Conguaglio",
        TipoCedolino.Integrativo => "Integrativo",
        _ => t.ToString(),
    };
}

/// <summary>Riga di elenco cedolini (proprietà init: EF può filtrare dopo la proiezione).</summary>
public sealed record BustaRiga
{
    public Guid Id { get; init; }
    public Guid DipendenteId { get; init; }
    public required string Dipendente { get; init; }
    public required string Azienda { get; init; }
    public int Anno { get; init; }
    public int Mese { get; init; }
    public TipoCedolino Tipo { get; init; }
    public decimal Netto { get; init; }
    public StatoBustaPaga Stato { get; init; }
    public DateTime? PubblicataIl { get; init; }
    public DateTime? VisualizzataIl { get; init; }

    public string Periodo => ServizioBustePaga.NomeMese(Mese) + " " + Anno;
}

public sealed record FileCaricato(string Nome, string Contenuto, Func<Stream> Apri, long Dimensione);

public sealed record EsitoCaricamento(List<(string File, string Dipendente)> Caricati, List<(string File, string Motivo)> Scartati, bool Pubblicati);

/// <summary>
/// Caricamento dei PDF dei cedolini, abbinamento al dipendente, pubblicazione e avviso via email.
/// </summary>
public sealed partial class ServizioBustePaga(
    AppDbContext db, IArchivioDocumenti archivio, CodaEmail coda, ServizioAccessi accessi, TimeProvider clock)
{
    public const long DimensioneMassima = 10 * 1024 * 1024;
    private static readonly CultureInfo Italiano = new("it-IT");

    public static string NomeMese(int mese) => Italiano.TextInfo.ToTitleCase(Italiano.DateTimeFormat.GetMonthName(mese));

    [GeneratedRegex("[^A-Z0-9]+")]
    private static partial Regex Separatori();

    /// <summary>
    /// Carica più cedolini della stessa azienda e mese. Ogni file si abbina al dipendente se il nome contiene
    /// il suo codice fiscale oppure la matricola come parola a sé (es. "0042_ottobre.pdf").
    /// </summary>
    public async Task<EsitoCaricamento> CaricaAsync(Guid aziendaId, int anno, int mese, TipoCedolino tipo,
        IReadOnlyList<FileCaricato> files, bool pubblica, Guid? soloDipendente = null, decimal lordo = 0, decimal netto = 0)
    {
        var dipendenti = await db.Dipendenti.AsNoTracking().IgnoreAutoIncludes()
            .Where(d => d.AziendaId == aziendaId && (soloDipendente == null || d.Id == soloDipendente))
            .Select(d => new { d.Id, d.Nome, d.Cognome, Cf = (string)(object)d.CodiceFiscale, d.Matricola })
            .ToListAsync();

        var caricati = new List<(string, string)>();
        var scartati = new List<(string, string)>();
        var nuove = new List<BustaPaga>();
        var hashVisti = new HashSet<string>();

        foreach (var f in files)
        {
            if (f.Dimensione <= 0) { scartati.Add((f.Nome, "file vuoto")); continue; }
            if (f.Dimensione > DimensioneMassima) { scartati.Add((f.Nome, "supera 10 MB")); continue; }

            byte[] dati;
            await using (var s = f.Apri())
            using (var ms = new MemoryStream())
            {
                await s.CopyToAsync(ms);
                dati = ms.ToArray();
            }
            if (dati.Length < 5 || dati[0] != '%' || dati[1] != 'P' || dati[2] != 'D' || dati[3] != 'F' || dati[4] != '-')
            {
                scartati.Add((f.Nome, "non è un PDF"));
                continue;
            }

            // Abbinamento: con un solo dipendente indicato (caricamento dalla sua scheda) non serve cercare.
            var trovati = soloDipendente is not null
                ? dipendenti
                : dipendenti.Where(d => Corrisponde(f.Nome, d.Cf, d.Matricola)).ToList();
            if (trovati.Count != 1)
            {
                scartati.Add((f.Nome, trovati.Count == 0
                    ? "nessun dipendente con codice fiscale o matricola nel nome del file"
                    : "il nome del file corrisponde a più dipendenti"));
                continue;
            }
            var dip = trovati[0];

            var hash = Convert.ToHexStringLower(SHA256.HashData(dati));
            if (!hashVisti.Add(hash) || await db.BustePaga.AnyAsync(b => b.HashSha256 == hash))
            {
                scartati.Add((f.Nome, "questo PDF è già stato caricato"));
                continue;
            }

            var chiave = $"{aziendaId}/{anno}/{mese:00}/{Guid.NewGuid()}.pdf"; // nome casuale: non rivela chi è il dipendente
            BustaPaga busta;
            try
            {
                busta = new BustaPaga(aziendaId, dip.Id, anno, mese, tipo, lordo, netto, Path.GetFileName(f.Nome), chiave, hash, dati.Length, Adesso);
            }
            catch (DomainException ex)
            {
                scartati.Add((f.Nome, ex.Message));
                continue;
            }

            await using (var ms = new MemoryStream(dati))
                await archivio.SalvaAsync(chiave, ms);
            nuove.Add(busta);
            caricati.Add((f.Nome, $"{dip.Cognome} {dip.Nome}"));
        }

        if (nuove.Count == 0) return new EsitoCaricamento(caricati, scartati, false);

        if (pubblica) foreach (var b in nuove) b.Pubblica(Adesso);
        db.BustePaga.AddRange(nuove);
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            foreach (var b in nuove) await archivio.EliminaAsync(b.ChiaveArchivio); // niente file orfani
            throw;
        }

        if (pubblica) await AvvisaAsync(nuove);
        return new EsitoCaricamento(caricati, scartati, pubblica);
    }

    /// <summary>Pubblica le bozze indicate e avvisa i dipendenti. Restituisce quante ne ha pubblicate.</summary>
    public async Task<int> PubblicaAsync(IReadOnlyCollection<Guid> ids)
    {
        var bozze = await db.BustePaga.Where(b => ids.Contains(b.Id) && b.Stato == StatoBustaPaga.Bozza).ToListAsync();
        foreach (var b in bozze) b.Pubblica(Adesso);
        await db.SaveChangesAsync();
        await AvvisaAsync(bozze);
        return bozze.Count;
    }

    private DateTime Adesso => clock.GetUtcNow().UtcDateTime;

    /// <summary>Corrispondenza nel nome del file: codice fiscale ovunque, matricola come parola a sé.</summary>
    internal static bool Corrisponde(string nomeFile, string codiceFiscale, string? matricola)
    {
        var nome = Path.GetFileNameWithoutExtension(nomeFile).ToUpperInvariant();
        if (nome.Contains(codiceFiscale, StringComparison.Ordinal)) return true;
        if (string.IsNullOrWhiteSpace(matricola)) return false;
        var m = matricola.Trim().ToUpperInvariant();
        return Separatori().Split(nome).Any(parola => parola == m || (parola.TrimStart('0') == m.TrimStart('0') && m.TrimStart('0').Length > 0));
    }

    /// <summary>Email di avviso (senza allegato: il cedolino si apre solo dal portale, dopo il login).</summary>
    private async Task AvvisaAsync(IReadOnlyCollection<BustaPaga> buste)
    {
        if (buste.Count == 0) return;
        var ids = buste.Select(b => b.DipendenteId).Distinct().ToList();
        var destinatari = await db.Users.AsNoTracking()
            .Where(u => u.DipendenteId != null && ids.Contains(u.DipendenteId.Value) && u.Attivo && u.EmailConfirmed)
            .Select(u => new { u.DipendenteId, u.Email, u.Nome })
            .ToListAsync();
        var link = WebUtility.HtmlEncode(accessi.ComponiUrl("/BustePaga"));

        foreach (var b in buste)
        {
            var u = destinatari.FirstOrDefault(x => x.DipendenteId == b.DipendenteId);
            if (u?.Email is null) continue;
            var cosa = b.Tipo == TipoCedolino.Ordinario
                ? $"la busta paga di {NomeMese(b.Mese).ToLower(Italiano)} {b.Anno}"
                : $"il cedolino {b.Tipo.Descrizione().ToLower(Italiano)} di {NomeMese(b.Mese).ToLower(Italiano)} {b.Anno}";
            coda.Accoda(new MessaggioEmail(u.Email, $"Nuova busta paga disponibile: {NomeMese(b.Mese)} {b.Anno}",
                $"""
                <p>Ciao {WebUtility.HtmlEncode(u.Nome)},</p>
                <p>è disponibile {cosa}. Puoi consultarla e scaricarla dal portale:</p>
                <p><a href="{link}">{link}</a></p>
                <p>Consulens HR</p>
                """));
        }
    }
}
