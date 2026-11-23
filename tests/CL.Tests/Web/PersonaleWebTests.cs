using System.Net;
using CL.Domain.Aziende;
using CL.Domain.Common;
using CL.Domain.Dipendenti;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CL.Tests.Web;

public sealed class PersonaleWebTests : IDisposable
{
    private readonly AppFactory _app = new();
    public void Dispose() => _app.Dispose();

    private static async Task<string> Testo(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

    [Fact]
    public async Task Switch_tra_personale_dipendente_e_professionisti_esterni()
    {
        var anno = DateTime.Today.Year;
        var (azienda, rossi, bianchi) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var r = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 1, 1));
            r.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(anno, 1, 1), null, 28_000m);
            var b = new Dipendente(a.Id, "Laura", "Bianchi", CodiceFiscale.Parse("RSSLRA15A41F839L"), new DateOnly(1985, 1, 1));
            db.AddRange(a, r, b);
            await db.SaveChangesAsync();
            return (a.Id, r.Id, b.Id);
        });
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, azienda);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");

        // Lavoro dipendente senza CCNL: rifiutato. Partita IVA senza CCNL: accettata, senza ferie da maturare.
        var campi = new Dictionary<string, string>
        {
            ["DipendenteId"] = bianchi.ToString(), ["Tipo"] = "TempoIndeterminato", ["DataInizio"] = $"{anno}-02-01",
            ["RetribuzioneLordaAnnua"] = "30000", ["Mansione"] = "Consulenza grafica",
        };
        Assert.Contains("Inserisci il CCNL", await Testo(await hr.InviaForm($"/Dipendenti/Assumi/{bianchi}", "/Dipendenti/Assumi", new(campi))));
        var html = await Testo(await hr.InviaForm($"/Dipendenti/Assumi/{bianchi}", "/Dipendenti/Assumi", new(campi) { ["Tipo"] = "LavoroAutonomo" }));
        Assert.Contains("Incarico registrato", html);
        Assert.Contains("Professionista esterno", html);
        var contratto = (await _app.ConDb(db => db.Dipendenti.SingleAsync(d => d.Id == bianchi))).Contratti.Single();
        Assert.Equal(0m, contratto.SpettanzaAnnua(CL.Domain.Assenze.TipoAssenza.Ferie));

        html = WebUtility.HtmlDecode(await hr.GetStringAsync("/Dipendenti"));
        Assert.Contains("Personale dipendente", html);
        Assert.Contains("Rossi Mario", html);
        Assert.DoesNotContain("Bianchi Laura", html);
        Assert.Contains("Scadenza contratto", html);
        Assert.Contains("28.000", html); // RAL del contratto in corso
        Assert.Contains("Mensile lordo", html);
        Assert.Contains("Indeterminato", html);

        html = WebUtility.HtmlDecode(await hr.GetStringAsync("/Dipendenti?esterni=true"));
        Assert.Contains("Bianchi Laura", html);
        Assert.Contains("Partita IVA / lavoro autonomo", html);
        Assert.Contains("Compenso annuo", html);
        Assert.DoesNotContain("Rossi Mario", html);
        Assert.Contains("Aggiungi professionista", html);

        // Gli esterni non hanno presenze da comunicare.
        var foglio = WebUtility.HtmlDecode(await hr.GetStringAsync($"/Presenze/Foglio?anno={anno}&mese={DateTime.Today.Month}"));
        Assert.Contains("Rossi Mario", foglio);
        Assert.DoesNotContain("Bianchi Laura", foglio);
        Assert.Empty(_app.ErroriServer);
    }

    [Fact]
    public void Contratto_da_dipendente_richiede_ccnl_quello_esterno_no()
    {
        var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
        var d = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 1, 1));
        Assert.Throws<DomainException>(() => d.Assumi(TipoContratto.TempoIndeterminato, "", "", new DateOnly(2026, 1, 1), null, 28_000m));
        var c = d.Assumi(TipoContratto.Collaborazione, "", "", new DateOnly(2026, 1, 1), null, 12_000m);
        Assert.False(c.Tipo.IsSubordinato());
        Assert.Equal(0m, c.OreFerieAnnue);
    }

    [Fact]
    public async Task Scheda_contratto_con_caricamento_del_pdf()
    {
        var (azienda, rossi, contratto) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var r = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 1, 1));
            var c = r.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(2025, 1, 1), null, 28_000m);
            db.AddRange(a, r);
            await db.SaveChangesAsync();
            return (a.Id, r.Id, c.Id);
        });
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, azienda);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");

        var pagina = $"/Dipendenti/Modifica/{rossi}?sezione=contratto";
        var html = WebUtility.HtmlDecode(await hr.GetStringAsync(pagina));
        Assert.Contains("Contratto in corso", html);
        Assert.Contains("Commercio", html);
        Assert.Contains("Nessun documento caricato", html);

        // Dalla scheda Contratto la foto si salva subito.
        using (var form = new MultipartFormDataContent { { new StringContent(await hr.TokenDa(pagina)), "__RequestVerificationToken" } })
        {
            form.Add(new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2]), "immagine", "foto.png");
            html = await Testo(await hr.PostAsync($"/Dipendenti/CaricaFoto/{rossi}?sezione=contratto", form));
        }
        Assert.Contains("Foto aggiornata", html);
        Assert.Contains("Contratto in corso", html); // si resta sulla scheda Contratto
        Assert.Contains("Foto rimossa", await Testo(await hr.InviaForm(pagina, $"/Dipendenti/TogliFoto/{rossi}?sezione=contratto", [])));

        async Task<string> Carica(byte[] contenuto, string nome)
        {
            using var form = new MultipartFormDataContent { { new StringContent(await hr.TokenDa(pagina)), "__RequestVerificationToken" } };
            form.Add(new ByteArrayContent(contenuto), "documento", nome);
            return await Testo(await hr.PostAsync($"/Dipendenti/CaricaContratto/{rossi}?contrattoId={contratto}", form));
        }
        Assert.Contains("deve essere un file PDF", await Carica([0x89, 0x50, 0x4E, 0x47], "finto.pdf"));
        byte[] pdf = [.. "%PDF-1.7\n"u8.ToArray(), 1, 2, 3];
        html = await Carica(pdf, "contratto-rossi.pdf");
        Assert.Contains("Contratto caricato", html);
        Assert.Contains("contratto-rossi.pdf", html);
        Assert.Equal(pdf, await hr.GetByteArrayAsync($"/Dipendenti/DocumentoContratto/{rossi}?contrattoId={contratto}"));

        html = await Testo(await hr.InviaForm(pagina, $"/Dipendenti/RimuoviContratto/{rossi}?contrattoId={contratto}", []));
        Assert.Contains("Documento del contratto rimosso", html);
        Assert.Null((await _app.ConDb(db => db.Contratti.SingleAsync())).DocumentoChiave);
        Assert.Empty(_app.ErroriServer);
    }

    [Fact]
    public async Task Stati_attivi_firmati_scaduti_cessati()
    {
        var oggi = DateOnly.FromDateTime(DateTime.Today);
        var azienda = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            Dipendente D(string nome, string cf) => new(a.Id, nome, "Prova", CodiceFiscale.Parse(cf), new DateOnly(1985, 1, 1));
            var attivo = D("Attilio", "RSSMRA85T10A562S");
            attivo.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", oggi.AddYears(-1), null, 28_000m);
            var firmato = D("Fabio", "RSSLRA15A41F839L");
            firmato.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", oggi.AddDays(10), null, 28_000m);
            var scaduto = D("Sara", "VRDNNA88C41F839V");
            scaduto.Assumi(TipoContratto.TempoDeterminato, "Commercio", "4", oggi.AddYears(-1), oggi.AddDays(-5), 28_000m);
            var cessato = D("Carlo", "BNCLRA80A41F839F");
            cessato.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", oggi.AddYears(-1), null, 28_000m);
            cessato.Cessa(oggi.AddDays(-3));
            db.AddRange(a, attivo, firmato, scaduto, cessato, D("Nadia", "NDANDA90A41F839O"));
            await db.SaveChangesAsync();
            return a.Id;
        });
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, azienda);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");

        var html = WebUtility.HtmlDecode(await hr.GetStringAsync("/Dipendenti"));
        Assert.Contains("Tutti <span>(5)</span>", html);
        foreach (var stato in new[] { "Attivi", "Firmati", "Scaduti", "Cessati" }) Assert.Contains($"{stato} <span>(1)</span>", html);
        Assert.DoesNotContain("In arrivo", html);
        Assert.Contains("Sara", WebUtility.HtmlDecode(await hr.GetStringAsync("/Dipendenti?stato=scaduti")));
        html = WebUtility.HtmlDecode(await hr.GetStringAsync("/Dipendenti?stato=cessati"));
        Assert.Contains("Carlo", html);
        Assert.DoesNotContain("Sara", html);
    }

    [Fact]
    public async Task Residenza_con_suggerimenti_e_campi_separati()
    {
        var (azienda, rossi) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var r = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 1, 1));
            db.AddRange(a, r);
            await db.SaveChangesAsync();
            return (a.Id, r.Id);
        });
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, azienda);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");

        Assert.Contains("\"provincia\":\"MI\"", await hr.GetStringAsync("/Indirizzi/Cerca?q=via%20roma%2010"));
        Assert.Equal("[]", await hr.GetStringAsync("/Indirizzi/Cerca?q=ro")); // troppo corto: nessuna ricerca
        Assert.Equal(HttpStatusCode.Redirect, (await _app.CreateClient(new() { AllowAutoRedirect = false }).GetAsync("/Indirizzi/Cerca?q=via%20roma")).StatusCode);

        // Visualizza e modifica sono la stessa scheda: cambia solo se i campi sono chiusi o aperti.
        var lettura = WebUtility.HtmlDecode(await hr.GetStringAsync($"/Dipendenti/Dettaglio/{rossi}"));
        Assert.Contains("class=\"scheda-profilo \" id=\"scheda\"", lettura);
        Assert.Contains("Modifica dati", lettura);
        Assert.Contains("Assenze e presenze", lettura);
        Assert.Contains("aria-current=\"page\">Rossi Mario</li>", lettura); // breadcrumb: Personale › persona
        var aperta = WebUtility.HtmlDecode(await hr.GetStringAsync($"/Dipendenti/Modifica/{rossi}"));
        Assert.Contains("class=\"scheda-profilo in-modifica\" id=\"scheda\"", aperta);

        var pagina = $"/Dipendenti/Modifica/{rossi}";
        var campi = new Dictionary<string, string>
        {
            ["Id"] = rossi.ToString(), ["AziendaId"] = azienda.ToString(), ["Nome"] = "Mario", ["Cognome"] = "Rossi",
            ["CodiceFiscale"] = "RSSMRA85T10A562S", ["DataNascita"] = "1985-01-01", ["Indirizzo"] = "Via Roma 10", ["Cap"] = "20121",
            ["Comune"] = "Milano", ["Provincia"] = "XX",
        };
        Assert.Contains("non esiste, usa la sigla", await Testo(await hr.InviaForm(pagina, "/Dipendenti/Salva", new(campi))));
        Assert.Contains("Modifiche salvate", await Testo(await hr.InviaForm(pagina, "/Dipendenti/Salva", new(campi) { ["Provincia"] = "mi" })));
        var d = await _app.ConDb(db => db.Dipendenti.SingleAsync(x => x.Id == rossi));
        Assert.Equal(("Via Roma 10", "20121", "Milano", "MI"), (d.Indirizzo, d.Cap, d.Comune, d.Provincia));
        Assert.Empty(_app.ErroriServer);
    }

    [Theory]
    [InlineData("Milano", "MI"), InlineData("Roma Capitale", "RM"), InlineData("Reggio nell'Emilia", "RE"), InlineData("Reggio di Calabria", "RC")]
    [InlineData("Ravenna", "RA"), InlineData("Enna", "EN"), InlineData("Città metropolitana di Napoli", "NA"), InlineData("Forlì-Cesena", "FC")]
    [InlineData("Provincia autonoma di Bolzano - Alto Adige", "BZ"), InlineData("Barletta-Andria-Trani", "BT"), InlineData("Atlantide", null)]
    public void Sigla_della_provincia_dal_nome_openstreetmap(string nome, string? sigla) =>
        Assert.Equal(sigla, CL.Infrastructure.Indirizzi.RicercaPhoton.SiglaProvincia(nome));

    [Fact]
    public void Comuni_dall_elenco_istat()
    {
        Assert.Equal(new Comune("Cassino", "FR"), Comuni.Trova("CASSINO", "fr"));
        Assert.Equal("Sant'Agata de' Goti", Comuni.Trova("sant agata de goti")?.Nome);
        Assert.Null(Comuni.Trova("Cassino", "MI"));
        Assert.Equal(107, Comuni.Province.Count);
        Assert.Equal("Cassino", Comuni.Cerca("cass", "FR").First().Nome);

        var errore = Assert.Throws<DomainException>(() => new CL.Domain.Aziende.Indirizzo("Via Roma 1", "03043", "Cassino", "MI").Validato("di residenza"));
        Assert.Contains("Cassino è in provincia di FR", errore.Message);
        Assert.Contains("non risulta tra i comuni italiani",
            Assert.Throws<DomainException>(() => new CL.Domain.Aziende.Indirizzo("Via Roma 1", "20121", "Milanoo", "MI").Validato("di residenza")).Message);
        Assert.Equal("Milano", new CL.Domain.Aziende.Indirizzo("Via Roma 1", "20121", "MILANO", "mi").Validato("di residenza").Comune);
    }

    [Fact]
    public async Task Ricerca_dei_comuni_per_la_select()
    {
        var azienda = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            db.Add(a);
            await db.SaveChangesAsync();
            return a.Id;
        });
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, azienda);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");
        var json = await hr.GetStringAsync("/Indirizzi/Comuni?q=cassin&p=FR");
        Assert.Contains("\"id\":\"Cassino\"", json);
        Assert.Contains("\"provincia\":\"FR\"", json);
    }

    [Fact]
    public async Task Dati_per_le_paghe_modificati_dalla_scheda()
    {
        var (azienda, rossi) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var r = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 1, 1));
            db.AddRange(a, r);
            await db.SaveChangesAsync();
            return (a.Id, r.Id);
        });
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, azienda);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");
        var pagina = $"/Dipendenti/Modifica/{rossi}";
        Assert.Contains("class=\"modulo-paghe mt-4\"", await hr.GetStringAsync(pagina)); // i campi sono nella stessa pagina

        var anagrafica = new Dictionary<string, string>
        {
            ["Id"] = rossi.ToString(), ["AziendaId"] = azienda.ToString(), ["Nome"] = "Mario", ["Cognome"] = "Rossi",
            ["CodiceFiscale"] = "RSSMRA85T10A562S", ["DataNascita"] = "1985-01-01", ["Telefono"] = "3331234567",
            ["Indirizzo"] = "Via Toledo 10", ["Cap"] = "80134", ["Comune"] = "Napoli", ["Provincia"] = "NA", ["Iban"] = "IT60X0542811101000000123456",
        };
        // Senza toccare i dati per le paghe si salva solo l'anagrafica (anche se sono incompleti).
        Assert.Contains("Modifiche salvate.", await Testo(await hr.InviaForm(pagina, "/Dipendenti/Salva", new(anagrafica))));
        Assert.Null(await _app.ConDb(db => db.ProfiliDipendente.FirstOrDefaultAsync()));

        var campi = new Dictionary<string, string>(anagrafica)
        {
            ["salvaPaghe"] = "true",
            ["paghe.Anagrafica.ComuneNascita"] = "Napoli", ["paghe.Anagrafica.ProvinciaNascita"] = "NA", ["paghe.Anagrafica.Cittadinanza"] = "Italiana",
            ["paghe.Pagamento.Tfr"] = "Azienda", ["paghe.Famiglia.RichiedeDetrazioni"] = "true",
            ["paghe.Famiglia.Familiari[0].Relazione"] = "Figlio", ["paghe.Famiglia.Familiari[0].Nome"] = "Luca", ["paghe.Famiglia.Familiari[0].Cognome"] = "Rossi",
            ["paghe.Famiglia.Familiari[0].CodiceFiscale"] = "XXX", ["paghe.Famiglia.Familiari[0].DataNascita"] = "2015-05-01", ["paghe.Famiglia.Familiari[0].PercentualeCarico"] = "100",
            ["paghe.Altri.TitoloStudio"] = "Diploma", ["paghe.Altri.TipoDocumento"] = "Passaporto", ["paghe.Altri.NumeroDocumento"] = "YA1234567",
            ["paghe.Altri.ScadenzaDocumento"] = DateTime.Today.AddYears(3).ToString("yyyy-MM-dd"),
        };
        // Codice fiscale del familiare sbagliato: nulla di salvato, la scheda resta aperta con l'errore.
        var html = await Testo(await hr.InviaForm(pagina, "/Dipendenti/Salva", new(campi)));
        Assert.Contains("in-modifica-paghe", html);
        Assert.Null(await _app.ConDb(db => db.ProfiliDipendente.FirstOrDefaultAsync()));

        foreach (var k in campi.Keys.Where(k => k.StartsWith("paghe.Famiglia.Familiari[0].") && !k.EndsWith("Relazione") && !k.EndsWith("PercentualeCarico")).ToList()) campi.Remove(k);
        html = await Testo(await hr.InviaForm(pagina, "/Dipendenti/Salva", new(campi)));
        Assert.Contains("anche nei dati per le paghe", html);
        var p = await _app.ConDb(db => db.ProfiliDipendente.SingleAsync());
        Assert.Equal(("Napoli", "NA", "Passaporto", "3331234567"), (p.ResidenzaComune, p.ResidenzaProvincia, p.TipoDocumento, p.Telefono));
        Assert.Equal("IT60X0542811101000000123456", p.Iban?.Valore); // residenza, IBAN e cellulare presi dall'anagrafica
        Assert.Empty(_app.ErroriServer);
    }

    [Fact]
    public async Task L_azienda_modifica_contratto_e_organizzazione_dalla_scheda()
    {
        var (azienda, rossi, verdi, contratto, team) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var r = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 1, 1));
            var v = new Dipendente(a.Id, "Laura", "Verdi", CodiceFiscale.Parse("RSSLRA15A41F839L"), new DateOnly(1985, 1, 1));
            var c = r.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(2025, 1, 1), null, 28_000m);
            var t = new CL.Domain.Organizzazione.Team(a.Id, "Vendite");
            db.AddRange(a, r, v, t);
            await db.SaveChangesAsync();
            return (a.Id, r.Id, v.Id, c.Id, t.Id);
        });
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, azienda);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");
        var pagina = $"/Dipendenti/Modifica/{rossi}?sezione=contratto";

        var contrattoCampi = new Dictionary<string, string>
        {
            ["contratto.ContrattoId"] = contratto.ToString(), ["contratto.Tipo"] = "TempoIndeterminato", ["contratto.Ccnl"] = "Commercio",
            ["contratto.Livello"] = "3", ["contratto.Mansione"] = "Responsabile vendite", ["contratto.DataInizio"] = "2025-01-01",
            ["contratto.RetribuzioneLordaAnnua"] = "32.000", ["contratto.MensilitaAnnue"] = "14", ["contratto.OreSettimanaliFullTime"] = "40",
            ["contratto.PercentualePartTime"] = "100", ["contratto.GiorniLavorativiSettimana"] = "5",
            ["contratto.OreFerieAnnue"] = "160", ["contratto.OreRolAnnue"] = "72"
        };
        Assert.Contains("Inserisci il CCNL", await Testo(await hr.InviaForm(pagina, $"/Dipendenti/ModificaContratto/{rossi}", new(contrattoCampi) { ["contratto.Ccnl"] = "" })));
        var html = await Testo(await hr.InviaForm(pagina, $"/Dipendenti/ModificaContratto/{rossi}", new(contrattoCampi)));
        Assert.Contains("Contratto aggiornato", html);
        Assert.Contains("class=\"mansione-profilo\">Responsabile vendite<", html); // sotto l'azienda, nell'intestazione
        var c = await _app.ConDb(db => db.Contratti.SingleAsync());
        Assert.Equal((32_000m, "3", "Responsabile vendite"), (c.RetribuzioneLordaAnnua, c.Livello, c.Mansione));

        // Team, posizione e responsabile diretto si salvano con l'anagrafica.
        html = await Testo(await hr.InviaForm($"/Dipendenti/Modifica/{rossi}", "/Dipendenti/Salva", new()
        {
            ["Id"] = rossi.ToString(), ["AziendaId"] = azienda.ToString(), ["Nome"] = "Mario", ["Cognome"] = "Rossi",
            ["CodiceFiscale"] = "RSSMRA85T10A562S", ["DataNascita"] = "1985-01-01",
            ["TeamId"] = team.ToString(), ["Posizione"] = "Commerciale", ["ResponsabileId"] = verdi.ToString(),
        }));
        Assert.Contains("Modifiche salvate", html);
        var d = await _app.ConDb(db => db.Dipendenti.SingleAsync(x => x.Id == rossi));
        Assert.Equal((team, "Commerciale", verdi), (d.TeamId!.Value, d.Posizione, d.ResponsabileId!.Value));
        Assert.Contains("Verdi Laura", html); // approva ferie e permessi
        Assert.Empty(_app.ErroriServer);
    }

    [Fact]
    public async Task L_azienda_cambia_l_email_di_accesso_del_dipendente()
    {
        var (azienda, rossi) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var r = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 1, 1));
            r.AggiornaRecapiti("mario@vecchia.it", null);
            db.AddRange(a, r);
            await db.SaveChangesAsync();
            return (a.Id, r.Id);
        });
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, azienda);
        await _app.CreaUtente("altro@acme.test", Ruoli.Azienda, azienda);
        var u = await _app.CreaUtente("mario@vecchia.it", Ruoli.Dipendente);
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var x = (await users.FindByIdAsync(u.Id))!;
            (x.DipendenteId, x.AziendaId, x.EmailConfirmed) = (rossi, azienda, true);
            await users.UpdateAsync(x);
        }
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");
        var pagina = $"/Dipendenti/Modifica/{rossi}";
        Assert.Contains("Invia email di reset password", WebUtility.HtmlDecode(await hr.GetStringAsync(pagina)));

        Assert.Contains("Esiste già un accesso", await Testo(await hr.InviaForm(pagina, $"/Utenti/CambiaEmail/{u.Id}", new() { ["email"] = "altro@acme.test" })));
        Assert.Contains("Email di accesso cambiata in mario@nuova.it", await Testo(await hr.InviaForm(pagina, $"/Utenti/CambiaEmail/{u.Id}", new() { ["email"] = "mario@nuova.it" })));
        Assert.Equal("mario@nuova.it", (await _app.ConDb(db => db.Dipendenti.SingleAsync(d => d.Id == rossi))).Email); // anche l'anagrafica
        var mario = _app.CreateClient();
        await mario.Accedi("mario@nuova.it");
        Assert.Contains("Nuovo link", await Testo(await hr.InviaForm(pagina, $"/Utenti/Reinvia/{u.Id}", [])));
    }

    [Fact]
    public void Lavoro_ibrido_richiede_i_giorni_in_ufficio()
    {
        var d = new Dipendente(Guid.NewGuid(), "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 1, 1));
        Assert.Throws<DomainException>(() => d.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(2026, 1, 1), null, 28_000m,
            modalita: ModalitaLavoro.Ibrido));
        Assert.Throws<DomainException>(() => d.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(2026, 1, 1), null, 28_000m,
            modalita: ModalitaLavoro.Ibrido, giorniInUfficio: 5)); // 5 su 5 è in presenza
        var c = d.Assumi(TipoContratto.TempoIndeterminato, "Commercio", "4", new DateOnly(2026, 1, 1), null, 28_000m, modalita: ModalitaLavoro.Ibrido, giorniInUfficio: 3);
        Assert.Equal((ModalitaLavoro.Ibrido, 3), (c.Modalita, c.GiorniInUfficio));
        d.ModificaContratto(c.Id, c.Tipo, c.Ccnl, c.Livello, c.DataInizio, null, c.RetribuzioneLordaAnnua, c.MensilitaAnnue, c.OreSettimanaliFullTime,
            c.PercentualePartTime, c.GiorniLavorativiSettimana, c.OreFerieAnnue, c.OreRolAnnue, c.Mansione, ModalitaLavoro.FullRemote, 3);
        Assert.Null(c.GiorniInUfficio); // i giorni contano solo per l'ibrido
    }

    [Fact]
    public async Task Sede_di_lavoro_tra_le_sedi_dell_azienda_o_smart_working()
    {
        var (azienda, rossi) = await _app.ConDb(async db =>
        {
            var a = new Azienda("Acme S.r.l.", PartitaIva.Parse("01234567897"));
            var r = new Dipendente(a.Id, "Mario", "Rossi", CodiceFiscale.Parse("RSSMRA85T10A562S"), new DateOnly(1985, 1, 1));
            db.AddRange(a, r);
            await db.SaveChangesAsync();
            return (a.Id, r.Id);
        });
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, azienda); // il profilo di prova ha solo la sede legale
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");
        var pagina = $"/Dipendenti/Modifica/{rossi}";
        var html = WebUtility.HtmlDecode(await hr.GetStringAsync(pagina));
        Assert.Contains("Sede legale · Via Roma 1, Milano (MI)", html);
        Assert.Contains("Smart working", html);

        var campi = new Dictionary<string, string>
        {
            ["Id"] = rossi.ToString(), ["AziendaId"] = azienda.ToString(), ["Nome"] = "Mario", ["Cognome"] = "Rossi",
            ["CodiceFiscale"] = "RSSMRA85T10A562S", ["DataNascita"] = "1985-01-01", ["Sede"] = "SedeOperativa",
        };
        Assert.Contains("non è tra quelle dell'azienda", await Testo(await hr.InviaForm(pagina, "/Dipendenti/Salva", new(campi))));
        Assert.Contains("Modifiche salvate", await Testo(await hr.InviaForm(pagina, "/Dipendenti/Salva", new(campi) { ["Sede"] = "SmartWorking" })));
        Assert.Equal(SedeLavoro.SmartWorking, (await _app.ConDb(db => db.Dipendenti.SingleAsync())).Sede);
    }
}
