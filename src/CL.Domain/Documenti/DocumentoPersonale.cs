using CL.Domain.Common;

namespace CL.Domain.Documenti;

/// <summary>Documenti personali chiesti al nuovo assunto (o in corso di rapporto).</summary>
public enum TipoDocumentoPersonale { DocumentoIdentita, TesseraSanitaria, PermessoSoggiorno, ModuloDetrazioni, SceltaTfr, Altro }

public static class TipoDocumentoPersonaleExtensions
{
    public static string Descrizione(this TipoDocumentoPersonale t) => t switch
    {
        TipoDocumentoPersonale.DocumentoIdentita => "Documento d'identità",
        TipoDocumentoPersonale.TesseraSanitaria => "Tessera sanitaria",
        TipoDocumentoPersonale.PermessoSoggiorno => "Permesso di soggiorno",
        TipoDocumentoPersonale.ModuloDetrazioni => "Modulo detrazioni",
        TipoDocumentoPersonale.SceltaTfr => "Scelta destinazione TFR (TFR2)",
        _ => "Altro documento",
    };

    /// <summary>Documenti che scadono: senza data di scadenza non si caricano.</summary>
    public static bool HaScadenza(this TipoDocumentoPersonale t) =>
        t is TipoDocumentoPersonale.DocumentoIdentita or TipoDocumentoPersonale.TesseraSanitaria or TipoDocumentoPersonale.PermessoSoggiorno;
}

public enum StatoScadenza { SenzaScadenza, Valido, InScadenza, Scaduto }

/// <summary>Un documento personale di una persona (file nell'archivio privato), con l'eventuale scadenza.</summary>
public sealed class DocumentoPersonale : Entity
{
    /// <summary>Giorni prima della scadenza da cui il documento è "in scadenza".</summary>
    public const int GiorniPreavviso = 30;

    public Guid AziendaId { get; private set; }
    public Guid DipendenteId { get; private set; }
    public TipoDocumentoPersonale Tipo { get; private set; }
    public string? Descrizione { get; private set; }
    public DateOnly? Scadenza { get; private set; }
    public string NomeFile { get; private set; } = null!;
    public string TipoFile { get; private set; } = null!;
    public string ChiaveArchivio { get; private set; } = null!;
    public long DimensioneByte { get; private set; }
    public DateTime CaricatoIl { get; private set; }
    public string CaricatoDa { get; private set; } = null!;

    private DocumentoPersonale() { } // EF Core

    public DocumentoPersonale(Guid aziendaId, Guid dipendenteId, TipoDocumentoPersonale tipo, string? descrizione, DateOnly? scadenza,
        string nomeFile, string tipoFile, string chiaveArchivio, long dimensioneByte, string caricatoDa, DateTime adesso)
    {
        if (aziendaId == Guid.Empty || dipendenteId == Guid.Empty) throw new DomainException("Azienda e persona obbligatorie.");
        if (!Enum.IsDefined(tipo)) throw new DomainException("Tipo di documento non valido.");
        if (tipo.HaScadenza() && scadenza is null) throw new DomainException($"Indica la scadenza: {tipo.Descrizione().ToLowerInvariant()}.");
        if (tipo == TipoDocumentoPersonale.Altro && string.IsNullOrWhiteSpace(descrizione)) throw new DomainException("Descrivi il documento.");
        if (dimensioneByte <= 0) throw new DomainException("Il file è vuoto.");
        AziendaId = aziendaId;
        DipendenteId = dipendenteId;
        Tipo = tipo;
        Descrizione = Guard.Facoltativo(descrizione);
        Scadenza = scadenza;
        NomeFile = Guard.NomeFile(nomeFile, "documento");
        TipoFile = Guard.NotEmpty(tipoFile, "Tipo di file");
        ChiaveArchivio = Guard.NotEmpty(chiaveArchivio, "Archivio");
        DimensioneByte = dimensioneByte;
        CaricatoDa = Guard.NotEmpty(caricatoDa, "Autore");
        CaricatoIl = adesso;
    }

    public string Titolo => Descrizione is { } d && Tipo == TipoDocumentoPersonale.Altro ? d : Tipo.Descrizione();

    public StatoScadenza StatoIl(DateOnly oggi) => Scadenza switch
    {
        null => StatoScadenza.SenzaScadenza,
        { } s when s < oggi => StatoScadenza.Scaduto,
        { } s when s <= oggi.AddDays(GiorniPreavviso) => StatoScadenza.InScadenza,
        _ => StatoScadenza.Valido,
    };
}
