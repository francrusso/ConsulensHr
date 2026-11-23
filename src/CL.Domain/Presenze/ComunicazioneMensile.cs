using CL.Domain.Assenze;
using CL.Domain.Common;
using CL.Domain.Dipendenti;

namespace CL.Domain.Presenze;

public enum StatoComunicazione
{
    /// <summary>L'azienda la sta compilando.</summary>
    Bozza,
    /// <summary>Inviata al consulente: non più modificabile dall'azienda.</summary>
    Inviata,
    /// <summary>Il consulente ha elaborato le paghe del mese.</summary>
    Elaborata,
}

/// <summary>Variazioni del mese che l'azienda comunica per un dipendente.</summary>
public sealed record VociMensili(
    decimal OreStraordinario, decimal OreStraordinarioFestivo, decimal OreNonRetribuite,
    int BuoniPasto, decimal Premi, decimal Rimborsi, decimal Anticipi, string? Note)
{
    public static readonly VociMensili Nessuna = new(0, 0, 0, 0, 0, 0, 0, null);
}

/// <summary>Ore di assenza del mese ricavate da ferie e permessi registrati nel portale.</summary>
public sealed record AssenzeDelMese(decimal Ferie, decimal Rol, decimal Malattia, decimal Altre)
{
    public static readonly AssenzeDelMese Nessuna = new(0, 0, 0, 0);
    public decimal Totale => Ferie + Rol + Malattia + Altre;
}

/// <summary>
/// Comunicazione mensile delle presenze e delle variazioni che l'azienda invia al consulente per le paghe.
/// Le assenze arrivano dal portale; all'invio vengono fotografate, così il consulente elabora esattamente ciò che ha ricevuto.
/// </summary>
public sealed class ComunicazioneMensile : Entity
{
    private readonly List<RigaPresenze> _righe = [];

    public Guid AziendaId { get; private set; }
    public int Anno { get; private set; }
    public int Mese { get; private set; }
    public StatoComunicazione Stato { get; private set; }
    public string? Note { get; private set; }
    public DateTime? InviataIl { get; private set; }
    public string? InviataDa { get; private set; }
    public DateTime? ElaborataIl { get; private set; }
    public string? ElaborataDa { get; private set; }

    public IReadOnlyCollection<RigaPresenze> Righe => _righe.AsReadOnly();

    private ComunicazioneMensile() { } // EF Core

    public ComunicazioneMensile(Guid aziendaId, int anno, int mese)
    {
        if (anno is < 2000 or > 2100) throw new DomainException("Anno non valido.");
        if (mese is < 1 or > 12) throw new DomainException("Mese non valido.");
        AziendaId = aziendaId;
        Anno = anno;
        Mese = mese;
    }

    public RigaPresenze? RigaDi(Guid dipendenteId) => _righe.FirstOrDefault(r => r.DipendenteId == dipendenteId);

    /// <summary>La riga del dipendente, creata se non c'è ancora.</summary>
    private RigaPresenze Riga(Guid dipendenteId)
    {
        var riga = RigaDi(dipendenteId);
        if (riga is null) _righe.Add(riga = new RigaPresenze(Id, dipendenteId));
        return riga;
    }

    public void ImpostaVoci(Guid dipendenteId, VociMensili voci)
    {
        RichiediBozza();
        Riga(dipendenteId).Imposta(voci);
    }

    public void ImpostaNote(string? note)
    {
        RichiediBozza();
        Note = Guard.Facoltativo(note);
    }

    /// <summary>
    /// Invia al consulente: crea le righe mancanti per i dipendenti in forza e fotografa le loro assenze del mese.
    /// </summary>
    public void Invia(IReadOnlyDictionary<Guid, AssenzeDelMese> assenze, string da, DateTime adesso)
    {
        RichiediBozza();
        if (assenze.Count == 0) throw new DomainException("Nessun dipendente in forza nel mese: non c'è nulla da inviare.");
        foreach (var (dipendenteId, a) in assenze)
            Riga(dipendenteId).FotografaAssenze(a);
        Stato = StatoComunicazione.Inviata;
        InviataIl = adesso;
        InviataDa = da;
    }

    public void SegnaElaborata(string da, DateTime adesso)
    {
        if (Stato != StatoComunicazione.Inviata) throw new DomainException("Si possono elaborare solo le comunicazioni inviate.");
        Stato = StatoComunicazione.Elaborata;
        ElaborataIl = adesso;
        ElaborataDa = da;
    }

    /// <summary>Il consulente la rimanda all'azienda per correzioni.</summary>
    public void Riapri()
    {
        if (Stato == StatoComunicazione.Bozza) throw new DomainException("La comunicazione è già modificabile.");
        Stato = StatoComunicazione.Bozza;
        ElaborataIl = null;
        ElaborataDa = null;
    }

