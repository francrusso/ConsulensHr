using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CL.Infrastructure.Archivio;

/// <summary>Un file dell'archivio salvato nel database (tabella FileArchivio), con <see cref="OpzioniArchivio.Tipo"/> "Database".</summary>
public sealed class FileArchivio
{
    public string Chiave { get; set; } = "";
    public byte[] Contenuto { get; set; } = [];
    public DateTime SalvatoIl { get; set; }
}

/// <summary>
/// Archivio nel database, per gli hosting con il disco che si azzera (es. il piano gratuito di Render).
/// Ogni operazione usa un proprio contesto: salvare un file non deve salvare anche le modifiche ancora aperte di chi lo chiama.
/// </summary>
internal sealed class ArchivioNelDatabase(IServiceScopeFactory scopes, TimeProvider clock) : IArchivioDocumenti
{
    public async Task SalvaAsync(string chiave, Stream contenuto, CancellationToken ct = default)
    {
        ChiaviArchivio.Controlla(chiave);
        using var memoria = new MemoryStream();
        await contenuto.CopyToAsync(memoria, ct);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Come su disco, una chiave già usata non si sovrascrive: l'inserimento fallisce sulla chiave primaria.
        db.FileArchivio.Add(new FileArchivio { Chiave = chiave, Contenuto = memoria.ToArray(), SalvatoIl = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(ct);
    }

    public async Task<Stream?> ApriAsync(string chiave, CancellationToken ct = default)
    {
        ChiaviArchivio.Controlla(chiave);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contenuto = await db.FileArchivio.Where(f => f.Chiave == chiave).Select(f => f.Contenuto).FirstOrDefaultAsync(ct);
        return contenuto is null ? null : new MemoryStream(contenuto, writable: false);
    }

    public async Task EliminaAsync(string chiave, CancellationToken ct = default)
    {
        ChiaviArchivio.Controlla(chiave);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.FileArchivio.Where(f => f.Chiave == chiave).ExecuteDeleteAsync(ct);
    }
}
