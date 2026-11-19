using CL.Domain.Aziende;
using CL.Domain.Common;
using CL.Domain.Dipendenti;

namespace CL.Domain.Assenze;

/// <summary>
/// Crea richieste di assenza validate: calcola le ore sui giorni lavorativi del contratto,
/// verifica le sovrapposizioni e, se indicato, la capienza del saldo.
/// </summary>
public static class PianificatoreAssenze
{
    public static RichiestaAssenza Crea(
        Dipendente dipendente,
        Azienda azienda,
        TipoAssenza tipo,
        DateOnly dal,
        DateOnly al,
        IEnumerable<RichiestaAssenza> richiesteEsistenti,
        DateTime adesso,
        decimal? orePermesso = null,
        decimal? saldoDisponibile = null,
        string? note = null,
        string? protocolloCertificato = null)
    {
        if (dipendente.AziendaId != azienda.Id)
            throw new DomainException("Il dipendente non appartiene all'azienda indicata.");
        if (al < dal)
            throw new DomainException("La data di fine precede la data di inizio.");
        if (dal.Year != al.Year)
            throw new DomainException("Le richieste a cavallo d'anno vanno divise in due richieste.");

        var contratto = dipendente.ContrattoAttivo(dal);
        if (contratto is null || !contratto.AttivoIl(al))
            throw new DomainException("Il dipendente non ha un contratto attivo per l'intero periodo richiesto.");

        var aOre = orePermesso is not null;
        decimal ore;
        if (orePermesso is { } orePermessoRichieste)
        {
            if (dal != al) throw new DomainException("Un permesso a ore deve riguardare un solo giorno.");
            if (!IsLavorativo(dal, contratto, azienda))
                throw new DomainException("Il giorno richiesto non è lavorativo.");
            if (orePermesso <= 0 || orePermesso > contratto.OreGiornaliere)
                throw new DomainException($"Le ore di permesso devono essere comprese tra 0 e {contratto.OreGiornaliere}.");
            ore = orePermessoRichieste;
        }
        else
        {
            var giorni = CalendarioLavorativo.GiorniLavorativi(dal, al, contratto.GiorniLavorativiSettimana,
                azienda.SantoPatronoMese, azienda.SantoPatronoGiorno).Count();
            if (giorni == 0) throw new DomainException("Il periodo richiesto non comprende giorni lavorativi.");
            ore = giorni * contratto.OreGiornaliere;
        }

        VerificaSovrapposizioni(dipendente.Id, dal, al, aOre, ore, contratto.OreGiornaliere, richiesteEsistenti);

        if (tipo.HaSaldo() && saldoDisponibile is { } saldo && ore > saldo)
            throw new DomainException(
                $"Saldo {tipo.Descrizione()} insufficiente: richieste {ore:0.##} ore, disponibili {saldo:0.##}.");

        return new RichiestaAssenza(dipendente.Id, tipo, dal, al, aOre, ore, note, protocolloCertificato, adesso);
    }

    private static bool IsLavorativo(DateOnly giorno, Contratto c, Azienda a) =>
        CalendarioLavorativo.IsLavorativo(giorno, c.GiorniLavorativiSettimana, a.SantoPatronoMese, a.SantoPatronoGiorno);

    private static void VerificaSovrapposizioni(Guid dipendenteId, DateOnly dal, DateOnly al, bool aOre, decimal ore,
        decimal oreGiornaliere, IEnumerable<RichiestaAssenza> esistenti)
    {
        var sovrapposte = esistenti
            .Where(r => r.DipendenteId == dipendenteId && r.Attiva && r.SovrapponeA(dal, al))
            .ToList();
        if (sovrapposte.Count == 0) return;

        // Più permessi a ore nello stesso giorno sono ammessi se non superano l'orario giornaliero.
        if (aOre && sovrapposte.All(r => r.AOre) && sovrapposte.Sum(r => r.Ore) + ore <= oreGiornaliere)
            return;

        var prima = sovrapposte.MinBy(r => r.Dal)!;
        throw new DomainException(
            $"Il periodo si sovrappone a una richiesta di {prima.Tipo.Descrizione()} dal {prima.Dal:dd/MM/yyyy} al {prima.Al:dd/MM/yyyy}.");
    }
}