    private void RichiediBozza()
    {
        if (Stato != StatoComunicazione.Bozza)
            throw new DomainException("La comunicazione è già stata inviata al consulente: per modificarla chiedi al consulente di riaprirla.");
    }

    /// <summary>
    /// Ore di assenza del mese per tipo, dalle richieste approvate. Un'assenza a cavallo di due mesi
    /// si ripartisce in proporzione ai giorni lavorativi che cadono in ciascun mese.
    /// </summary>
    public static AssenzeDelMese AssenzeDi(IEnumerable<RichiestaAssenza> richieste, int anno, int mese,
        Contratto? contratto, int? patronoMese, int? patronoGiorno)
    {
        var inizio = new DateOnly(anno, mese, 1);
        var fine = inizio.AddMonths(1).AddDays(-1);
        var giorniSettimana = contratto?.GiorniLavorativiSettimana ?? 5;
        decimal ferie = 0, rol = 0, mal = 0, altre = 0;

        foreach (var r in richieste.Where(r => r.Stato == StatoRichiesta.Approvata && r.SovrapponeA(inizio, fine)))
        {
            decimal ore;
            if (r.Dal >= inizio && r.Al <= fine) ore = r.Ore;
            else
            {
                var tutti = CalendarioLavorativo.GiorniLavorativi(r.Dal, r.Al, giorniSettimana, patronoMese, patronoGiorno).Count();
                var nelMese = CalendarioLavorativo.GiorniLavorativi(r.Dal < inizio ? inizio : r.Dal, r.Al > fine ? fine : r.Al,
                    giorniSettimana, patronoMese, patronoGiorno).Count();
                ore = tutti == 0 ? 0 : Math.Round(r.Ore * nelMese / tutti, 2);
            }

            switch (r.Tipo)
            {
                case TipoAssenza.Ferie: ferie += ore; break;
                case TipoAssenza.Rol: rol += ore; break;
                case TipoAssenza.Malattia: mal += ore; break;
                default: altre += ore; break;
            }
        }
        return new AssenzeDelMese(ferie, rol, mal, altre);
    }
}

public sealed class RigaPresenze : Entity
{
    public Guid ComunicazioneId { get; private set; }
    public Guid DipendenteId { get; private set; }

    // Inserite dall'azienda
    public decimal OreStraordinario { get; private set; }
    public decimal OreStraordinarioFestivo { get; private set; }
    public decimal OreNonRetribuite { get; private set; }
    public int BuoniPasto { get; private set; }
    public decimal Premi { get; private set; }
    public decimal Rimborsi { get; private set; }
    public decimal Anticipi { get; private set; }
    public string? Note { get; private set; }

    // Dal portale, fotografate all'invio
    public decimal OreFerie { get; private set; }
    public decimal OreRol { get; private set; }
    public decimal OreMalattia { get; private set; }
    public decimal OreAltreAssenze { get; private set; }

    private RigaPresenze() { } // EF Core

    internal RigaPresenze(Guid comunicazioneId, Guid dipendenteId)
    {
        ComunicazioneId = comunicazioneId;
        DipendenteId = dipendenteId;
    }

    public VociMensili Voci => new(OreStraordinario, OreStraordinarioFestivo, OreNonRetribuite, BuoniPasto, Premi, Rimborsi, Anticipi, Note);
    public AssenzeDelMese Assenze => new(OreFerie, OreRol, OreMalattia, OreAltreAssenze);

    internal void Imposta(VociMensili v)
    {
        if (v.OreStraordinario + v.OreStraordinarioFestivo > 250) throw new DomainException("Ore di straordinario non plausibili per un mese.");
        if (v.BuoniPasto is < 0 or > 31) throw new DomainException("Buoni pasto: al massimo uno al giorno.");
        OreStraordinario = Guard.NonNegative(v.OreStraordinario, "Straordinario");
        OreStraordinarioFestivo = Guard.NonNegative(v.OreStraordinarioFestivo, "Straordinario festivo");
        OreNonRetribuite = Guard.NonNegative(v.OreNonRetribuite, "Ore non retribuite");
        BuoniPasto = v.BuoniPasto;
        Premi = Guard.NonNegative(v.Premi, "Premi");
        Rimborsi = Guard.NonNegative(v.Rimborsi, "Rimborsi");
        Anticipi = Guard.NonNegative(v.Anticipi, "Anticipi");
        Note = Guard.Facoltativo(v.Note);
    }

    internal void FotografaAssenze(AssenzeDelMese a)
    {
        OreFerie = a.Ferie;
        OreRol = a.Rol;
        OreMalattia = a.Malattia;
        OreAltreAssenze = a.Altre;
    }
}
