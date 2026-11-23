using CL.Domain.Common;

namespace CL.Domain.Assenze;

public enum StatoRichiesta
{
    InAttesa,
    Approvata,
    Rifiutata,
    Annullata,
}

/// <summary>
/// Richiesta di ferie/permesso/assenza. Le ore sono già calcolate sui giorni lavorativi
/// (vedi <see cref="PianificatoreAssenze"/>), così il saldo non dipende da ricalcoli futuri del calendario.
/// </summary>
public sealed class RichiestaAssenza : Entity
{
    public Guid DipendenteId { get; private set; }
    public TipoAssenza Tipo { get; private set; }
    public DateOnly Dal { get; private set; }
    public DateOnly Al { get; private set; }
    /// <summary>True per i permessi a ore all'interno di una singola giornata.</summary>
    public bool AOre { get; private set; }
    public decimal Ore { get; private set; }
    public string? Note { get; private set; }
    /// <summary>Numero di protocollo del certificato medico (malattia).</summary>
    public string? ProtocolloCertificato { get; private set; }

    public StatoRichiesta Stato { get; private set; }
    public DateTime RichiestaIl { get; private set; }
    public string? DecisaDa { get; private set; }
    public DateTime? DecisaIl { get; private set; }
    public string? MotivoRifiuto { get; private set; }
    /// <summary>Ferie generate da una chiusura aziendale (si tolgono insieme alla chiusura).</summary>
    public Guid? ChiusuraId { get; private set; }
    /// <summary>Il dipendente ha chiesto di cancellare una richiesta già approvata: decide l'azienda.</summary>
    public DateTime? AnnullamentoRichiestoIl { get; private set; }
    public string? MotivoAnnullamento { get; private set; }
    public bool AnnullamentoInAttesa => AnnullamentoRichiestoIl is not null && Stato == StatoRichiesta.Approvata;

    private RichiestaAssenza() { } // EF Core

    internal RichiestaAssenza(Guid dipendenteId, TipoAssenza tipo, DateOnly dal, DateOnly al, bool aOre,
        decimal ore, string? note, string? protocolloCertificato, DateTime adesso)
    {
        if (al < dal) throw new DomainException("La data di fine precede la data di inizio.");
        if (aOre && dal != al) throw new DomainException("Un permesso a ore deve riguardare un solo giorno.");
        if (ore <= 0) throw new DomainException("La richiesta non comprende ore lavorative.");

        DipendenteId = dipendenteId;
        Tipo = tipo;
        Dal = dal;
        Al = al;
        AOre = aOre;
        Ore = ore;
        Note = note?.Trim();
        ProtocolloCertificato = protocolloCertificato?.Trim();
        RichiestaIl = adesso;
        Stato = tipo.RichiedeApprovazione() ? StatoRichiesta.InAttesa : StatoRichiesta.Approvata;
    }

    public void CollegaChiusura(Guid chiusuraId) => ChiusuraId = chiusuraId;

    /// <summary>Richiesta che occupa il calendario (in attesa o approvata).</summary>
    public bool Attiva => Stato is StatoRichiesta.InAttesa or StatoRichiesta.Approvata;

    public bool SovrapponeA(DateOnly dal, DateOnly al) => Dal <= al && dal <= Al;

    public void Approva(string approvatore, DateTime adesso)
    {
        if (Stato != StatoRichiesta.InAttesa)
            throw new DomainException("Solo le richieste in attesa possono essere approvate.");
        Stato = StatoRichiesta.Approvata;
        DecisaDa = Guard.NotEmpty(approvatore, "Approvatore");
        DecisaIl = adesso;
    }

    public void Rifiuta(string approvatore, string motivo, DateTime adesso)
    {
        if (Stato != StatoRichiesta.InAttesa)
            throw new DomainException("Solo le richieste in attesa possono essere rifiutate.");
        Stato = StatoRichiesta.Rifiutata;
        DecisaDa = Guard.NotEmpty(approvatore, "Approvatore");
        MotivoRifiuto = Guard.NotEmpty(motivo, "Motivo del rifiuto");
        DecisaIl = adesso;
    }

    /// <summary>Il dipendente può annullare finché l'assenza non è iniziata.</summary>
    public void Annulla(DateOnly oggi)
    {
        if (!Attiva) throw new DomainException("La richiesta non è più attiva.");
        if (Stato == StatoRichiesta.Approvata && Dal <= oggi)
            throw new DomainException("Non è possibile annullare un'assenza già iniziata: contattare l'ufficio del personale.");
        Stato = StatoRichiesta.Annullata;
    }

    /// <summary>Il dipendente chiede di cancellare una richiesta già approvata (anche iniziata o passata).</summary>
    public void ChiediAnnullamento(string? motivo, DateTime adesso)
    {
        if (Stato != StatoRichiesta.Approvata) throw new DomainException("Si può chiedere di cancellare solo una richiesta approvata.");
        if (AnnullamentoRichiestoIl is not null) throw new DomainException("La cancellazione è già stata chiesta.");
        var m = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        if (m is { Length: > 500 }) throw new DomainException("Motivo troppo lungo (massimo 500 caratteri).");
        AnnullamentoRichiestoIl = adesso;
        MotivoAnnullamento = m;
    }

    /// <summary>L'azienda non accetta la cancellazione: la richiesta resta approvata.</summary>
    public void RespingiAnnullamento()
    {
        if (!AnnullamentoInAttesa) throw new DomainException("Non c'è una cancellazione da decidere.");
        AnnullamentoRichiestoIl = null;
        MotivoAnnullamento = null;
    }

    /// <summary>L'azienda revoca una richiesta (anche approvata e già passata, es. registrata per errore): non conta più nei saldi.</summary>
    public void Revoca()
    {
        if (!Attiva) throw new DomainException("La richiesta non è più attiva.");
        Stato = StatoRichiesta.Annullata;
    }
}
