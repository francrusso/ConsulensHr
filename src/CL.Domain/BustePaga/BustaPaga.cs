using CL.Domain.Common;

namespace CL.Domain.BustePaga;

public enum TipoCedolino
{
    Ordinario,
    Tredicesima,
    Quattordicesima,
    Conguaglio,
    Integrativo,
}

public enum StatoBustaPaga
{
    Bozza,
    Pubblicata,
    Ritirata,
}

/// <summary>
/// Cedolino elaborato dal gestionale paghe e distribuito al dipendente tramite il portale.
/// Il PDF è conservato nello storage documentale; qui teniamo metadati, impronta e tracciatura della consegna.
/// </summary>
public sealed class BustaPaga : Entity
{
    public Guid AziendaId { get; private set; }
    public Guid DipendenteId { get; private set; }
    public int Anno { get; private set; }
    public int Mese { get; private set; }
    public TipoCedolino Tipo { get; private set; }

    public decimal Lordo { get; private set; }
    public decimal Netto { get; private set; }

    public string NomeFile { get; private set; } = null!;
    /// <summary>Chiave del file nello storage (non un percorso esposto all'utente).</summary>
    public string ChiaveArchivio { get; private set; } = null!;
    /// <summary>Impronta del PDF (unica nel database): lo stesso file non si carica due volte.</summary>
    public string HashSha256 { get; private set; } = null!;
    public long DimensioneByte { get; private set; }

    public StatoBustaPaga Stato { get; private set; }
    public DateTime CaricataIl { get; private set; }
    public DateTime? PubblicataIl { get; private set; }
    /// <summary>Prima apertura da parte del dipendente: vale come presa visione/consegna.</summary>
    public DateTime? VisualizzataIl { get; private set; }

    private BustaPaga() { } // EF Core

    public BustaPaga(Guid aziendaId, Guid dipendenteId, int anno, int mese, TipoCedolino tipo,
        decimal lordo, decimal netto, string nomeFile, string chiaveArchivio, string hashSha256, long dimensioneByte,
        DateTime adesso)
    {
        if (anno is < 2000 or > 2100) throw new DomainException("Anno non valido.");
        if (mese is < 1 or > 12) throw new DomainException("Mese non valido.");
        // Importi facoltativi (0 = non indicato): il controllo vale solo se il lordo è stato inserito.
        if (lordo > 0 && netto > lordo) throw new DomainException("Il netto non può superare il lordo.");
        Guard.NonNegative(netto, "Netto");
        if (dimensioneByte <= 0) throw new DomainException("Il file del cedolino è vuoto.");

        AziendaId = aziendaId;
        DipendenteId = dipendenteId;
        Anno = anno;
        Mese = mese;
        Tipo = tipo;
        Lordo = Guard.NonNegative(lordo, "Lordo");
        Netto = netto;
        NomeFile = Guard.NotEmpty(nomeFile, "Nome file");
        ChiaveArchivio = Guard.NotEmpty(chiaveArchivio, "Chiave archivio");
        HashSha256 = Guard.NotEmpty(hashSha256, "Hash").ToLowerInvariant();
        DimensioneByte = dimensioneByte;
        CaricataIl = adesso;
        Stato = StatoBustaPaga.Bozza;
    }

    public string Periodo => $"{Mese:00}/{Anno}";

    public void Pubblica(DateTime adesso)
    {
        if (Stato != StatoBustaPaga.Bozza) throw new DomainException("Solo un cedolino in bozza può essere pubblicato.");
        Stato = StatoBustaPaga.Pubblicata;
        PubblicataIl = adesso;
    }

    /// <summary>Ritira un cedolino pubblicato per errore (resta in archivio, non visibile al dipendente).</summary>
    public void Ritira()
    {
        if (Stato != StatoBustaPaga.Pubblicata) throw new DomainException("Il cedolino non è pubblicato.");
        Stato = StatoBustaPaga.Ritirata;
    }

    public void RegistraVisualizzazione(DateTime adesso)
    {
        if (Stato != StatoBustaPaga.Pubblicata) throw new DomainException("Il cedolino non è disponibile.");
        VisualizzataIl ??= adesso;
    }
}
