using System.Net;
using CL.Domain.Aziende;
using CL.Domain.Dipendenti;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CL.Tests.Web;

public sealed class DocumentiWebTests : IDisposable
{
    private readonly AppFactory _app = new();
    public void Dispose() => _app.Dispose();

    private static async Task<string> Testo(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
    private static readonly byte[] Pdf = [.. "%PDF-1.7\n"u8.ToArray(), 1, 2, 3];

    [Fact]
    public async Task L_azienda_carica_la_CU_e_il_dipendente_la_vede()
    {
        var (azienda, rossi, altraPersona) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var altra = new Azienda("Altra S.p.A.", PartitaIva.Parse("12345678903"));
            var r = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 1, 1));
            var c = r.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(2025, 1, 1), null, 28_000m);
            c.ImpostaDocumento("contratti/x.pdf", "contratto.pdf", DateTime.UtcNow);
            var v = new Dipendente(altra.Id, "Luigi", "Verdi", CodiceFiscale.Parse("VRDNNA88C41F839V"), new DateOnly(1988, 3, 1));
            db.AddRange(a, altra, r, v);
            await db.SaveChangesAsync();
            return (a.Id, r.Id, v.Id);
        });
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, azienda);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");
        var anno = DateTime.Today.Year - 1;

        var html = WebUtility.HtmlDecode(await hr.GetStringAsync("/Documenti/Cu"));
        Assert.Contains("Rossi Mario", html);
        Assert.DoesNotContain("Verdi Luigi", html);
        Assert.Contains("Da caricare", html);
        Assert.Contains("DOCUMENTI", html.ToUpperInvariant());

        async Task<string> Carica(Guid dip, byte[] file)
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(await hr.TokenDa("/Documenti/Cu")), "__RequestVerificationToken" },
                { new StringContent(dip.ToString()), "dipendenteId" }, { new StringContent(anno.ToString()), "anno" },
            };
            form.Add(new ByteArrayContent(file), "documento", "cu.pdf");
            return await Testo(await hr.PostAsync("/Documenti/CaricaCu", form));
        }
        Assert.Contains("deve essere un file PDF", await Carica(rossi, [1, 2, 3]));
        Assert.Contains($"CU {anno} di Rossi Mario caricata", await Carica(rossi, Pdf));
        Assert.Contains("caricata", await Carica(rossi, Pdf)); // la seconda sostituisce la prima
        Assert.Equal(1, await _app.ConDb(db => db.CertificazioniUniche.CountAsync()));
        // Una persona di un'altra azienda non si trova.
        using (var form = new MultipartFormDataContent
               {
                   { new StringContent(await hr.TokenDa("/Documenti/Cu")), "__RequestVerificationToken" },
                   { new StringContent(altraPersona.ToString()), "dipendenteId" }, { new StringContent(anno.ToString()), "anno" },
               })
        {
            form.Add(new ByteArrayContent(Pdf), "documento", "cu.pdf");
            Assert.Equal(HttpStatusCode.NotFound, (await hr.PostAsync("/Documenti/CaricaCu", form)).StatusCode);
        }

        // Il dipendente vede la propria CU e l'apertura viene registrata.
        var u = await _app.CreaUtente("mario@acme.test", Ruoli.Dipendente);
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var x = (await users.FindByIdAsync(u.Id))!;
            (x.DipendenteId, x.AziendaId) = (rossi, azienda);
            await users.UpdateAsync(x);
        }
        await _app.CompletaDatiPersonali(rossi);
        var mario = _app.CreateClient();
        await mario.Accedi("mario@acme.test");
        var cu = await _app.ConDb(db => db.CertificazioniUniche.SingleAsync());
        Assert.Contains($"CU {anno + 1}", WebUtility.HtmlDecode(await mario.GetStringAsync("/Documenti/Cu")));
        Assert.Equal(Pdf, await mario.GetByteArrayAsync($"/Documenti/ApriCu/{cu.Id}"));
        Assert.NotNull((await _app.ConDb(db => db.CertificazioniUniche.SingleAsync())).VisualizzataIl);
        Assert.Contains("Il mio contratto", WebUtility.HtmlDecode(await mario.GetStringAsync("/Documenti/Contratti"))); // ora è "Il mio contratto"
        // Il dipendente non può eliminarla.
        await mario.PostAsync("/Documenti/EliminaCu", new FormUrlEncodedContent([new("id", cu.Id.ToString())]));
        Assert.Equal(1, await _app.ConDb(db => db.CertificazioniUniche.CountAsync()));

        // Contratti: elenco con il link al PDF.
        html = WebUtility.HtmlDecode(await hr.GetStringAsync("/Documenti/Contratti"));
        Assert.Contains("Tempo indeterminato", html);
        Assert.Contains("/Documenti/ApriContratto/", html);
        Assert.Empty(_app.ErroriServer);
    }

    [Fact]
    public async Task Il_dipendente_carica_i_suoi_documenti_e_l_azienda_vede_le_scadenze()
    {
        var (azienda, rossi) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var r = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 1, 1));
            db.AddRange(a, r);
            await db.SaveChangesAsync();
            return (a.Id, r.Id);
        });
        var u = await _app.CreaUtente("mario@acme.test", Ruoli.Dipendente);
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var x = (await users.FindByIdAsync(u.Id))!;
            (x.DipendenteId, x.AziendaId) = (rossi, azienda);
            await users.UpdateAsync(x);
        }
        await _app.CompletaDatiPersonali(rossi);
        var mario = _app.CreateClient();
        await mario.Accedi("mario@acme.test");

        async Task<string> Carica(HttpClient c, string tipo, string? scadenza, byte[] file)
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(await c.TokenDa("/Documenti/NuovoPersonale")), "__RequestVerificationToken" },
                { new StringContent(tipo), "tipo" }, { new StringContent(rossi.ToString()), "dipendenteId" },
            };
            if (scadenza is not null) form.Add(new StringContent(scadenza), "scadenza");
            form.Add(new ByteArrayContent(file), "documento", "doc.pdf");
            return await Testo(await c.PostAsync("/Documenti/CaricaPersonale", form));
        }
        var tra10 = DateTime.Today.AddDays(10).ToString("yyyy-MM-dd");
        Assert.Contains("Indica la scadenza", await Carica(mario, "DocumentoIdentita", null, Pdf));
        Assert.Contains("Carica un PDF o una foto", await Carica(mario, "DocumentoIdentita", tra10, [1, 2, 3]));
        Assert.Contains("Documento d'identità caricato", await Carica(mario, "DocumentoIdentita", tra10, Pdf));
        Assert.Contains("Modulo detrazioni caricato", await Carica(mario, "ModuloDetrazioni", null, [0xFF, 0xD8, 0xFF, 0xE0, 0]));

        var html = WebUtility.HtmlDecode(await mario.GetStringAsync("/Documenti/Personali"));
        Assert.Contains("I miei documenti", html);
        Assert.Contains("In scadenza", html);
        var id = await _app.ConDb(db => db.DocumentiPersonali.Where(d => d.TipoFile == "image/jpeg").Select(d => d.Id).SingleAsync());
        var foto = await mario.GetAsync($"/Documenti/ApriPersonale/{id}");
        Assert.Equal("image/jpeg", foto.Content.Headers.ContentType!.MediaType);

        // L'azienda lo vede tra i documenti in scadenza e nelle cose da fare.
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, azienda);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");
        html = WebUtility.HtmlDecode(await hr.GetStringAsync("/Documenti/Personali?stato=in-scadenza"));
        Assert.Contains("Rossi Mario", html);
        Assert.Contains("Documento d'identità", html);
        Assert.DoesNotContain("Modulo detrazioni", html);
        Assert.Contains("Rinnova i documenti personali", WebUtility.HtmlDecode(await hr.GetStringAsync("/")));
        Assert.Empty(_app.ErroriServer);
    }

    [Fact]
    public async Task Le_variazioni_del_contratto_si_caricano_col_loro_tipo()
    {
        var (azienda, rossi, contratto) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var r = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 1, 1));
            var c = r.Assumi(TipoContratto.TempoDeterminato, "Commercio", "4", new DateOnly(2025, 1, 1), new DateOnly(2026, 12, 31), 28_000m);
            db.AddRange(a, r);
            await db.SaveChangesAsync();
            return (a.Id, r.Id, c.Id);
        });
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, azienda);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");
        var pagina = $"/Dipendenti/Dettaglio/{rossi}?sezione=contratto";

        async Task<string> Carica(string tipo, string? dal)
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(await hr.TokenDa(pagina)), "__RequestVerificationToken" }, { new StringContent(tipo), "tipo" },
            };
            if (dal is not null) form.Add(new StringContent(dal), "dataEffetto");
            form.Add(new ByteArrayContent(Pdf), "documento", "lettera.pdf");
            return await Testo(await hr.PostAsync($"/Dipendenti/CaricaContratto/{rossi}?contrattoId={contratto}", form));
        }
        Assert.Contains("Indica da quando vale la variazione", await Carica("Proroga", null));
        Assert.Contains("non può valere prima dell'inizio", await Carica("Proroga", "2024-06-01"));
        var html = await Carica("Proroga", "2027-01-01");
        Assert.Contains("Proroga caricata", html);
        Assert.Contains("dal 01/01/2027", html);
        Assert.Contains("Contratto caricato", await Carica("Assunzione", null));

        var c = await _app.ConDb(db => db.Contratti.SingleAsync());
        Assert.NotNull(c.DocumentoChiave);
        var lettera = Assert.Single(c.Lettere);
        Assert.Equal(TipoLettera.Proroga, lettera.Tipo);
        Assert.Contains("+ 1 variazione", WebUtility.HtmlDecode(await hr.GetStringAsync("/Documenti/Contratti")));

        // Il dipendente la vede in "Il mio contratto" e la apre.
        var u = await _app.CreaUtente("mario@acme.test", Ruoli.Dipendente);
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var x = (await users.FindByIdAsync(u.Id))!;
            (x.DipendenteId, x.AziendaId) = (rossi, azienda);
            await users.UpdateAsync(x);
        }
        await _app.CompletaDatiPersonali(rossi);
        var mario = _app.CreateClient();
        await mario.Accedi("mario@acme.test");
        Assert.Contains("Variazioni del contratto", WebUtility.HtmlDecode(await mario.GetStringAsync("/MioContratto")));
        var pdf = await mario.GetAsync($"/Documenti/ApriLettera/{contratto}?letteraId={lettera.Id}");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);

        // Rimossa la lettera, il file non si apre più.
        await hr.InviaForm(pagina, $"/Dipendenti/RimuoviLettera/{rossi}?contrattoId={contratto}&letteraId={lettera.Id}", new());
        Assert.Empty((await _app.ConDb(db => db.Contratti.SingleAsync())).Lettere);
        Assert.Empty(_app.ErroriServer);
    }

    [Fact]
    public async Task Subentro_progressivi_trattenute_e_archivio_storico()
    {
        var (azienda, rossi) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            a.ImpostaTipoAvvio(TipoAvvio.CambioConsulente);
            var r = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 1, 1));
            db.AddRange(a, r);
            await db.SaveChangesAsync();
            return (a.Id, r.Id);
        });
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, azienda);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");
        var pagina = $"/Dipendenti/Dettaglio/{rossi}?sezione=trattenute";

        // Progressivi al subentro.
        Assert.Contains("Progressivi al cambio consulente", WebUtility.HtmlDecode(await hr.GetStringAsync(pagina)));
        Assert.Contains("I ratei vanno da 0 a 12", await Testo(await hr.InviaForm(pagina, $"/Dipendenti/SalvaProgressivi/{rossi}",
            new() { ["al"] = "2026-11-30", ["ratei13"] = "13" })));
        Assert.Contains("Progressivi salvati", await Testo(await hr.InviaForm(pagina, $"/Dipendenti/SalvaProgressivi/{rossi}",
            new() { ["al"] = "2026-11-30", ["ratei13"] = "11", ["ratei14"] = "5", ["tfrAzienda"] = "4200", ["tfrFondi"] = "1800" })));
        var d = await _app.ConDb(db => db.Dipendenti.SingleAsync());
        Assert.Equal(new ProgressiviSubentro(new DateOnly(2026, 11, 30), 11, 5, 4200, 1800), d.Progressivi);

        // Trattenute e benefit.
        Assert.Contains("Indica da quando vale", await Testo(await hr.InviaForm(pagina, $"/Dipendenti/AggiungiTrattenuta/{rossi}",
            new() { ["tipo"] = "CessioneQuinto" })));
        var html = await Testo(await hr.InviaForm(pagina, $"/Dipendenti/AggiungiTrattenuta/{rossi}", new()
        {
            ["tipo"] = "CessioneQuinto", ["descrizione"] = "Banca Esempio", ["importoMensile"] = "250", ["residuo"] = "9000", ["dal"] = "2025-01-01",
        }));
        Assert.Contains("Cessione del quinto aggiunta", html);
        Assert.Contains("Banca Esempio", html);
        var voce = await _app.ConDb(db => db.TrattenuteBenefit.SingleAsync());
        Assert.Equal(250m, voce.ImportoMensile);
        await hr.InviaForm(pagina, $"/Dipendenti/TogliTrattenuta/{rossi}?voceId={voce.Id}", new());
        Assert.Equal(0, await _app.ConDb(db => db.TrattenuteBenefit.CountAsync()));

        // Archivio storico: compare tra le cose da fare finché è vuoto.
        Assert.Contains("Carica lo storico del consulente precedente", WebUtility.HtmlDecode(await hr.GetStringAsync("/")));
        using (var form = new MultipartFormDataContent
               {
                   { new StringContent(await hr.TokenDa("/Documenti/NuovoStorico")), "__RequestVerificationToken" },
                   { new StringContent("Modello770"), "tipo" }, { new StringContent("2024"), "anno" },
               })
        {
            form.Add(new ByteArrayContent(Pdf), "documento", "770.pdf");
            Assert.Contains("Modello 770 2024 caricato", await Testo(await hr.PostAsync("/Documenti/CaricaStorico", form)));
        }
        Assert.DoesNotContain("Carica lo storico del consulente precedente", WebUtility.HtmlDecode(await hr.GetStringAsync("/")));
        Assert.Contains("Modello 770", WebUtility.HtmlDecode(await hr.GetStringAsync("/Documenti/Storico?anno=2024")));
        Assert.Empty(_app.ErroriServer);
    }

    [Fact]
    public async Task L_archivio_storico_c_e_solo_per_chi_arriva_da_un_altro_consulente()
    {
        var (nuova, subentro) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Nuova S.r.l.", PartitaIva.Parse("01234567897"));
            var b = new Azienda("Subentro S.r.l.", PartitaIva.Parse("12345678903"));
            b.ImpostaTipoAvvio(TipoAvvio.CambioConsulente);
            db.AddRange(a, b);
            await db.SaveChangesAsync();
            return (a.Id, b.Id);
        });
        await _app.CreaUtente("hr@nuova.test", Ruoli.Azienda, nuova);
        await _app.CreaUtente("hr@subentro.test", Ruoli.Azienda, subentro);
        var hrNuova = _app.CreateClient(); await hrNuova.Accedi("hr@nuova.test");
        var hrSubentro = _app.CreateClient(); await hrSubentro.Accedi("hr@subentro.test");

        Assert.DoesNotContain("Archivio storico", WebUtility.HtmlDecode(await hrNuova.GetStringAsync("/Documenti/Personali")));
        Assert.Equal(HttpStatusCode.NotFound, (await hrNuova.GetAsync("/Documenti/Storico")).StatusCode);
        Assert.Contains("Archivio storico", WebUtility.HtmlDecode(await hrSubentro.GetStringAsync("/Documenti/Personali")));
        Assert.Equal(HttpStatusCode.OK, (await hrSubentro.GetAsync("/Documenti/Storico")).StatusCode);

        // Lo studio sceglie l'azienda solo tra quelle con l'archivio.
        await _app.CreaUtente("studio@test.it", Ruoli.Consulente);
        var studio = _app.CreateClient(); await studio.Accedi("studio@test.it");
        var modulo = WebUtility.HtmlDecode(await studio.GetStringAsync("/Documenti/NuovoStorico"));
        Assert.Contains("Subentro S.r.l.", modulo);
        Assert.DoesNotContain("Nuova S.r.l.", modulo);
    }
}
