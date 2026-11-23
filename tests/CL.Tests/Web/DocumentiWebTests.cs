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
}
