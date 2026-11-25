# Consulens HR

Piattaforma Consulens per uno studio di consulenza del lavoro e le aziende sue clienti: anagrafica e contratti del
personale, ferie e permessi con i saldi in ore, presenze mensili, smart working, distribuzione di buste paga, CU e
contratti. I cedolini non si calcolano qui: i PDF arrivano dal gestionale paghe dello studio.

**Tecnologie**: .NET 10, ASP.NET Core MVC con viste Razor, Entity Framework Core 10, PostgreSQL su Supabase (SQLite per
la demo e i test), ASP.NET Identity, xUnit.

## Avvio rapido

Demo locale, senza database esterno:

```bash
dotnet user-secrets set "ConnectionStrings:Default" "Data Source=file.db" --project src/CL.Web
```

```bash
dotnet user-secrets set "Seed:Consulente:Email" "consulente@esempio.it" --project src/CL.Web
```

```bash
dotnet user-secrets set "Seed:Consulente:Password" "una-password-di-almeno-10-caratteri" --project src/CL.Web
```

```bash
dotnet run --project src/CL.Web
```

Poi apri http://localhost:5256 e accedi con l'utente indicato.

Test:

```bash
dotnet test ConsulenzaLavoro.sln
```

## Documentazione

| Documento | Contenuto |
|---|---|
| [docs/funzionalita.md](docs/funzionalita.md) | Cosa fa l'applicazione, sezione per sezione, e chi può fare cosa (super amministratore, consulente, referente azienda, dipendente). |
| [docs/architettura.md](docs/architettura.md) | Progetti, percorso di una richiesta, perimetro dei dati per ruolo, dominio, database, email, archivio dei documenti. |
| [docs/sviluppo.md](docs/sviluppo.md) | Configurazione, Supabase, migrazioni, app di prova online su Render, test e regole del progetto (lingua, grafica, segreti). |

## Struttura

```
src/
  CL.Domain/          entità e regole di business, senza dipendenze esterne
  CL.Infrastructure/  database (EF Core), login (Identity), email, archivio file, servizi esterni
  CL.Web/             controller, viste Razor, servizi delle pagine, file statici
tests/
  CL.Tests/           test del dominio, della persistenza e dell'app web completa
docs/                 documentazione
Dockerfile            immagine dell'app web, usata da Render
render.yaml           app di prova online sul piano gratuito di Render
```
