using CL.Domain.Assenze;

namespace CL.Domain.Aziende;

/// <summary>Un tipo di richiesta che l'azienda può offrire ai dipendenti (assenze, smart working, ufficio, straordinari).</summary>
public sealed record TipoRichiesta(string Chiave, string Nome, string Gruppo, bool Obbligatorio = false);

/// <summary>
/// Catalogo dei tipi di richiesta. Ferie e malattia ci sono sempre (sono diritti di legge); gli altri l'azienda li
/// abilita o no dal suo profilo, e chi non è abilitato non compare né ai dipendenti né all'azienda.
/// La chiave di un'assenza è il nome del valore di <see cref="TipoAssenza"/> (es. "Rol").
/// </summary>
public static class TipiRichiesta
{
    public const string SmartWorking = "SmartWorking", InUfficio = "InUfficio", Straordinario = "Straordinario";

    public static readonly IReadOnlyList<TipoRichiesta> Tutti =
    [
        new(SmartWorking, "Smart working", "Presenze"),
        new(InUfficio, "Presenza in ufficio", "Presenze"),
        new(Straordinario, "Straordinario", "Presenze"),
        .. Enum.GetValues<TipoAssenza>().Select(t => new TipoRichiesta(t.ToString(), t.Descrizione(), "Assenze",
            Obbligatorio: t is TipoAssenza.Ferie or TipoAssenza.Malattia)),
    ];
}
