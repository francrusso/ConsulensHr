namespace CL.Infrastructure.Persistence;

/// <summary>
/// Aziende che l'utente della richiesta può vedere. Null = nessuna restrizione a livello di database
/// (super amministratore, avvio, migrazioni; azienda e dipendente hanno già i loro filtri nell'applicazione).
/// Per un consulente si riempie con le aziende a lui assegnate: i filtri globali di <see cref="AppDbContext"/>
/// nascondono tutto il resto (aziende, personale, documenti, richieste…), in ogni query.
/// </summary>
public sealed class PerimetroAziende
{
    public IReadOnlyCollection<Guid>? Consentite { get; set; }
}

/// <summary>Azienda assegnata a un consulente (o creata da lui).</summary>
public sealed class AssegnazioneAzienda
{
    public Guid AziendaId { get; set; }
    public string UserId { get; set; } = "";
    public DateTime AssegnataIl { get; set; }
}
