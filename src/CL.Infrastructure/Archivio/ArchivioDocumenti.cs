using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace CL.Infrastructure.Archivio;

/// <summary>
/// Archivio privato dei file (cedolini, CU, contratti firmati, foto e loghi). Le chiavi sono generate dall'applicazione,
/// mai dall'utente, e i file si leggono solo passando dai controller che verificano i permessi.
/// </summary>
public interface IArchivioDocumenti
{
    Task SalvaAsync(string chiave, Stream contenuto, CancellationToken ct = default);
    /// <summary>Il file, oppure null se non esiste.</summary>
    Task<Stream?> ApriAsync(string chiave, CancellationToken ct = default);
    Task EliminaAsync(string chiave, CancellationToken ct = default);
}

public sealed class OpzioniArchivio
{
    /// <summary>
    /// "Disco" (predefinito): i file nella cartella <see cref="Percorso"/>. "Database": i file nella tabella FileArchivio,
    /// per gli hosting senza disco persistente (es. il piano gratuito di Render, che azzera il disco a ogni riavvio).
    /// </summary>
    public string Tipo { get; set; } = "Disco";

    /// <summary>Cartella dei documenti su disco (fuori da wwwroot), usata con <see cref="Tipo"/> "Disco".</summary>
    public string Percorso { get; set; } = "";

    public bool NelDatabase => string.Equals(Tipo, "Database", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Le chiavi valide: solo lettere, cifre, trattini e barre, niente "..", percorsi assoluti o caratteri speciali.</summary>
internal static partial class ChiaviArchivio
{
    [GeneratedRegex(@"^[A-Za-z0-9\-]+(/[A-Za-z0-9\-]+)*\.(pdf|png|jpg|webp)$")]
    private static partial Regex Valida();

    public static string Controlla(string chiave) =>
        Valida().IsMatch(chiave) ? chiave : throw new ArgumentException("Chiave di archivio non valida.", nameof(chiave));
}

internal sealed class ArchivioSuDisco(IOptions<OpzioniArchivio> opzioni) : IArchivioDocumenti
{
    private string Percorso(string chiave)
    {
        ChiaviArchivio.Controlla(chiave);
        var radice = Path.GetFullPath(opzioni.Value.Percorso);
        var completo = Path.GetFullPath(Path.Combine(radice, chiave));
        if (!completo.StartsWith(radice + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Chiave di archivio non valida.", nameof(chiave));
        return completo;
    }

    public async Task SalvaAsync(string chiave, Stream contenuto, CancellationToken ct = default)
    {
        var percorso = Percorso(chiave);
        Directory.CreateDirectory(Path.GetDirectoryName(percorso)!);
        // Scrittura su file temporaneo e rinomina: mai un cedolino scritto a metà.
        var temporaneo = percorso + ".tmp";
        await using (var file = new FileStream(temporaneo, FileMode.Create, FileAccess.Write, FileShare.None))
            await contenuto.CopyToAsync(file, ct);
        File.Move(temporaneo, percorso, overwrite: false);
    }

    public Task<Stream?> ApriAsync(string chiave, CancellationToken ct = default)
    {
        var percorso = Percorso(chiave);
        return Task.FromResult<Stream?>(File.Exists(percorso)
            ? new FileStream(percorso, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null);
    }

    public Task EliminaAsync(string chiave, CancellationToken ct = default)
    {
        var percorso = Percorso(chiave);
        if (File.Exists(percorso)) File.Delete(percorso);
        return Task.CompletedTask;
    }
}
