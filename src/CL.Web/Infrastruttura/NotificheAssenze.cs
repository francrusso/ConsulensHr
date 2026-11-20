using System.Net;
using CL.Domain.Assenze;
using CL.Infrastructure.Email;
using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CL.Web.Infrastruttura;

/// <summary>
/// Avvisi email sulle richieste di assenza: a chi deve decidere (il responsabile dall'organigramma, altrimenti i referenti),
/// al dipendente quando la richiesta è approvata o rifiutata. Partono in background.
/// </summary>
public sealed class NotificheAssenze(AppDbContext db, CodaEmail coda, ServizioAccessi accessi, ServizioOrganigramma organigramma)
{
    public async Task NuovaRichiestaAsync(RichiestaAssenza r)
    {
        if (r.Stato != StatoRichiesta.InAttesa) return;
        var dip = await db.Dipendenti.AsNoTracking().IgnoreAutoIncludes()
            .Where(d => d.Id == r.DipendenteId).Select(d => new { d.Nome, d.Cognome, d.AziendaId }).FirstAsync();

        // Chi decide: il responsabile dall'organigramma, se ha un accesso attivo; altrimenti i referenti dell'azienda.
        var approvatore = await organigramma.ApprovatoreAsync(r.DipendenteId);
        var referenti = approvatore is { } a
            ? await db.Users.Where(u => u.DipendenteId == a && u.Attivo && u.EmailConfirmed).Select(u => new { u.Email, u.Nome }).ToListAsync()
            : [];
        if (referenti.Count == 0)
            referenti = await (
                from u in db.Users
                join ur in db.UserRoles on u.Id equals ur.UserId
                join ruolo in db.Roles on ur.RoleId equals ruolo.Id
                where ruolo.Name == Ruoli.Azienda && u.AziendaId == dip.AziendaId && u.Attivo && u.EmailConfirmed
                select new { u.Email, u.Nome }).ToListAsync();

        var link = WebUtility.HtmlEncode(accessi.ComponiUrl("/Assenze"));
        var chi = WebUtility.HtmlEncode($"{dip.Nome} {dip.Cognome}");
        foreach (var u in referenti.Where(x => x.Email is not null))
            coda.Accoda(new MessaggioEmail(u.Email!, $"Richiesta di {r.Tipo.Descrizione().ToLower()} da approvare: {dip.Nome} {dip.Cognome}",
                $"""
                <p>Ciao {WebUtility.HtmlEncode(u.Nome)},</p>
                <p>{chi} ha chiesto <strong>{WebUtility.HtmlEncode(r.Tipo.Descrizione().ToLower())}</strong> {Periodo(r)} ({r.Ore:0.##} ore).</p>
                {(string.IsNullOrWhiteSpace(r.Note) ? "" : $"<p>Note: {WebUtility.HtmlEncode(r.Note)}</p>")}
                <p>Puoi approvarla o rifiutarla dal portale: <a href="{link}">{link}</a></p>
                <p>Consulens HR</p>
                """));
    }

    public async Task EsitoAsync(RichiestaAssenza r)
    {
        if (r.Stato is not (StatoRichiesta.Approvata or StatoRichiesta.Rifiutata)) return;
        var u = await db.Users.AsNoTracking()
            .Where(x => x.DipendenteId == r.DipendenteId && x.Attivo && x.EmailConfirmed)
            .Select(x => new { x.Email, x.Nome }).FirstOrDefaultAsync();
        if (u?.Email is null) return;

        var approvata = r.Stato == StatoRichiesta.Approvata;
        var link = WebUtility.HtmlEncode(accessi.ComponiUrl("/Assenze"));
        var cosa = WebUtility.HtmlEncode(r.Tipo.Descrizione().ToLower());
        coda.Accoda(new MessaggioEmail(u.Email,
            approvata ? $"Richiesta di {r.Tipo.Descrizione().ToLower()} approvata" : $"Richiesta di {r.Tipo.Descrizione().ToLower()} non approvata",
            $"""
            <p>Ciao {WebUtility.HtmlEncode(u.Nome)},</p>
            <p>la tua richiesta di <strong>{cosa}</strong> {Periodo(r)} è stata <strong>{(approvata ? "approvata" : "rifiutata")}</strong>.</p>
            {(approvata || string.IsNullOrWhiteSpace(r.MotivoRifiuto) ? "" : $"<p>Motivo: {WebUtility.HtmlEncode(r.MotivoRifiuto)}</p>")}
            <p>Il dettaglio è nel portale: <a href="{link}">{link}</a></p>
            <p>Consulens HR</p>
            """));
    }

    private static string Periodo(RichiestaAssenza r) =>
        r.Dal == r.Al ? $"per il {r.Dal:dd/MM/yyyy}" : $"dal {r.Dal:dd/MM/yyyy} al {r.Al:dd/MM/yyyy}";
}
