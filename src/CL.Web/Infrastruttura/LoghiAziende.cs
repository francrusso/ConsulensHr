using CL.Domain.Aziende;
using CL.Infrastructure.Archivio;

namespace CL.Web.Infrastruttura;

/// <summary>
/// Logo dell'azienda: PNG, JPEG o WebP fino a 1 MB, riconosciuti dal contenuto (non dall'estensione dichiarata).
/// SVG escluso: può contenere script. Il file sta nell'archivio privato e si legge passando dal controller.
/// </summary>
public sealed class LoghiAziende(IArchivioDocumenti archivio)
{
    public const long DimensioneMassima = 1024 * 1024;
    public const string FormatiAccettati = "image/png,image/jpeg,image/webp";

    /// <summary>Errore da mostrare all'utente, oppure null se il logo è stato salvato.</summary>
    public async Task<string?> SalvaAsync(Azienda azienda, IFormFile file)
    {
        if (file.Length == 0) return "Il file del logo è vuoto.";
        if (file.Length > DimensioneMassima) return "Il logo supera 1 MB: usa un'immagine più leggera.";

        var testa = new byte[12];
        await using (var s = file.OpenReadStream()) _ = await s.ReadAtLeastAsync(testa, testa.Length, throwOnEndOfStream: false);
        var estensione = Formato(testa);
        if (estensione is null) return "Il logo deve essere un'immagine PNG, JPEG o WebP.";

        var precedente = azienda.LogoChiave;
        var chiave = $"loghi/{azienda.Id}-{Guid.NewGuid():N}.{estensione}";
        await using (var s = file.OpenReadStream()) await archivio.SalvaAsync(chiave, s);
        azienda.ImpostaLogo(chiave);
        if (precedente is not null) await archivio.EliminaAsync(precedente);
        return null;
    }

    public async Task RimuoviAsync(Azienda azienda)
    {
        if (azienda.LogoChiave is { } chiave) await archivio.EliminaAsync(chiave);
        azienda.RimuoviLogo();
    }

    public static string TipoMime(string chiave) => Path.GetExtension(chiave) switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg",
    };

    private static string? Formato(byte[] b) =>
        b is [0x89, 0x50, 0x4E, 0x47, ..] ? "png"
        : b is [0xFF, 0xD8, 0xFF, ..] ? "jpg"
        : b is [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P'] ? "webp"
        : null;
}
