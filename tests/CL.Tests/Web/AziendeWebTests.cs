using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CL.Domain.Aziende;
using CL.Domain.ValueObjects;
using CL.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace CL.Tests.Web;

public sealed class AziendeWebTests : IDisposable
{
    private readonly AppFactory _app = new();
    public void Dispose() => _app.Dispose();

    private static async Task<string> Testo(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0, 0x1F, 0x15, 0xC4,
        0x89, 0, 0, 0, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0, 1, 0, 0, 5, 0, 1, 0x0D, 0x0A, 0x2D, 0xB4, 0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    private async Task<HttpClient> Studio()
    {
        await _app.CreaUtente("consulente@test.it", Ruoli.Consulente);
        var c = _app.CreateClient();
        await c.Accedi("consulente@test.it");
        return c;
    }

    private static async Task<HttpResponseMessage> Salva(HttpClient c, Dictionary<string, string> campi, (string Nome, byte[] Dati)? logo = null)
    {
        using var form = new MultipartFormDataContent { { new StringContent(await c.TokenDa("/Aziende/Nuova")), "__RequestVerificationToken" } };
        foreach (var (k, v) in campi) form.Add(new StringContent(v), k);
        if (logo is { } l)
        {
            var file = new ByteArrayContent(l.Dati);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "Logo", l.Nome);
        }
        return await c.PostAsync("/Aziende/Salva", form);
    }

    [Fact]
    public async Task Ricerca_per_partita_iva_compila_i_dati_del_registro()
    {
        var consulente = await Studio();
        async Task<JsonElement> Cerca(string piva) =>
            JsonDocument.Parse(await consulente.GetStringAsync($"/Aziende/CercaPartitaIva?partitaIva={piva}")).RootElement;

        var trovata = await Cerca("IT 01234567897");
        Assert.Equal("trovata", trovata.GetProperty("esito").GetString());
        Assert.Equal("Acme S.r.l.", trovata.GetProperty("ragioneSociale").GetString());
        Assert.Equal("20121", trovata.GetProperty("cap").GetString());
        Assert.Equal("MI", trovata.GetProperty("provincia").GetString());

        Assert.Equal("non-valida", (await Cerca("01234567890")).GetProperty("esito").GetString()); // cifra di controllo errata
        Assert.Equal("non-disponibile", (await Cerca("12345678903")).GetProperty("esito").GetString());

        await Salva(consulente, new() { ["RagioneSociale"] = "Acme S.r.l.", ["PartitaIva"] = "01234567897" });
        Assert.Equal("esistente", (await Cerca("01234567897")).GetProperty("esito").GetString());
    }

    [Fact]
    public async Task Controlli_formali_sui_campi_dell_azienda()
    {
        var consulente = await Studio();
        var validi = new Dictionary<string, string>
        {
            ["RagioneSociale"] = "Acme S.r.l.", ["PartitaIva"] = "01234567897", ["Indirizzo"] = "Via Roma 1", ["Cap"] = "20121",
            ["Comune"] = "Milano", ["Provincia"] = "MI", ["Email"] = "info@acme.it",
        };

        Assert.Contains("CAP di 5 cifre", await Testo(await Salva(consulente, new(validi) { ["Cap"] = "2012" })));
        Assert.Contains("La provincia \"XX\" non esiste", await Testo(await Salva(consulente, new(validi) { ["Provincia"] = "XX" })));
        Assert.Contains("Email non valida", await Testo(await Salva(consulente, new(validi) { ["Email"] = "info@acme" })));
        Assert.Contains("cifra di controllo errata", await Testo(await Salva(consulente, new(validi) { ["CodiceFiscale"] = "01234567890" })));
        Assert.Contains("controllo", await Testo(await Salva(consulente, new(validi) { ["CodiceFiscale"] = "RSSMRA85T10A562X" })));
        Assert.Contains("partita IVA", await Testo(await Salva(consulente, new(validi) { ["PartitaIva"] = "01234567890" })));
        Assert.Equal(0, await _app.ConDb(db => db.Aziende.CountAsync()));

        // Ditta individuale: codice fiscale personale di 16 caratteri accettato.
        var html = await Testo(await Salva(consulente, new(validi) { ["CodiceFiscale"] = "rssmra85t10a562s" }));
        Assert.Contains("Azienda Acme S.r.l. creata", html);
        Assert.Contains("Via Roma 1, 20121 Milano (MI)", html);
        Assert.Equal("RSSMRA85T10A562S", (await _app.ConDb(db => db.Aziende.SingleAsync())).CodiceFiscale);
    }

    [Fact]
    public async Task Logo_caricato_visibile_allo_studio_e_alla_propria_azienda()
    {
        var consulente = await Studio();
        Assert.Contains("deve essere un'immagine",
            await Testo(await Salva(consulente, new() { ["RagioneSociale"] = "Acme S.r.l.", ["PartitaIva"] = "01234567897" }, ("logo.png", "<svg/>"u8.ToArray()))));

        var html = await Testo(await Salva(consulente, new() { ["RagioneSociale"] = "Acme S.r.l.", ["PartitaIva"] = "01234567897" }, ("logo.png", Png)));
        Assert.Contains("Azienda Acme S.r.l. creata", html);
        var acme = await _app.ConDb(db => db.Aziende.SingleAsync());
        Assert.Matches(@"^loghi/[0-9a-f-]+-[0-9a-f]{32}\.png$", acme.LogoChiave);
        Assert.Contains($"/Aziende/Logo/{acme.Id}", html);

        var logo = await consulente.GetAsync($"/Aziende/Logo/{acme.Id}");
        Assert.Equal("image/png", logo.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await logo.Content.ReadAsByteArrayAsync());

        var altra = await _app.ConDb(async db =>
        {
            var a = new Azienda("Altra S.p.A.", PartitaIva.Parse("12345678903"));
            db.Add(a);
            await db.SaveChangesAsync();
            return a.Id;
        });
        await _app.CreaUtente("hr@acme.test", Ruoli.Azienda, acme.Id);
        var hr = _app.CreateClient();
        await hr.Accedi("hr@acme.test");
        Assert.Equal(HttpStatusCode.OK, (await hr.GetAsync($"/Aziende/Logo/{acme.Id}")).StatusCode);
        Assert.Contains($"/Aziende/Logo/{acme.Id}", await hr.GetStringAsync("/")); // nella barra in alto
        Assert.Equal(HttpStatusCode.NotFound, (await hr.GetAsync($"/Aziende/Logo/{altra}")).StatusCode);
    }
}
