using CL.Domain.Aziende;
using CL.Domain.Dipendenti;
using CL.Infrastructure.Archivio;

namespace CL.Web.Infrastruttura;

/// <summary>
/// Immagini caricate dagli utenti (loghi delle aziende, foto dei dipendenti): PNG, JPEG o WebP riconosciuti dal
/// contenuto e non dall'estensione dichiarata. SVG escluso perché può contenere script. I file stanno nell'archivio
/// privato e si leggono solo passando dai controller che verificano i permessi.
/// </summary>
public sealed class Immagini(IArchivioDocumenti archivio)
{
    public const long DimensioneMassima = 2 * 1024 * 1024;
    public const string FormatiAccettati = "image/png,image/jpeg,image/webp";

    /// <summary>
    /// Salva l'immagine in <paramref name="cartella"/> con un nome nuovo ed elimina subito la <paramref name="precedente"/>
    /// (la nuova chiave la registra poi il chiamante sull'entità). Restituisce la chiave o il messaggio d'errore per l'utente.
    /// </summary>
    public async Task<(string? Chiave, string? Errore)> SalvaAsync(string cartella, Guid proprietario, IFormFile file, string? precedente, long massimo)
    {
        if (file.Length == 0) return (null, "Il file è vuoto.");
        if (file.Length > massimo) return (null, $"L'immagine supera {massimo / 1024 / 1024} MB: usane una più leggera.");

        var testa = new byte[12];
        await using (var s = file.OpenReadStream()) _ = await s.ReadAtLeastAsync(testa, testa.Length, throwOnEndOfStream: false);
        if (Formato(testa) is not { } estensione) return (null, "L'immagine deve essere PNG, JPEG o WebP.");

        var chiave = $"{cartella}/{proprietario}-{Guid.NewGuid():N}.{estensione}";
        await using (var s = file.OpenReadStream()) await archivio.SalvaAsync(chiave, s);
        if (precedente is not null) await archivio.EliminaAsync(precedente);
        return (chiave, null);
    }

    public Task EliminaAsync(string? chiave) => chiave is null ? Task.CompletedTask : archivio.EliminaAsync(chiave);

    public static string TipoMime(string chiave) => Path.GetExtension(chiave) switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg",
    };

    /// <summary>Estensione dai primi byte del file ("firma" di PNG, JPEG e WebP), null se non è uno dei tre formati.</summary>
    private static string? Formato(byte[] b) =>
        b is [0x89, 0x50, 0x4E, 0x47, ..] ? "png"
        : b is [0xFF, 0xD8, 0xFF, ..] ? "jpg"
        : b is [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P'] ? "webp"
        : null;
}

/// <summary>Logo dell'azienda (massimo 1 MB), nella cartella <c>loghi/</c> dell'archivio.</summary>
public sealed class LoghiAziende(Immagini immagini)
{
    public const long DimensioneMassima = 1024 * 1024;
    public const string FormatiAccettati = Immagini.FormatiAccettati;

    /// <summary>Errore da mostrare all'utente, oppure null se il logo è stato salvato.</summary>
    public async Task<string?> SalvaAsync(Azienda azienda, IFormFile file)
    {
        var (chiave, errore) = await immagini.SalvaAsync("loghi", azienda.Id, file, azienda.LogoChiave, DimensioneMassima);
        if (errore is not null) return errore.Replace("L'immagine deve essere", "Il logo deve essere un'immagine");
        azienda.ImpostaLogo(chiave!);
        return null;
    }

    public async Task RimuoviAsync(Azienda azienda)
    {
        await immagini.EliminaAsync(azienda.LogoChiave);
        azienda.RimuoviLogo();
    }

    public static string TipoMime(string chiave) => Immagini.TipoMime(chiave);
}

/// <summary>Foto del dipendente (massimo 2 MB), nella cartella <c>foto/</c> dell'archivio: si vede negli elenchi, nell'organigramma e nella scheda.</summary>
public sealed class FotoDipendenti(Immagini immagini)
{
    public async Task<string?> SalvaAsync(Dipendente dipendente, IFormFile file)
    {
        var (chiave, errore) = await immagini.SalvaAsync("foto", dipendente.Id, file, dipendente.FotoChiave, Immagini.DimensioneMassima);
        if (errore is not null) return errore.Replace("L'immagine deve essere", "La foto deve essere un'immagine");
        dipendente.ImpostaFoto(chiave!);
        return null;
    }

    public async Task RimuoviAsync(Dipendente dipendente)
    {
        await immagini.EliminaAsync(dipendente.FotoChiave);
        dipendente.RimuoviFoto();
    }
}
