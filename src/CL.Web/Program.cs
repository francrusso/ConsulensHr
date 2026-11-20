using System.Globalization;
using CL.Infrastructure;
using CL.Infrastructure.Identity;
using CL.Infrastructure.Persistence;
using CL.Web.Infrastruttura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Authorization;

var builder = WebApplication.CreateBuilder(args);

// Cedolini su disco, fuori da wwwroot (mai serviti come file statici). Sovrascrivibile con "Archivio:Percorso".
if (string.IsNullOrWhiteSpace(builder.Configuration["Archivio:Percorso"]))
    builder.Configuration["Archivio:Percorso"] = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "archivio");

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.LogoutPath = "/Account/Logout";
    o.AccessDeniedPath = "/Account/AccessoNegato";
    o.Cookie.Name = "cl.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policy.SoloConsulente, p => p.RequireRole(Ruoli.Consulente))
    .AddPolicy(Policy.GestionePersonale, p => p.RequireRole(Ruoli.Consulente, Ruoli.Azienda))
    .AddPolicy(Policy.ApprovazioneAssenze, p => p.RequireRole(Ruoli.Azienda));

// Lettere accentate scritte così come sono nell'HTML (senza &#xE0; ecc.).
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(o =>
    o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<UtenteCorrente>();
builder.Services.AddScoped<ServizioSaldi>();
builder.Services.AddScoped<ServizioAccessi>();
builder.Services.AddScoped<ServizioBustePaga>();
builder.Services.AddScoped<NotificheAssenze>();
builder.Services.AddScoped<ServizioPresenze>();

builder.Services.AddControllersWithViews(o =>
{
    // Tutto richiede login, salvo le azioni marcate [AllowAnonymous].
    o.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
    o.Filters.Add<FiltroOnboarding>();
});

var app = builder.Build();

var italiano = new CultureInfo("it-IT");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(italiano),
    SupportedCultures = [italiano],
    SupportedUICultures = [italiano],
});

// In sviluppo le migrazioni si applicano all'avvio; in produzione vanno applicate dalla pipeline di rilascio.
// L'ambiente "Testing" (test di integrazione) prepara il database da sé.
if (!app.Environment.IsEnvironment("Testing"))
    await DatabaseInitializer.InizializzaAsync(app.Services, applicaMigrazioni: app.Environment.IsDevelopment());

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

public partial class Program; // per WebApplicationFactory nei test
