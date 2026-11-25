using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CL.Domain.BustePaga;
using CL.Domain.Common;
using CL.Domain.Notifiche;
using CL.Infrastructure.Archivio;
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

/// <summary>PDF ricevuto dal modulo: si apre solo quando serve (il tipo dichiarato dal browser non conta).</summary>
public sealed record FileCaricato(string Nome, Func<Stream> Apri, long Dimensione);

public sealed record EsitoCaricamento(List<(string File, string Dipendente)> Caricati, List<(string File, string Motivo)> Scartati, bool Pubblicati);

/// <summary>
/// Caricamento dei PDF dei cedolini, abbinamento al dipendente, pubblicazione e avviso via email.
/// </summary>
public sealed partial class ServizioBustePaga(
    AppDbContext db, IArchivioDocumenti archivio, ServizioNotifiche notifiche, TimeProvider clock)
{
    public const long DimensioneMassima = 10 * 1024 * 1024;
    private static readonly CultureInfo Italiano = new("it-IT");

    public static string NomeMese(int mese) => Italiano.TextInfo.ToTitleCase(Italiano.DateTimeFormat.GetMonthName(mese));

    [GeneratedRegex("[^A-Z0-9]+")]
    private static partial Regex Separatori();

    /// <summary>
    /// Carica più cedolini della stessa azienda e mese. Ogni file si abbina al dipendente se il nome contiene
    /// il suo codice fiscale oppure la matricola come parola a sé (es. "0042_ottobre.pdf"); con <paramref name="soloDipendente"/>
    /// (caricamento dalla scheda) va a quella persona qualunque sia il nome. I file scartati non bloccano gli altri,
    /// ma i cedolini validi si salvano tutti insieme: se il salvataggio fallisce si tolgono anche i PDF già archiviati.
    /// </summary>
    public async Task<EsitoCaricamento> CaricaAsync(Guid aziendaId, int anno, int mese, TipoCedolino tipo,
        IReadOnlyList<FileCaricato> files, bool pubblica, Guid? soloDipendente = null, decimal netto = 0)
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
            if (!dati.AsSpan().StartsWith("%PDF-"u8)) // riconosciuto dal contenuto, non dall'estensione
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

            // Lo stesso PDF (stessa impronta) non si carica due volte, né nello stesso invio né in uno precedente.
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
                busta = new BustaPaga(aziendaId, dip.Id, anno, mese, tipo, 0, netto, Path.GetFileName(f.Nome), chiave, hash, dati.Length, Adesso);
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

    /// <summary>
    /// Corrispondenza nel nome del file: codice fiscale ovunque, matricola come parola a sé (separata da spazi, trattini,
    /// underscore…) e senza badare agli zeri iniziali: "0042_ottobre.pdf" e "42.pdf" sono della matricola 42.
    /// </summary>
    internal static bool Corrisponde(string nomeFile, string codiceFiscale, string? matricola)
    {
        var nome = Path.GetFileNameWithoutExtension(nomeFile).ToUpperInvariant();
        if (nome.Contains(codiceFiscale, StringComparison.Ordinal)) return true;
        if (string.IsNullOrWhiteSpace(matricola)) return false;
        var m = matricola.Trim().ToUpperInvariant();
        var senzaZeri = m.TrimStart('0');
        return Separatori().Split(nome).Any(parola => parola == m || (senzaZeri.Length > 0 && parola.TrimStart('0') == senzaZeri));
    }

    /// <summary>Notifica a ogni persona (l'email è senza allegato: il cedolino si apre solo dal portale, dopo il login).</summary>
    private async Task AvvisaAsync(IReadOnlyCollection<BustaPaga> buste)
    {
        if (buste.Count == 0) return;
        var persone = (await db.PersoneAsync(buste.Select(b => b.DipendenteId))).ToLookup(d => d.DipendenteId);
        var messaggi = buste.SelectMany(b => persone[b.DipendenteId].Select(d =>
        {
            var cosa = b.Tipo == TipoCedolino.Ordinario
                ? $"la busta paga di {NomeMese(b.Mese).ToLower(Italiano)} {b.Anno}"
                : $"il cedolino {b.Tipo.Descrizione().ToLower(Italiano)} di {NomeMese(b.Mese).ToLower(Italiano)} {b.Anno}";
            return new MessaggioNotifica(d, $"Nuova busta paga disponibile: {NomeMese(b.Mese)} {b.Anno}",
                $"È disponibile {cosa}. Puoi consultarla e scaricarla dal portale.", "/BustePaga");
        }));
        await notifiche.InviaAsync(TipoNotifica.BustaPagaPubblicata, messaggi);
    }
}
