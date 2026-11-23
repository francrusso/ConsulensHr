using System.Net;
using CL.Domain.Assenze;
using CL.Domain.Aziende;
using CL.Domain.Dipendenti;
using CL.Domain.Organizzazione;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CL.Tests.Web;

public sealed class OrganigrammaWebTests : IDisposable
{
    private readonly AppFactory _app = new();
    private Guid _azienda, _rossi, _bianchi, _verdi;

    public void Dispose() => _app.Dispose();

    private static async Task<string> Testo(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

    /// <summary>Acme con tre dipendenti in forza: Rossi, Bianchi, Verdi.</summary>
    private async Task Dati()
    {
        (_azienda, _rossi, _bianchi, _verdi) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            Dipendente Nuovo(string nome, string cognome, string cf)
            {
                var d = new Dipendente(a.Id, nome, cognome, CodiceFiscale.Parse(cf), new DateOnly(1985, 1, 1));
                d.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(DateTime.Today.Year, 1, 1), null, 28_000m);
                return d;
            }
            var rossi = Nuovo("Mario", "Rossi", "RSSMRA85T10A562S");
            var bianchi = Nuovo("Laura", "Bianchi", "RSSLRA15A41F839L");
            var verdi = Nuovo("Anna", "Verdi", "VRDNNA88C41F839V");
            db.AddRange(a, rossi, bianchi, verdi);
            await db.SaveChangesAsync();
            return (a.Id, rossi.Id, bianchi.Id, verdi.Id);
        });
    }

    private async Task<HttpClient> Dipendente(string email, Guid dipendenteId)
    {
        var u = await _app.CreaUtente(email, Ruoli.Dipendente);
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var x = (await users.FindByIdAsync(u.Id))!;
            x.DipendenteId = dipendenteId;
            x.AziendaId = _azienda;
            await users.UpdateAsync(x);
        }
        await _app.CompletaDatiPersonali(dipendenteId);
        var c = _app.CreateClient();
        await c.Accedi(email);
        return c;
    }

    private static DateOnly GiornoLavorativo(int traGiorni)
    {
        var g = DateOnly.FromDateTime(DateTime.Today).AddDays(traGiorni);
        while (!CalendarioLavorativo.IsLavorativo(g, 5)) g = g.AddDays(1);
        return g;
    }

    [Fact]
    public async Task Il_responsabile_del_team_approva_le_ferie_dei_suoi_collaboratori()
    {
        await Dati();
        var giorno = GiornoLavorativo(20);
        if (giorno.Year != DateTime.Today.Year) return; // ultime settimane dell'anno: scenario non applicabile

        // Il referente crea Vendite > Vendite Nord (responsabile Rossi) e vi assegna Bianchi.
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, _azienda);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");
        Assert.Contains("Team \"Vendite\" creato", await Testo(await hr.InviaForm("/Team", "/Team/Salva", new() { ["aziendaId"] = _azienda.ToString(), ["nome"] = "Vendite" })));
        var vendite = await _app.ConDb(db => db.Team.SingleAsync());
        await hr.InviaForm("/Team", "/Team/Salva", new()
        {
            ["aziendaId"] = _azienda.ToString(), ["nome"] = "Vendite Nord", ["padreId"] = vendite.Id.ToString(), ["responsabileId"] = _rossi.ToString(),
        });
        var nord = await _app.ConDb(db => db.Team.SingleAsync(t => t.Nome == "Vendite Nord"));
        var html = await Testo(await hr.InviaForm("/Team", "/Team/Assegna", new()
        {
            ["dipendenteId"] = _bianchi.ToString(), ["teamId"] = nord.Id.ToString(), ["posizione"] = "Commerciale",
        }));
        Assert.Contains("Organizzazione di Bianchi Laura aggiornata", html);
        Assert.Contains("Vendite Nord", html);
        Assert.Equal(_rossi, (await _app.ConDb(db => db.Team.SingleAsync(t => t.Id == nord.Id))).ResponsabileId);

        // Un ciclo nella gerarchia viene rifiutato.
        Assert.Contains("non può stare sotto", await Testo(await hr.InviaForm("/Team", "/Team/Salva", new()
        {
            ["aziendaId"] = _azienda.ToString(), ["id"] = vendite.Id.ToString(), ["nome"] = "Vendite", ["padreId"] = nord.Id.ToString(),
        })));

        // Bianchi chiede un giorno di ferie: lo vede Rossi, non Verdi.
        var bianchi = await Dipendente("laura@acme.test", _bianchi);
        await bianchi.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new() { ["Tipo"] = "Ferie", ["Dal"] = giorno.ToString("yyyy-MM-dd") });
        var richiesta = await _app.ConDb(db => db.RichiesteAssenza.SingleAsync());

        var verdi = await Dipendente("anna@acme.test", _verdi);
        Assert.DoesNotContain("Da approvare per il tuo team", WebUtility.HtmlDecode(await verdi.GetStringAsync("/Assenze")));
        var r = await verdi.InviaForm("/Assenze", $"/Assenze/Approva/{richiesta.Id}", new());
        Assert.Contains("/Account/AccessoNegato", r.RequestMessage!.RequestUri!.AbsolutePath);

        var rossi = await Dipendente("mario@acme.test", _rossi);
        html = WebUtility.HtmlDecode(await rossi.GetStringAsync("/Assenze"));
        Assert.Contains("Da approvare per il tuo team", html);
        Assert.Contains("Bianchi Laura", html);
        Assert.Contains("Richiesta approvata", await Testo(await rossi.InviaForm("/Assenze", $"/Assenze/Approva/{richiesta.Id}", new())));
        var decisa = await _app.ConDb(db => db.RichiesteAssenza.SingleAsync());
        Assert.Equal(StatoRichiesta.Approvata, decisa.Stato);
        Assert.Equal("mario@acme.test", decisa.DecisaDa);

        // Rossi non approva sé stesso: sopra di lui non c'è nessuno, decide il referente.
        await rossi.InviaForm("/Assenze/Nuova", "/Assenze/Nuova", new() { ["Tipo"] = "Ferie", ["Dal"] = GiornoLavorativo(40).ToString("yyyy-MM-dd") });
        var sua = await _app.ConDb(db => db.RichiesteAssenza.SingleAsync(x => x.DipendenteId == _rossi));
        r = await rossi.InviaForm("/Assenze", $"/Assenze/Approva/{sua.Id}", new());
        Assert.Contains("/Account/AccessoNegato", r.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Richiesta approvata", await Testo(await hr.InviaForm("/Assenze", $"/Assenze/Approva/{sua.Id}", new())));
        Assert.Empty(_app.ErroriServer);
    }

    [Fact]
    public async Task Lo_studio_gestisce_l_organigramma_e_un_team_con_componenti_non_si_elimina()
    {
        await Dati();
        await _app.CreaUtente("consulente@test.it", Ruoli.Consulente);
        var consulente = _app.CreateClient();
        await consulente.Accedi("consulente@test.it");
        var pagina = $"/Team?aziendaId={_azienda}";

        await consulente.InviaForm(pagina, "/Team/Salva", new() { ["aziendaId"] = _azienda.ToString(), ["nome"] = "Magazzino", ["responsabileId"] = _verdi.ToString() });
        var team = await _app.ConDb(db => db.Team.SingleAsync());
        await consulente.InviaForm(pagina, "/Team/Assegna", new() { ["dipendenteId"] = _rossi.ToString(), ["teamId"] = team.Id.ToString(), ["ritorno"] = pagina });

        var html = WebUtility.HtmlDecode(await consulente.GetStringAsync(pagina));
        Assert.Contains("Magazzino", html);
        Assert.Contains("Verdi Anna", html);
        Assert.Contains("Esiste già un team", await Testo(await consulente.InviaForm(pagina, "/Team/Salva", new() { ["aziendaId"] = _azienda.ToString(), ["nome"] = "magazzino" })));

        html = await Testo(await consulente.InviaForm(pagina, "/Team/Elimina", new() { ["aziendaId"] = _azienda.ToString(), ["id"] = team.Id.ToString() }));
        Assert.Contains("ha dei componenti", html);

        // Nella scheda del dipendente non c'è più il campo "Approva ferie e permessi" (si ricava da team e responsabile).
        var scheda = WebUtility.HtmlDecode(await consulente.GetStringAsync($"/Dipendenti/Dettaglio/{_rossi}"));
        Assert.DoesNotContain("Approva ferie e permessi", scheda);
    }

    [Fact]
    public async Task Team_con_colore_ed_emoji_nel_grafico()
    {
        await Dati();
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, _azienda);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");
        var campi = new Dictionary<string, string>
        {
            ["aziendaId"] = _azienda.ToString(), ["nome"] = "Vendite", ["responsabileId"] = _rossi.ToString(), ["colore"] = "#8cc98a", ["emoji"] = "🛒",
        };
        Assert.Contains("Scegli una sola emoji", await Testo(await hr.InviaForm("/Team", "/Team/Salva", new(campi) { ["emoji"] = "abc" })));
        Assert.Contains("Colore non valido", await Testo(await hr.InviaForm("/Team", "/Team/Salva", new(campi) { ["colore"] = "rosso" })));
        await hr.InviaForm("/Team", "/Team/Salva", new(campi));
        var team = await _app.ConDb(db => db.Team.SingleAsync());
        Assert.Equal("#8CC98A", team.Colore);
        Assert.Equal("🛒", team.Emoji);
        await hr.InviaForm("/Team", "/Team/Assegna", new() { ["dipendenteId"] = _bianchi.ToString(), ["teamId"] = team.Id.ToString(), ["posizione"] = "Commerciale" });

        // Un secondo team senza colore scelto prende il primo colore libero della tavolozza.
        await hr.InviaForm("/Team", "/Team/Salva", new() { ["aziendaId"] = _azienda.ToString(), ["nome"] = "Magazzino" });
        Assert.Equal(Team.Palette[0].Hex, (await _app.ConDb(db => db.Team.SingleAsync(t => t.Nome == "Magazzino"))).Colore);

        // Pagina "Team": gestione; pagina "Organigramma": l'albero. Entrambe nel sottomenù "Personale".
        var gestione = WebUtility.HtmlDecode(await hr.GetStringAsync("/Team"));
        Assert.Contains("<title>Team |", gestione);
        Assert.Contains("Magazzino", gestione); // un'unica tabella con tutti i team
        var cercati = WebUtility.HtmlDecode(await hr.GetStringAsync("/Team?q=magaz"));
        Assert.Contains("Magazzino", cercati);
        Assert.DoesNotContain("Apri il team Vendite", cercati);
        Assert.Contains("Ricerca:", cercati); // chip per togliere la ricerca
        Assert.DoesNotContain("li approva il", gestione); // niente più spiegazione delle regole
        var magazzino = await _app.ConDb(db => db.Team.SingleAsync(t => t.Nome == "Magazzino"));

        // Pagina del team: membri da aggiungere e togliere, posizione e responsabile diretto restano.
        var paginaTeam = $"/Team/Dettaglio/{magazzino.Id}";
        var htmlTeam = WebUtility.HtmlDecode(await hr.GetStringAsync(paginaTeam));
        Assert.Contains("Nessun membro", htmlTeam);
        Assert.Matches(@"<a href=""/Dipendenti"">Personale</a>[\s\S]*<a href=""/Team"">Team</a>[\s\S]*aria-current=""page"">Magazzino</li>", htmlTeam);
        var conMembro = await Testo(await hr.InviaForm(paginaTeam, "/Team/Membro", new() { ["teamId"] = magazzino.Id.ToString(), ["dipendenteId"] = _bianchi.ToString() }));
        Assert.Contains("aggiunto al team Magazzino", conMembro);
        var bianchi = await _app.ConDb(db => db.Dipendenti.SingleAsync(d => d.Id == _bianchi));
        Assert.Equal((magazzino.Id, "Commerciale"), (bianchi.TeamId!.Value, bianchi.Posizione));
        Assert.Contains("tolto dal team", await Testo(await hr.InviaForm(paginaTeam, "/Team/Membro",
            new() { ["teamId"] = magazzino.Id.ToString(), ["dipendenteId"] = _bianchi.ToString(), ["togli"] = "true" })));
        Assert.Null((await _app.ConDb(db => db.Dipendenti.SingleAsync(d => d.Id == _bianchi))).TeamId);
        await hr.InviaForm(paginaTeam, "/Team/Membro", new() { ["teamId"] = team.Id.ToString(), ["dipendenteId"] = _bianchi.ToString() });
        Assert.Contains("href=\"#nuovoTeam\"", gestione); // "Nuovo team" nella barra in alto
        Assert.DoesNotContain("class=\"organigramma\"", gestione);
        Assert.Contains("href=\"/Team/Organigramma\"", gestione);
        var html = WebUtility.HtmlDecode(await hr.GetStringAsync("/Team/Organigramma"));
        Assert.Contains("class=\"organigramma\"", html);
        Assert.Matches(@"<a class=""org-team org-team-link[^""]*""[^>]*href=""/Team/Dettaglio/[0-9a-f-]{36}\?modifica=[Tt]rue""", html); // il team apre la modifica
        Assert.Contains("--colore-team:#8CC98A", html);
        Assert.Contains("🛒", html);
        Assert.Contains("Commerciale", html);
        Assert.Contains("/assets/images/avatar/neutro.png", html); // nessuno ha indicato genere o pronomi

        await hr.InviaForm($"/Dipendenti/Modifica/{_bianchi}", "/Dipendenti/Salva", new()
        {
            ["Id"] = _bianchi.ToString(), ["AziendaId"] = _azienda.ToString(), ["Nome"] = "Laura", ["Cognome"] = "Bianchi",
            ["CodiceFiscale"] = "RSSLRA15A41F839L", ["DataNascita"] = "1985-01-01", ["Pronomi"] = "Femminili",
        });
        Assert.Contains("/assets/images/avatar/donna.png", WebUtility.HtmlDecode(await hr.GetStringAsync("/Team/Organigramma")));
    }

    [Fact]
    public async Task Foto_del_dipendente_nell_organigramma_visibile_solo_a_chi_e_autorizzato()
    {
        await Dati();
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, _azienda);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");

        using (var form = new MultipartFormDataContent
               {
                   { new StringContent(await hr.TokenDa($"/Dipendenti/Modifica/{_rossi}")), "__RequestVerificationToken" },
                   { new StringContent(_rossi.ToString()), "Id" }, { new StringContent(_azienda.ToString()), "AziendaId" },
                   { new StringContent("Mario"), "Nome" }, { new StringContent("Rossi"), "Cognome" },
                   { new StringContent("RSSMRA85T10A562S"), "CodiceFiscale" }, { new StringContent("1985-01-01"), "DataNascita" },
               })
        {
            form.Add(new ByteArrayContent(png), "Foto", "mario.png");
            Assert.Contains("Modifiche salvate", await Testo(await hr.PostAsync("/Dipendenti/Salva", form)));
        }
        Assert.NotNull((await _app.ConDb(db => db.Dipendenti.SingleAsync(d => d.Id == _rossi))).FotoChiave);

        Assert.Equal(png, await hr.GetByteArrayAsync($"/Dipendenti/Foto/{_rossi}"));
        Assert.Contains($"/Dipendenti/Foto/{_rossi}", await hr.GetStringAsync("/Team/Organigramma")); // card con la foto

        var mario = await Dipendente("mario@acme.test", _rossi);
        Assert.Equal(HttpStatusCode.OK, (await mario.GetAsync($"/Dipendenti/Foto/{_rossi}")).StatusCode); // la propria
        var anna = await Dipendente("anna@acme.test", _verdi);
        Assert.Equal(HttpStatusCode.NotFound, (await anna.GetAsync($"/Dipendenti/Foto/{_rossi}")).StatusCode); // non quella dei colleghi
    }

    [Fact]
    public async Task La_tabella_dei_team_conta_chi_lavora_in_presenza_e_chi_da_remoto()
    {
        await Dati();
        await _app.ConDb(async db =>
        {
            var t = new Team(_azienda, "Vendite");
            db.Add(t);
            foreach (var d in await db.Dipendenti.ToListAsync()) d.ImpostaOrganizzazione(t, null, null);
            (await db.Dipendenti.SingleAsync(d => d.Id == _verdi)).ImpostaSede(SedeLavoro.SmartWorking);
            return await db.SaveChangesAsync();
        });
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, _azienda);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");
        var html = WebUtility.HtmlDecode(await hr.GetStringAsync("/Team"));
        Assert.Matches(@"data-label=""In presenza"">[\s\S]*?<span class=""numero-membri"">2</span>", html);
        Assert.Matches(@"data-label=""Da remoto"">[\s\S]*?title=""Verdi Anna""[\s\S]*?<span class=""numero-membri"">1</span>", html);
    }
}
