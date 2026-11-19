using CL.Domain.Common;
using CL.Domain.ValueObjects;

namespace CL.Domain.Dipendenti;

/// <summary>Anagrafica del lavoratore e storico dei contratti.</summary>
public sealed class Dipendente : Entity
{
    private readonly List<Contratto> _contratti = [];

    public Guid AziendaId { get; private set; }
    public string Nome { get; private set; } = null!;
    public string Cognome { get; private set; } = null!;
    public CodiceFiscale CodiceFiscale { get; private set; } = null!;
    public DateOnly DataNascita { get; private set; }
    public string? Email { get; private set; }
    public string? Telefono { get; private set; }
    public string? Indirizzo { get; private set; }
    public Iban? Iban { get; private set; }
    public string? Matricola { get; private set; }
    /// <summary>Utente Identity collegato (portale dipendente), se abilitato.</summary>
    public string? UserId { get; private set; }

    public IReadOnlyCollection<Contratto> Contratti => _contratti.AsReadOnly();

    public string NomeCompleto => $"{Cognome} {Nome}";

    private Dipendente() { } // EF Core

    public Dipendente(Guid aziendaId, string nome, string cognome, CodiceFiscale codiceFiscale, DateOnly dataNascita)
    {
        if (aziendaId == Guid.Empty) throw new DomainException("Azienda obbligatoria.");
        AziendaId = aziendaId;
        Nome = Guard.NotEmpty(nome, "Nome");
        Cognome = Guard.NotEmpty(cognome, "Cognome");
        CodiceFiscale = codiceFiscale;
        DataNascita = dataNascita;
    }

    public void AggiornaRecapiti(string? email, string? telefono, string? indirizzo)
    {
        Email = email?.Trim();
        Telefono = telefono?.Trim();
        Indirizzo = indirizzo?.Trim();
    }

    public void ImpostaIban(Iban? iban) => Iban = iban;
    public void ImpostaMatricola(string? matricola) => Matricola = matricola?.Trim();
    public void CollegaUtente(string? userId) => UserId = userId;

    public Contratto Assumi(TipoContratto tipo, string ccnl, string livello, DateOnly dataInizio, DateOnly? dataFine,
        decimal retribuzioneLordaAnnua, int mensilitaAnnue = 13, decimal oreSettimanaliFullTime = 40,
        decimal percentualePartTime = 100, int giorniLavorativiSettimana = 5,
        decimal oreFerieAnnue = 160, decimal oreRolAnnue = 72, decimal oreExFestivitaAnnue = 32, string? mansione = null)
    {
        if (dataInizio < DataNascita.AddYears(16))
            throw new DomainException("Il lavoratore deve avere almeno 16 anni alla data di assunzione.");

        var nuovaFine = dataFine ?? DateOnly.MaxValue;
        if (_contratti.Any(c => c.DataInizio <= nuovaFine && dataInizio <= (c.DataFine ?? DateOnly.MaxValue)))
            throw new DomainException("Esiste già un contratto attivo nel periodo indicato.");

        var contratto = new Contratto(Id, tipo, ccnl, livello, dataInizio, dataFine, oreSettimanaliFullTime,
            percentualePartTime, giorniLavorativiSettimana, retribuzioneLordaAnnua, mensilitaAnnue,
            oreFerieAnnue, oreRolAnnue, oreExFestivitaAnnue, mansione);
        _contratti.Add(contratto);
        return contratto;
    }

    public void Cessa(DateOnly dataCessazione)
    {
        var attivo = ContrattoAttivo(dataCessazione)
            ?? throw new DomainException("Nessun contratto attivo alla data di cessazione.");
        attivo.Chiudi(dataCessazione);
    }

    public Contratto? ContrattoAttivo(DateOnly giorno) => _contratti.FirstOrDefault(c => c.AttivoIl(giorno));

    public bool InForzaIl(DateOnly giorno) => ContrattoAttivo(giorno) is not null;
}
