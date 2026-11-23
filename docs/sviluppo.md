# Guida allo sviluppo

Come avviare il progetto, configurarlo, cambiare il database e lanciare i test.
Per capire com'è fatto il codice vedi [architettura.md](architettura.md).

## Requisiti

- .NET SDK 10 (`dotnet --version` ≥ 10.0)
- Per il database vero: un progetto Supabase (PostgreSQL). Per provare l'interfaccia basta SQLite, già incluso.

## Avvio rapido (demo locale su SQLite)

Non serve nessun server: lo schema viene creato da solo in un file `file.db`.

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

L'app risponde su http://localhost:5256 (profilo `http`) o https://localhost:7004 (profilo `https`).
Al primo avvio vengono creati i ruoli e l'utente consulente indicato in `Seed:Consulente`.

## Configurazione

Le impostazioni sensibili vanno **sempre** negli user-secrets (in sviluppo) o nelle variabili d'ambiente
(in produzione), mai in `appsettings.json`. Il file `.gitignore` esclude già `*.db` e `App_Data/`.

| Chiave | A cosa serve |
|---|---|
| `ConnectionStrings:Default` | Connessione al database. `Host=...` = PostgreSQL/Supabase; `Data Source=file.db` = demo SQLite. Obbligatoria. |
| `Seed:Consulente:Email` / `Password` | Primo utente consulente, creato all'avvio se non esiste. |
| `Seed:Amministratore:Email` / `Password` | Super amministratore. Senza password l'account nasce come un invito: la password si sceglie da "Password dimenticata?". |
| `Email:Host`, `Porta` (587), `Ssl` (true), `Utente`, `Password`, `Mittente`, `NomeMittente` | Server SMTP per inviti, recupero password e notifiche. Senza `Host` e `Mittente` le email sono disattivate e il link di invito compare a schermo. |
| `App:UrlPubblico` | Indirizzo pubblico del sito usato nei link delle email (in produzione è obbligatorio di fatto: non ci si fida dell'header `Host`). |
| `Archivio:Percorso` | Cartella dei documenti caricati (buste paga, CU, contratti, foto, loghi). Predefinita: `src/CL.Web/App_Data/archivio`, fuori da `wwwroot`. |

### Supabase

Le tabelle stanno nello schema `hr`, non in `public`: lo schema `public` di Supabase è esposto dalla sua API REST.
L'autenticazione è ASP.NET Identity (tabelle nello stesso schema), non Supabase Auth.

Usare la connessione diretta o il **session pooler** (porta 5432). Il transaction pooler (porta 6543) non supporta i
prepared statement di Npgsql. Il codice aggiunge da sé alla stringa di connessione i parametri che evitano le connessioni
chiuse dal pooler dopo una pausa (`DependencyInjection.ConnessioneRobusta`).

## Database e migrazioni

- In **Development** le migrazioni si applicano all'avvio.
- In **produzione** vanno applicate dalla pipeline di rilascio, con lo script idempotente
  `src/CL.Infrastructure/Persistence/Migrations/schema.sql` (si può rilanciare senza danni).
- In modalità **demo SQLite** non si usano le migrazioni: lo schema si crea con `EnsureCreated`.

Gli strumenti `dotnet ef` sono fissati in `dotnet-tools.json`:

```bash
dotnet tool restore
```

Nuova migrazione dopo una modifica al modello (sostituire `NomeMigrazione`):

```bash
dotnet ef migrations add NomeMigrazione --project src/CL.Infrastructure --startup-project src/CL.Web --output-dir Persistence/Migrations
```

Rigenerare lo script per la produzione:

```bash
dotnet ef migrations script --idempotent --project src/CL.Infrastructure --startup-project src/CL.Web --output src/CL.Infrastructure/Persistence/Migrations/schema.sql
```

Controllare che il modello non abbia modifiche senza migrazione:

```bash
dotnet ef migrations has-pending-model-changes --project src/CL.Infrastructure --startup-project src/CL.Web
```

`DesignTimeDbContextFactory` permette a `dotnet ef` di lavorare senza aprire connessioni al database.

## Test

```bash
dotnet test ConsulenzaLavoro.sln
```

- `tests/CL.Tests/Domain`: regole di dominio pure (saldi, calendario, contratti, value object…).
- `tests/CL.Tests/Infrastructure`: persistenza su SQLite e funzioni dei servizi esterni.
- `tests/CL.Tests/Web`: test di integrazione dell'app completa (`AppFactory`): SQLite in memoria, archivio in una
  cartella temporanea, servizi esterni (VIES, ricerca indirizzi) sostituiti da finti. Nessun accesso a Supabase o a internet.

Per scrivere un test web: `factory.CreaUtente(email, ruolo)` crea un utente di qualunque ruolo, `client.Accedi(email)`
fa il login, `client.InviaForm(pagina, azione, campi)` legge il token antiforgery dalla pagina e invia il form. Gli errori
del server finiscono in `factory.ErroriServer`, utile per capire un errore 500.

## Regole del progetto

- **Lingua**: codice, nomi di dominio, commenti e messaggi in italiano.
- **Build severa**: `TreatWarningsAsErrors` è attivo per tutti i progetti (`Directory.Build.props`): un avviso blocca la build.
- **Versioni dei pacchetti** centralizzate in `Directory.Packages.props`.
- **Regole di business nel dominio**: le entità validano sé stesse e lanciano `DomainException` con un messaggio per
  l'utente; i controller usano `Prova(...)` di `AppController` per mostrarlo nel form.
- **Commenti**: solo dove la logica non è ovvia, per spiegare il perché.
- **Grafica**: l'aspetto riprende ConsulensNew. I file in `wwwroot/assets` (CSS, font, plugin) sono copiati da lì
  **invariati**; la palette beige è solo in `assets/css/tema-beige.css`; gli stili delle pagine di questo progetto sono in
  `wwwroot/css/site.css`. Per le pagine nuove usare i componenti esistenti (`titolo-pagina`, card `radius-15`,
  `customer-directory`, `invoice-filter-bar`, `customer-actions`).
- **Librerie client** in `wwwroot/lib`: jQuery, Bootstrap (bundle), ApexCharts. Plugin in `wwwroot/assets/plugins`:
  Select2, SimpleBar, MetisMenu.
- **Documenti caricati** solo nell'archivio (`Archivio:Percorso`), mai in `wwwroot` e mai nel repository.
