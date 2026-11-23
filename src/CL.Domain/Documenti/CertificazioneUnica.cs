using CL.Domain.Common;

namespace CL.Domain.Documenti;

/// <summary>
/// Certificazione Unica (CU) di un dipendente per un anno d'imposta: il PDF sta nell'archivio privato.
/// Una sola CU per persona e anno; caricarne un'altra la sostituisce (es. CU rettificata).
/// </summary>
public sealed class CertificazioneUnica : Entity
{
    public Guid AziendaId { get; private set; }
    public Guid DipendenteId { get; private set; }
    /// <summary>Anno dei redditi certificati (la CU 2026 riguarda i redditi 2025).</summary>
    public int AnnoImposta { get; private set; }
    public string NomeFile { get; private set; } = null!;
    public string ChiaveArchivio { get; private set; } = null!;
    public long DimensioneByte { get; private set; }
    public DateTime CaricataIl { get; private set; }
    public string CaricataDa { get; private set; } = null!;
    public DateTime? VisualizzataIl { get; private set; }

    private CertificazioneUnica() { } // EF Core

    public CertificazioneUnica(Guid aziendaId, Guid dipendenteId, int annoImposta, string nomeFile, string chiaveArchivio,
        long dimensioneByte, string caricataDa, DateTime adesso)
    {
        if (aziendaId == Guid.Empty || dipendenteId == Guid.Empty) throw new DomainException("Azienda e dipendente obbligatori.");
        AziendaId = aziendaId;
        DipendenteId = dipendenteId;
        AnnoImposta = annoImposta is >= 2000 and <= 2100 ? annoImposta : throw new DomainException("Anno non valido.");
        Sostituisci(nomeFile, chiaveArchivio, dimensioneByte, caricataDa, adesso);
    }

    /// <summary>Nuovo file per la stessa persona e anno: la presa visione si azzera, il dipendente deve riaprirla.</summary>
    public void Sostituisci(string nomeFile, string chiaveArchivio, long dimensioneByte, string caricataDa, DateTime adesso)
    {
        if (dimensioneByte <= 0) throw new DomainException("Il file della CU è vuoto.");
        NomeFile = Guard.NomeFile(nomeFile, $"CU_{AnnoImposta}.pdf");
        ChiaveArchivio = Guard.NotEmpty(chiaveArchivio, "Archivio");
        DimensioneByte = dimensioneByte;
        CaricataDa = Guard.NotEmpty(caricataDa, "Autore");
        CaricataIl = adesso;
        VisualizzataIl = null;
    }

    public void RegistraVisualizzazione(DateTime adesso) => VisualizzataIl ??= adesso;
}
