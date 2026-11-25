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
| `WhatsApp:PhoneNumberId`, `WhatsApp:Token` | WhatsApp Cloud API di Meta per le notifiche (vedi sotto). Senza, il canale WhatsApp non è disponibile. Solo negli user-secrets o nelle variabili d'ambiente. |
| `WhatsApp:NumeroMittente` | Numero Consulens da cui arrivano i messaggi, mostrato nel portale (in `appsettings.json`: +39 349 584 4020). |
| `WhatsApp:TemplateNotifica` (`notifica_consulens_hr`), `TemplateCodice` (`codice_verifica_consulens_hr`), `Lingua` (`it`), `VersioneApi` (`v23.0`) | Nomi dei template approvati e versione della Graph API, se diversi dai predefiniti. |
| `Archivio:Tipo` | Dove stanno i documenti caricati: `Disco` (predefinito, nella cartella di `Archivio:Percorso`) o `Database` (tabella `FileArchivio`, per gli hosting con il disco che si azzera, come l'app di prova online). |
| `Archivio:Percorso` | Cartella dei documenti caricati (buste paga, CU, contratti, foto, loghi) con `Archivio:Tipo` = `Disco`. Predefinita: `src/CL.Web/App_Data/archivio`, fuori da `wwwroot`. |
| `Database:ApplicaMigrazioni` | `true` = applica le migrazioni all'avvio anche fuori da Development (lo usa l'app di prova online). |

### WhatsApp (notifiche)

Le notifiche WhatsApp partono dal numero Consulens registrato sulla piattaforma WhatsApp Business di Meta, con la
WhatsApp Cloud API. Il numero usato nell'app WhatsApp Business del telefono non può inviarle in automatico.

1. In WhatsApp Manager › **API Setup** copia il **Phone Number ID** del numero Consulens (non è il numero di telefono) e
   crea un token permanente da un utente di sistema del Business Manager con il permesso `whatsapp_business_messaging`.
2. Salvali negli user-secrets (in produzione nelle variabili d'ambiente `WhatsApp__PhoneNumberId` e `WhatsApp__Token`):

```bash
dotnet user-secrets set "WhatsApp:PhoneNumberId" "INCOLLA-QUI-IL-PHONE-NUMBER-ID" --project src/CL.Web
```

```bash
dotnet user-secrets set "WhatsApp:Token" "INCOLLA-QUI-IL-TOKEN" --project src/CL.Web
```

3. In WhatsApp Manager › **Modelli di messaggio** crea e fai approvare i due template, in italiano (`it`):

   - **`notifica_consulens_hr`**, categoria **Utilità**. Corpo:

     ```
     Ciao {{1}}, c'è una novità per te su Consulens HR.

     *{{2}}*
     {{3}}

     Puoi scegliere quali notifiche ricevere su WhatsApp dalle impostazioni del portale.
     ```

     Esempi per l'approvazione: `{{1}}` Mario, `{{2}}` Richiesta di ferie da approvare: Anna Bianchi, `{{3}}` Anna Bianchi
     ha chiesto ferie dal 09/12/2026 al 10/12/2026 (16 ore). Pulsante **Visita sito web**, testo "Apri nel portale",
     URL **dinamico** `https://<App:UrlPubblico>/Notifiche/Apri/{{1}}` (il portale passa l'id della notifica).
   - **`codice_verifica_consulens_hr`**, categoria **Autenticazione**, con il pulsante **Copia codice** e l'avviso di
     sicurezza. Il codice vale pochi minuti: se si indica la scadenza, non oltre 5 minuti.

Ogni messaggio inviato da un'azienda su WhatsApp ha un costo (tariffe Meta per i template di utilità e di autenticazione):
per questo nel portale WhatsApp è spento per tutte le tipologie finché la persona non le accende. Se l'invio non riesce,
il log riporta l'errore di Meta (template non approvato, numero senza WhatsApp…).

### Supabase

Le tabelle stanno nello schema `hr`, non in `public`: lo schema `public` di Supabase è esposto dalla sua API REST.
L'autenticazione è ASP.NET Identity (tabelle nello stesso schema), non Supabase Auth.

Usare la connessione diretta o il **session pooler** (porta 5432). Il transaction pooler (porta 6543) non supporta i
prepared statement di Npgsql. Il codice aggiunge da sé alla stringa di connessione i parametri che evitano le connessioni
chiuse dal pooler dopo una pausa (`DependencyInjection.ConnessioneRobusta`).

## Database e migrazioni

- In **Development** le migrazioni si applicano all'avvio, e così nell'app di prova online (`Database:ApplicaMigrazioni`).
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

## App di prova online (Render, gratis)

Per far provare l'app ai colleghi c'è un'app online sul piano gratuito di [Render](https://render.com), costruita dal
`Dockerfile` e descritta in `render.yaml`. Usa **lo stesso database Supabase dello sviluppo**: tester e sviluppo vedono
gli stessi dati. Vercel non va bene: non esegue .NET se non come container in beta, senza disco né processi in background.

### Metterla online (una volta sola)

1. Fai il push di `main` su GitHub.
2. Su [dashboard.render.com](https://dashboard.render.com) accedi con GitHub, poi **New › Blueprint** e scegli il
   repository `ConsulensHr`: Render legge `render.yaml` e propone il servizio `consulens-hr` (piano Free, Francoforte).
3. Alla voce `ConnectionStrings__Default` incolla la stessa stringa di connessione degli user-secrets (session pooler di
   Supabase, porta 5432). Per rileggerla (mostra anche le password, non condividere l'output):

```bash
dotnet user-secrets list --project src/CL.Web
```

4. **Deploy Blueprint**. La prima build richiede qualche minuto; poi l'app risponde su `https://consulens-hr.onrender.com`
   (o l'indirizzo che Render mostra in alto nella pagina del servizio).

Da lì in poi ogni push su `main` ripubblica l'app da sola. Le migrazioni nuove si applicano all'avvio.

### Cosa cambia rispetto allo sviluppo

- **Si addormenta** dopo 15 minuti senza visite: il primo accesso dopo una pausa impiega circa un minuto. Chi era collegato
  resta collegato e i link di invito restano validi (le chiavi che li firmano stanno nel database, tabella `DataProtectionKeys`).
- **Niente email**: il piano gratuito di Render blocca le porte SMTP. Quando inviti un collega il link d'accesso compare a
  schermo e glielo mandi tu; "Password dimenticata?" online non manda niente: per chi l'ha persa usa in **Utenti** «Invia un link per reimpostare
  la password», che online mostra il link da copiare.
  Non aggiungere le variabili `Email__*` su Render: l'invio andrebbe in timeout.
- **Documenti nel database** (`Archivio__Tipo` = `Database`): il disco di Render si azzera a ogni riavvio. I file caricati
  in locale su disco online non si aprono, e viceversa. Per vedere in locale anche quelli caricati online:

```bash
dotnet user-secrets set "Archivio:Tipo" "Database" --project src/CL.Web
```

- **Stesso database**: una migrazione creata in locale si applica a Supabase al primo avvio in sviluppo. Fai il push
  subito, così anche l'app online ha il codice che la conosce.

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
