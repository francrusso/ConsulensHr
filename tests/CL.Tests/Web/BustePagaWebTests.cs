using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CL.Domain.Aziende;
using CL.Domain.BustePaga;
using CL.Domain.Dipendenti;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CL.Tests.Web;

public sealed class BustePagaWebTests : IDisposable
{
    private const string CfRossi = "RSSMRA85T10A562S";
    private readonly AppFactory _app = new();
    private Guid _aziendaId, _altraAziendaId, _rossi, _bianchi, _verdi;

    public void Dispose() => _app.Dispose();

    private static async Task<string> Testo(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

    private static byte[] Pdf(string contenuto) => Encoding.ASCII.GetBytes($"%PDF-1.4\n% {contenuto}\n%%EOF");

    /// <summary>Acme con Rossi (cerca per CF) e Bianchi (matricola 42); Verdi in un'altra azienda.</summary>
    private async Task Dati()
    {
        (_aziendaId, _altraAziendaId, _rossi, _bianchi, _verdi) = await _app.ConDb(async db =>
        {
            var acme = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var altra = new Azienda("Altra S.p.A.", PartitaIva.Parse("12345678903"));
            var rossi = new Dipendente(acme.Id, "Mario", "Rossi", CodiceFiscale.Parse(CfRossi), new DateOnly(1985, 12, 10));
            var bianchi = new Dipendente(acme.Id, "Anna", "Bianchi", CodiceFiscale.Parse("VRDLGU90A01H501S"), new DateOnly(1990, 1, 1));
            bianchi.ImpostaMatricola("42");
            var verdi = new Dipendente(altra.Id, "Luigi", "Verdi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 12, 10));
            db.AddRange(acme, altra, rossi, bianchi, verdi);
            await db.SaveChangesAsync();
            return (acme.Id, altra.Id, rossi.Id, bianchi.Id, verdi.Id);
        });
    }

    private async Task<HttpClient> Accedi(string email, string ruolo, Guid? aziendaId = null, Guid? dipendenteId = null)
    {
        var u = await _app.CreaUtente(email, ruolo, aziendaId);
        if (dipendenteId is not null)
        {
            using var scope = _app.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var x = (await users.FindByIdAsync(u.Id))!;
            x.DipendenteId = dipendenteId;
            await users.UpdateAsync(x);
            await _app.CompletaDatiPersonali(dipendenteId.Value);
        }
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        await c.Accedi(email);
        return c;
    }

    private static async Task<HttpResponseMessage> CaricaInBlocco(HttpClient c, Guid aziendaId, bool pubblica, params (string Nome, byte[] Dati)[] files)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(await c.TokenDa("/BustePaga/Carica")), "__RequestVerificationToken" },
            { new StringContent(aziendaId.ToString()), "AziendaId" },
            { new StringContent("2026"), "Anno" },
            { new StringContent("10"), "Mese" },
            { new StringContent("Ordinario"), "Tipo" },
            { new StringContent(pubblica ? "true" : "false"), "Pubblica" },
        };
        foreach (var (nome, dati) in files)
        {
            var file = new ByteArrayContent(dati);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(file, "files", nome);
        }
        return await c.PostAsync("/BustePaga/Carica", form);
    }

    [Fact]
    public async Task Caricamento_in_blocco_abbina_per_codice_fiscale_e_matricola()
    {
        await Dati();
        var consulente = await Accedi("consulente@test.it", Ruoli.Consulente);

        var r = await CaricaInBlocco(consulente, _aziendaId, pubblica: true,
            ($"cedolino_{CfRossi}_10-2026.pdf", Pdf("rossi")),
            ("0042_ottobre.pdf", Pdf("bianchi")),
            ("sconosciuto.pdf", Pdf("nessuno")),
            ("foto.pdf", Encoding.ASCII.GetBytes("non sono un pdf")));
        var html = await Testo(r);

        Assert.Contains("2 cedolini caricati", html);
        Assert.Contains("Rossi Mario", html);
        Assert.Contains("Bianchi Anna", html);
        Assert.Contains("sconosciuto.pdf: nessun dipendente", html);
        Assert.Contains("foto.pdf: non è un PDF", html);

        var buste = await _app.ConDb(db => db.BustePaga.ToListAsync());
        Assert.Equal(2, buste.Count);
        Assert.All(buste, b => Assert.Equal(StatoBustaPaga.Pubblicata, b.Stato));
        // Verdi ha lo stesso codice fiscale ma lavora per un'altra azienda: non viene coinvolto.
        Assert.DoesNotContain(buste, b => b.DipendenteId == _verdi);
        Assert.All(buste, b => Assert.True(File.Exists(Path.Combine(_app.CartellaArchivio, b.ChiaveArchivio))));

        // Lo stesso PDF una seconda volta viene scartato.
        r = await CaricaInBlocco(consulente, _aziendaId, pubblica: true, ($"{CfRossi}.pdf", Pdf("rossi")));
        Assert.Contains("questo PDF è già stato caricato", await Testo(r));
    }

    [Fact]
    public async Task Dipendente_vede_e_apre_solo_i_propri_cedolini_pubblicati()
    {
        await Dati();
        var consulente = await Accedi("consulente@test.it", Ruoli.Consulente);
        await CaricaInBlocco(consulente, _aziendaId, pubblica: true, ($"{CfRossi}.pdf", Pdf("rossi")), ("42.pdf", Pdf("bianchi")));
        await CaricaInBlocco(consulente, _aziendaId, pubblica: false, ($"{CfRossi}_bozza.pdf", Pdf("rossi-bozza")));
        var buste = await _app.ConDb(db => db.BustePaga.ToListAsync());
        var mia = buste.Single(b => b.DipendenteId == _rossi && b.Stato == StatoBustaPaga.Pubblicata);
        var bozza = buste.Single(b => b.Stato == StatoBustaPaga.Bozza);
        var collega = buste.Single(b => b.DipendenteId == _bianchi);

        var mario = await Accedi("mario@test.it", Ruoli.Dipendente, _aziendaId, _rossi);
        var elenco = WebUtility.HtmlDecode(await mario.GetStringAsync("/BustePaga"));
        Assert.Contains("Le mie buste paga", elenco);
        Assert.Contains("Ottobre 2026", elenco);
        Assert.DoesNotContain("Bianchi", elenco);

        var pdf = await mario.GetAsync($"/BustePaga/Apri/{mia.Id}");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Pdf("rossi"), await pdf.Content.ReadAsByteArrayAsync());
        Assert.Contains("no-store", pdf.Headers.CacheControl!.ToString());
        Assert.NotNull((await _app.ConDb(db => db.BustePaga.SingleAsync(b => b.Id == mia.Id))).VisualizzataIl);

        Assert.Equal(HttpStatusCode.NotFound, (await mario.GetAsync($"/BustePaga/Apri/{collega.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mario.GetAsync($"/BustePaga/Apri/{bozza.Id}")).StatusCode);
    }

    [Fact]
    public async Task Referente_consulta_la_propria_azienda_ma_non_carica()
    {
        await Dati();
        var consulente = await Accedi("consulente@test.it", Ruoli.Consulente);
        await CaricaInBlocco(consulente, _aziendaId, pubblica: true, ($"{CfRossi}.pdf", Pdf("rossi")));
        await CaricaInBlocco(consulente, _altraAziendaId, pubblica: true, ($"{CfRossi}.pdf", Pdf("verdi")));
        var diAltri = await _app.ConDb(db => db.BustePaga.SingleAsync(b => b.DipendenteId == _verdi));

        var hr = await Accedi("hr@acme.test", Ruoli.Azienda, _aziendaId);
        var elenco = WebUtility.HtmlDecode(await hr.GetStringAsync("/BustePaga"));
        Assert.Contains("Rossi Mario", elenco);
        Assert.DoesNotContain("Verdi", elenco);
        Assert.Equal(HttpStatusCode.NotFound, (await hr.GetAsync($"/BustePaga/Apri/{diAltri.Id}")).StatusCode);

        var carica = await hr.GetAsync("/BustePaga/Carica");
        Assert.Contains("/Account/AccessoNegato", carica.RequestMessage!.RequestUri!.AbsolutePath);

        // L'apertura del referente non vale come consegna al dipendente.
        var mia = await _app.ConDb(db => db.BustePaga.SingleAsync(b => b.DipendenteId == _rossi));
        await hr.GetAsync($"/BustePaga/Apri/{mia.Id}");
        Assert.Null((await _app.ConDb(db => db.BustePaga.SingleAsync(b => b.Id == mia.Id))).VisualizzataIl);
    }

    [Fact]
    public async Task Bozza_si_pubblica_o_si_elimina_e_il_pubblicato_si_ritira()
    {
        await Dati();
        var consulente = await Accedi("consulente@test.it", Ruoli.Consulente);
        await CaricaInBlocco(consulente, _aziendaId, pubblica: false, ($"{CfRossi}.pdf", Pdf("a")), ("42.pdf", Pdf("b")));
        var buste = await _app.ConDb(db => db.BustePaga.ToListAsync());
        var (prima, seconda) = (buste.Single(b => b.DipendenteId == _rossi), buste.Single(b => b.DipendenteId == _bianchi));

        var r = await consulente.InviaForm("/BustePaga", "/BustePaga/Pubblica", new() { ["ids"] = prima.Id.ToString() });
        Assert.Contains("Cedolino pubblicato", await Testo(r));

        r = await consulente.InviaForm("/BustePaga", $"/BustePaga/Elimina/{prima.Id}", new());
        Assert.Contains("Si possono eliminare solo i cedolini in bozza", await Testo(r));

        r = await consulente.InviaForm("/BustePaga", $"/BustePaga/Ritira/{prima.Id}", new());
        Assert.Contains("Cedolino ritirato", await Testo(r));
        Assert.Equal(StatoBustaPaga.Ritirata, (await _app.ConDb(db => db.BustePaga.SingleAsync(b => b.Id == prima.Id))).Stato);

        r = await consulente.InviaForm("/BustePaga", $"/BustePaga/Elimina/{seconda.Id}", new());
        Assert.Contains("Bozza eliminata", await Testo(r));
        Assert.False(File.Exists(Path.Combine(_app.CartellaArchivio, seconda.ChiaveArchivio)));
    }

    [Fact]
    public async Task Cedolino_singolo_dalla_scheda_del_dipendente()
    {
        await Dati();
        var consulente = await Accedi("consulente@test.it", Ruoli.Consulente);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(await consulente.TokenDa($"/Dipendenti/Dettaglio/{_bianchi}")), "__RequestVerificationToken" },
            { new StringContent(_bianchi.ToString()), "dipendenteId" },
            { new StringContent("2026"), "anno" }, { new StringContent("12"), "mese" },
            { new StringContent("Tredicesima"), "tipo" }, { new StringContent("1250,40"), "netto" },
            { new StringContent("true"), "pubblica" },
        };
        form.Add(new ByteArrayContent(Pdf("tredicesima")), "file", "qualsiasi_nome.pdf");
        var html = await Testo(await consulente.PostAsync("/BustePaga/CaricaPerDipendente", form));

        Assert.Contains("Cedolino caricato e pubblicato", html);
        Assert.Contains("Dicembre 2026", html);
        Assert.Contains("Tredicesima", html);
        var b = await _app.ConDb(db => db.BustePaga.SingleAsync());
        Assert.Equal(1250.40m, b.Netto);
    }
}
