namespace CL.Domain.Assenze;

/// <summary>Festività nazionali italiane e calcolo dei giorni lavorativi.</summary>
public static class CalendarioLavorativo
{
    public static IReadOnlySet<DateOnly> FestivitaNazionali(int anno)
    {
        var pasqua = Pasqua(anno);
        var feste = new HashSet<DateOnly>
        {
            new(anno, 1, 1),   // Capodanno
            new(anno, 1, 6),   // Epifania
            pasqua,
            pasqua.AddDays(1), // Lunedì dell'Angelo
            new(anno, 4, 25),  // Liberazione
            new(anno, 5, 1),   // Festa del lavoro
            new(anno, 6, 2),   // Festa della Repubblica
            new(anno, 8, 15),  // Ferragosto
            new(anno, 11, 1),  // Ognissanti
            new(anno, 12, 8),  // Immacolata
            new(anno, 12, 25), // Natale
            new(anno, 12, 26), // Santo Stefano
        };
        // San Francesco d'Assisi: festività nazionale ripristinata dal 2026 (L. 151/2025)
        if (anno >= 2026) feste.Add(new DateOnly(anno, 10, 4));
        return feste;
    }

    public static bool IsFestivo(DateOnly giorno, int? patronoMese = null, int? patronoGiorno = null) =>
        FestivitaNazionali(giorno.Year).Contains(giorno)
        || (patronoMese == giorno.Month && patronoGiorno == giorno.Day);

    public static bool IsLavorativo(DateOnly giorno, int giorniLavorativiSettimana,
        int? patronoMese = null, int? patronoGiorno = null)
    {
        if (giorno.DayOfWeek == DayOfWeek.Sunday) return false;
        if (giorno.DayOfWeek == DayOfWeek.Saturday && giorniLavorativiSettimana < 6) return false;
        return !IsFestivo(giorno, patronoMese, patronoGiorno);
    }

    public static IEnumerable<DateOnly> GiorniLavorativi(DateOnly dal, DateOnly al, int giorniLavorativiSettimana,
        int? patronoMese = null, int? patronoGiorno = null)
    {
        for (var g = dal; g <= al; g = g.AddDays(1))
            if (IsLavorativo(g, giorniLavorativiSettimana, patronoMese, patronoGiorno))
                yield return g;
    }

    /// <summary>Domenica di Pasqua (calendario gregoriano, algoritmo anonimo di Meeus/Jones/Butcher).</summary>
    public static DateOnly Pasqua(int anno)
    {
        int a = anno % 19, b = anno / 100, c = anno % 100, d = b / 4, e = b % 4;
        int f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int mese = (h + l - 7 * m + 114) / 31, giorno = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(anno, mese, giorno);
    }
}
