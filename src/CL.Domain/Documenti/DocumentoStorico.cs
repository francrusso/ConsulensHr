using CL.Domain.Common;

namespace CL.Domain.Documenti;

/// <summary>Documenti degli anni passati che l'azienda porta dal consulente precedente.</summary>
public enum TipoDocumentoStorico { Cu, Modello770, UniEmens, Dm10, AutoliquidazioneInail, F24, Verbale, Contenzioso, Altro }

public static class TipoDocumentoStoricoExtensions
{
    public static string Descrizione(this TipoDocumentoStorico t) => t switch
    {
        TipoDocumentoStorico.Cu => "CU",
        TipoDocumentoStorico.Modello770 => "Modello 770",
        TipoDocumentoStorico.UniEmens => "UniEmens",
        TipoDocumentoStorico.Dm10 => "DM10",
        TipoDocumentoStorico.AutoliquidazioneInail => "Autoliquidazione INAIL",
        TipoDocumentoStorico.F24 => "F24",
        TipoDocumentoStorico.Verbale => "Verbale ispettivo",
        TipoDocumentoStorico.Contenzioso => "Contenzioso",
        _ => "Altro",
    };
}

/// <summary>Documento dello storico dell'azienda (anni precedenti al subentro), archiviato per anno.</summary>
public sealed class DocumentoStorico : Entity
{
    public Guid AziendaId { get; private set; }
    public TipoDocumentoStorico Tipo { get; private set; }
    public int Anno { get; private set; }
    public string? Descrizione { get; private set; }
    public string NomeFile { get; private set; } = null!;
    public string TipoFile { get; private set; } = null!;
    public string ChiaveArchivio { get; private set; } = null!;
    public long DimensioneByte { get; private set; }
    public DateTime CaricatoIl { get; private set; }
    public string CaricatoDa { get; private set; } = null!;

    private DocumentoStorico() { } // EF Core

    public DocumentoStorico(Guid aziendaId, TipoDocumentoStorico tipo, int anno, string? descrizione, string nomeFile, string tipoFile,
        string chiaveArchivio, long dimensioneByte, string caricatoDa, DateTime adesso)
    {
        if (aziendaId == Guid.Empty) throw new DomainException("Azienda obbligatoria.");
        if (!Enum.IsDefined(tipo)) throw new DomainException("Tipo di documento non valido.");
        if (anno is < 1990 or > 2100) throw new DomainException("Anno non valido.");
        if (tipo == TipoDocumentoStorico.Altro && string.IsNullOrWhiteSpace(descrizione)) throw new DomainException("Descrivi il documento.");
        if (dimensioneByte <= 0) throw new DomainException("Il file è vuoto.");
        AziendaId = aziendaId;
        Tipo = tipo;
        Anno = anno;
        Descrizione = Guard.Facoltativo(descrizione);
        NomeFile = Guard.NomeFile(nomeFile, "documento");
        TipoFile = Guard.NotEmpty(tipoFile, "Tipo di file");
        ChiaveArchivio = Guard.NotEmpty(chiaveArchivio, "Archivio");
        DimensioneByte = dimensioneByte;
        CaricatoDa = Guard.NotEmpty(caricatoDa, "Autore");
        CaricatoIl = adesso;
    }

    public string Titolo => Tipo == TipoDocumentoStorico.Altro && Descrizione is { } d ? d : Tipo.Descrizione();
}
