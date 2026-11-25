using System.Net;
using CL.Domain.Assenze;
using CL.Domain.Aziende;
using CL.Domain.Dipendenti;
using CL.Domain.Documenti;
using CL.Domain.Notifiche;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CL.Tests.Web;

public sealed class NotificheWebTests : IDisposable
{
    private readonly AppFactory _app = new();
    private Guid _aziendaId, _dipId;
    private string _idReferente = "", _idDipendente = "";

    public void Dispose() => _app.Dispose();

    private static async Task<string> Testo(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

    /// <summary>Azienda con un dipendente (contratto da inizio anno, con accesso) e un referente; restituisce i due client.</summary>
    private async Task<(HttpClient Dipendente, HttpClient Referente)> Prepara(DateOnly? fineContratto = null)
    {
        var anno = DateTime.Today.Year;
        (_aziendaId, _dipId) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var d = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 12, 10));
            d.Assumi(fineContratto is null ? TipoContratto.TempoIndeterminato : TipoContratto.TempoDeterminato, "Commercio", "4",
                new DateOnly(anno, 1, 1), fineContratto, 28_000m);
            db.AddRange(a, d);
            await db.SaveChangesAsync();
            return (a.Id, d.Id);
        });
        _idReferente = (await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, _aziendaId)).Id;
        _idDipendente = (await _app.CreaUtente("mario@acme.test", Ruoli.Dipendente)).Id;
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            foreach (var id in new[] { _idReferente, _idDipendente })
            {
                var u = (await users.FindByIdAsync(id))!;
                if (id == _idDipendente) u.DipendenteId = _dipId;
                u.EmailConfirmed = true; // ha scelto la password: può ricevere email
                await users.UpdateAsync(u);
            }
        }
        await _app.CompletaDatiPersonali(_dipId);

        var dip = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await dip.Accedi("mario@acme.test");
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");
        return (dip, hr);
    }

    private Task<List<Notifica>> NotificheDi(string utenteId) =>
        _app.ConDb(db => db.Notifiche.Where(n => n.UtenteId == utenteId).OrderBy(n => n.CreataIl).ToListAsync());

    /// <summary>Un giorno lavorativo dell'anno in corso da domani in poi (null negli ultimi giorni dell'anno).</summary>
    private static DateOnly? ProssimoLavorativo()
    {
        var g = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        while (!CalendarioLavorativo.IsLavorativo(g, 5)) g = g.AddDays(1);
        return g.Year == DateTime.Today.Year ? g : null;
    }

    private static Task<HttpResponseMessage> ChiediFerie(HttpClient dip, DateOnly giorno) =>
        dip.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new()
        {
            ["Tipo"] = "Ferie", ["Dal"] = giorno.ToString("yyyy-MM-dd"), ["Al"] = giorno.ToString("yyyy-MM-dd"), ["Note"] = "Visita parenti",
        });

    [Fact]
    public async Task Chi_decide_e_la_persona_ricevono_la_notifica_in_piattaforma()
    {
        var (dip, hr) = await Prepara();
        if (ProssimoLavorativo() is not { } giorno) return; // 31 dicembre: scenario non applicabile

        Assert.Equal(HttpStatusCode.Redirect, (await ChiediFerie(dip, giorno)).StatusCode);

        // Al referente (che decide), non a chi ha fatto la richiesta.
        var perHr = Assert.Single(await NotificheDi(_idReferente));
        Assert.Equal(TipoNotifica.AssenzaDaApprovare, perHr.Tipo);
        Assert.Equal("Richiesta di ferie da approvare: Mario Rossi", perHr.Titolo);
        Assert.Contains("Note: Visita parenti", perHr.Testo);
        Assert.Equal("/Assenze", perHr.Link);
        Assert.Empty(await NotificheDi(_idDipendente));

        // Campanella ed elenco del referente.
        var html = WebUtility.HtmlDecode(await hr.GetStringAsync("/Notifiche"));
        Assert.Contains("1 nuova notifica da leggere", html);
        Assert.Contains("Richiesta di ferie da approvare: Mario Rossi", html);

        var richiesta = await _app.ConDb(db => db.RichiesteAssenza.SingleAsync());
        await hr.InviaForm("/Assenze", $"/Assenze/Approva/{richiesta.Id}", new());
        var esito = Assert.Single(await NotificheDi(_idDipendente));
        Assert.Equal(TipoNotifica.AssenzaDecisa, esito.Tipo);
        Assert.Equal("Richiesta di ferie approvata", esito.Titolo);

        // "Vai alla sezione": porta alla pagina dell'evento e segna come letta. Le notifiche altrui non si aprono.
        var apri = await dip.GetAsync($"/Notifiche/Apri/{esito.Id}");
        Assert.Equal(HttpStatusCode.Redirect, apri.StatusCode);
        Assert.Equal("/Assenze", apri.Headers.Location!.OriginalString);
        Assert.True(Assert.Single(await NotificheDi(_idDipendente)).Letta);
        Assert.Equal(HttpStatusCode.NotFound, (await dip.GetAsync($"/Notifiche/Apri/{perHr.Id}")).StatusCode);

        await hr.InviaForm("/Notifiche", "/Notifiche/SegnaTutteLette", new());
        Assert.All(await NotificheDi(_idReferente), n => Assert.True(n.Letta));
    }

    [Fact]
    public async Task L_email_rispetta_la_scelta_per_tipologia_mentre_la_piattaforma_resta_attiva()
    {
        _app.Email.Configurato = true;
        var (dip, hr) = await Prepara();
        if (ProssimoLavorativo() is not { } giorno) return;

        // Il referente spegne l'email delle nuove richieste di assenza.
        var r = await hr.InviaForm("/Notifiche/Impostazioni", "/Notifiche/Scelta",
            new() { ["tipi"] = "AssenzaDaApprovare", ["canale"] = "Email", ["attiva"] = "false" });
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        var scelta = await _app.ConDb(db => db.PreferenzeNotifiche.SingleAsync(p => p.UtenteId == _idReferente));
        Assert.Equal((TipoNotifica.AssenzaDaApprovare, false), (scelta.Tipo, scelta.Email));

        await ChiediFerie(dip, giorno);
        // Poi uno straordinario, la cui email è rimasta attiva: le email partono in ordine, quindi quella delle ferie sarebbe già arrivata.
        await dip.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new()
        {
            ["Tipo"] = "Straordinario", ["Dal"] = DateTime.Today.ToString("yyyy-MM-dd"), ["Ore"] = "2",
        });
        var email = await _app.Email.AttendiAsync("Straordinario da approvare: Mario Rossi");
        Assert.Equal("hr@acme.test", email.A);
        Assert.Contains("Ciao Test", email.Html);
        Assert.Contains("/Assenze", email.Html);
        Assert.Contains("Impostazioni notifiche", email.Html);
        Assert.DoesNotContain(_app.Email.Inviate, m => m.Oggetto.StartsWith("Richiesta di ferie"));

        // In piattaforma arrivano entrambe.
        Assert.Equal([TipoNotifica.AssenzaDaApprovare, TipoNotifica.StraordinarioDaApprovare],
            (await NotificheDi(_idReferente)).Select(n => n.Tipo).Order());
    }

    [Fact]
    public async Task Le_impostazioni_mostrano_solo_le_tipologie_del_proprio_ruolo()
    {
        var (dip, hr) = await Prepara();

        var html = WebUtility.HtmlDecode(await dip.GetStringAsync("/Notifiche/Impostazioni"));
        Assert.Contains("Nuova busta paga", html);
        Assert.Contains("Richiesta approvata o rifiutata", html);
        Assert.Contains("WhatsApp", html);
        Assert.Contains("Presto disponibile", html);
        Assert.Contains("Recupero della password", html); // sempre attive
        Assert.DoesNotContain("Presenze inviate", html);
        Assert.DoesNotContain("Nuova richiesta da approvare", html); // non è responsabile di nessuno

        // Una tipologia che non può ricevere non si imposta.
        var r = await dip.InviaForm("/Notifiche/Impostazioni", "/Notifiche/Scelta",
            new() { ["tipi"] = "PresenzeInviate", ["canale"] = "Email", ["attiva"] = "false" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);

        html = WebUtility.HtmlDecode(await hr.GetStringAsync("/Notifiche/Impostazioni"));
        Assert.Contains("Nuova richiesta da approvare", html);
        Assert.Contains("Promemoria presenze", html);
        Assert.DoesNotContain("Nuova busta paga", html);
    }

    [Fact]
    public async Task Con_il_numero_collegato_le_notifiche_scelte_arrivano_anche_su_WhatsApp()
    {
        _app.WhatsApp.Configurato = true;
        var (dip, hr) = await Prepara();
        if (ProssimoLavorativo() is not { } giorno) return;
        Task<HttpResponseMessage> Invia(string azione, Dictionary<string, string> campi) => hr.InviaForm("/Notifiche/Impostazioni", azione, campi);
        Task<ApplicationUser> Referente() => _app.ConDb(db => db.Users.SingleAsync(u => u.Id == _idReferente));

        // Senza numero collegato WhatsApp non si accende; senza consenso non parte nessun codice.
        var r = await Invia("/Notifiche/Scelta", new() { ["tipi"] = "AssenzaDaApprovare", ["canale"] = "WhatsApp", ["attiva"] = "true" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("serve il tuo consenso", await Testo(await Invia("/Notifiche/WhatsAppCodice", new() { ["numero"] = "333 123 4567" })));
        Assert.Empty(_app.WhatsApp.Codici);

        var html = await Testo(await Invia("/Notifiche/WhatsAppCodice", new() { ["numero"] = "333 123 4567", ["consenso"] = "true" }));
        Assert.Contains("Ti abbiamo inviato un codice su WhatsApp al +39 333 123 4567", html);
        Assert.Contains("Inserisci il codice", html);
        var (numero, codice) = Assert.Single(_app.WhatsApp.Codici);
        Assert.Equal("+393331234567", numero);

        // Un codice sbagliato non collega il numero.
        html = await Testo(await Invia("/Notifiche/WhatsAppVerifica", new() { ["numero"] = numero, ["codice"] = codice == "000000" ? "111111" : "000000" }));
        Assert.Contains("Codice non valido", html);
        Assert.False((await Referente()).PhoneNumberConfirmed);

        html = await Testo(await Invia("/Notifiche/WhatsAppVerifica", new() { ["numero"] = numero, ["codice"] = codice }));
        Assert.Contains("WhatsApp collegato", html);
        var referente = await Referente();
        Assert.Equal(("+393331234567", true), (referente.PhoneNumber, referente.PhoneNumberConfirmed));
        Assert.NotNull(referente.WhatsAppConsensoIl);

        // WhatsApp acceso solo per le nuove richieste di assenza (predefinito: spento per tutte).
        r = await Invia("/Notifiche/Scelta", new() { ["tipi"] = "AssenzaDaApprovare", ["canale"] = "WhatsApp", ["attiva"] = "true" });
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        // Prima uno straordinario (WhatsApp spento), poi le ferie: la coda è in ordine, quindi lo straordinario sarebbe già arrivato.
        await dip.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new()
        {
            ["Tipo"] = "Straordinario", ["Dal"] = DateTime.Today.ToString("yyyy-MM-dd"), ["Ore"] = "2",
        });
        await ChiediFerie(dip, giorno);
        var messaggio = await _app.WhatsApp.AttendiAsync("Richiesta di ferie da approvare: Mario Rossi");
        Assert.Equal("+393331234567", messaggio.Numero);
        Assert.Equal("Test", messaggio.Nome);
        // Il pulsante del messaggio apre la notifica nel portale.
        var notifica = (await NotificheDi(_idReferente)).Single(n => n.Tipo == TipoNotifica.AssenzaDaApprovare);
        Assert.Equal(notifica.Id.ToString(), messaggio.IdNotifica);
        Assert.DoesNotContain(_app.WhatsApp.Notifiche, m => m.Titolo.StartsWith("Straordinario"));

        Assert.Contains("Numero WhatsApp scollegato", await Testo(await Invia("/Notifiche/WhatsAppScollega", new())));
        referente = await Referente();
        Assert.Null(referente.PhoneNumber);
        Assert.Null(referente.WhatsAppConsensoIl);
    }

    [Fact]
    public async Task I_promemoria_delle_scadenze_arrivano_una_volta_sola()
    {
        var oggi = DateOnly.FromDateTime(DateTime.Today);
        await Prepara(fineContratto: oggi.AddDays(10));
        var studio = (await _app.CreaUtente("studio@test.it", Ruoli.Consulente)).Id; // super amministratore: l'azienda non è assegnata
        await _app.ConDb(async db =>
        {
            db.DocumentiPersonali.Add(new DocumentoPersonale(_aziendaId, _dipId, TipoDocumentoPersonale.DocumentoIdentita, null, oggi.AddDays(5),
                "ci.pdf", "application/pdf", "personali/ci.pdf", 100, "test", DateTime.UtcNow));
            return await db.SaveChangesAsync();
        });

        async Task<int> Controlla()
        {
            using var scope = _app.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<PromemoriaScadenze>().ControllaAsync();
        }

        Assert.True(await Controlla() > 0);
        Assert.Equal(0, await Controlla());

        Assert.Equal([TipoNotifica.ContrattoInScadenza, TipoNotifica.DocumentoInScadenza], (await NotificheDi(studio)).Select(n => n.Tipo).Order());
        Assert.Equal([TipoNotifica.ContrattoInScadenza, TipoNotifica.DocumentoInScadenza], (await NotificheDi(_idReferente)).Select(n => n.Tipo).Order());
        var perPersona = Assert.Single(await NotificheDi(_idDipendente));
        Assert.Equal(TipoNotifica.DocumentoInScadenza, perPersona.Tipo);
        Assert.Contains("Il tuo documento «Documento d'identità» scade il", perPersona.Testo);
    }
}
