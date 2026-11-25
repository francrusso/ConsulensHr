# Funzionalità

Cosa fa Consulens HR, sezione per sezione, e chi può fare cosa.
Per come è fatto il codice vedi [architettura.md](architettura.md).

Consulens HR è il portale di uno studio di consulenza del lavoro e delle aziende sue clienti: anagrafica e contratti del
personale, ferie e permessi con i saldi in ore, presenze mensili da passare al consulente, distribuzione di buste paga,
CU e contratti. **Non calcola i cedolini**: i PDF arrivano dal gestionale paghe dello studio.

## Ruoli

| Ruolo | Chi è | Cosa fa |
|---|---|---|
| **Super amministratore** | Il titolare dello studio | Tutto ciò che fa un consulente, su tutte le aziende. In più crea i consulenti, assegna loro le aziende e gestisce gli accessi di tutti. |
| **Consulente** | Personale dello studio | Lavora solo sulle aziende che gli sono assegnate (e su quelle che crea). Crea aziende, gestisce personale e contratti, carica buste paga e CU, riceve e elabora le presenze. Non decide le richieste di ferie. |
| **Referente azienda** | Chi gestisce il portale per l'azienda | Solo la propria azienda: completa il profilo, gestisce personale, team e organigramma, approva le richieste, compila e invia le presenze del mese. |
| **Dipendente** | La persona | Solo sé stesso: completa i propri dati, chiede ferie e permessi, segna i giorni in ufficio, vede contratto, buste paga e CU. |

### Menù per ruolo

- **Dipendente**: I miei dati, Il mio contratto, Calendario, Richieste, Documenti (buste paga e CU).
- **Referente**: Panoramica, Profilo azienda, Personale (elenco, team, organigramma), Presenze e assenze, Documenti (anche Contratti).
- **Consulente**: Panoramica, Aziende, Personale, Presenze e assenze, Documenti, Utenti; il super amministratore anche Consulenti.
- Durante il **percorso guidato** (referente o dipendente al primo accesso) c'è solo la voce del profilo da completare.

Il pulsante in alto a destra cambia con la sezione ("Aggiungi persona", "Nuova azienda", "Nuova richiesta", "Carica busta paga"…).

## Accesso

- Login con email e password (almeno 10 caratteri, con maiuscole, minuscole e numeri). Dopo 5 tentativi falliti
  l'account si blocca per 15 minuti. "Ricorda accesso" mantiene la sessione anche chiudendo il browser.
- Nessuno si registra da solo: gli accessi nascono da un **invito**. L'invito crea l'utente senza password e manda un
  link per sceglierla, valido 3 giorni e utilizzabile una volta sola. Se l'email non è configurata, il link compare a chi
  invita, che lo comunica da sé.
- "Password dimenticata?" usa lo stesso link e risponde allo stesso modo che l'email esista o no.
- Un utente disattivato non entra più e le sue sessioni aperte decadono entro 5 minuti.

## Aziende

- **Elenco** (studio): ricerca per ragione sociale o partita IVA; il super amministratore filtra per consulente e vede
  la colonna "Consulente".
- **Nuova azienda**: la partita IVA viene verificata sul registro europeo VIES, che precompila ragione sociale e sede.
- **Scheda**: dati, personale, stato del profilo e referenti (con invito).

### Profilo azienda (percorso guidato)

Il referente lo compila al primo accesso; il consulente può farlo per conto dell'azienda. Ogni passo si salva da sé e si
può tornare solo ai passi già fatti.

1. **Dati societari**: ragione sociale, forma giuridica, codice fiscale, REA, ATECO, inizio attività, PEC, email, telefono.
2. **Sedi**: sede legale obbligatoria, sede operativa facoltativa (validate sull'elenco ISTAT dei comuni).
3. **Legale rappresentante**.
4. **Contratto e posizioni**: CCNL, matricola INPS, codice ditta e PAT INAIL, Cassa Edile, ente bilaterale, fondo
   pensione, orario; oppure "nessun contratto attivo".
5. **Paghe**: IBAN di addebito, giorno di pagamento, note.
6. **Riepilogo e conferma privacy** (art. 28 GDPR): il profilo diventa completo e i consulenti dell'azienda ricevono un'email.

A profilo completo i passi diventano schede modificabili, più **Tipi di richiesta**: l'azienda sceglie quali richieste
offrire ai dipendenti (ferie e malattia ci sono sempre; smart working, presenza in ufficio, straordinari e le altre
assenze si possono disattivare).

## Personale

### Elenco (`/Dipendenti`)

- Due schede: **Personale dipendente** e **Professionisti esterni**, in base al tipo del contratto più recente.
- Stati: Attivo (contratto in corso), Firmato (inizia in futuro), Scaduto (finito senza cessazione), Cessato, Tutti.
- Ricerca per nome, cognome e codice fiscale; lo studio filtra anche per azienda.
- Ogni riga: team, retribuzione annua e mensile del contratto di riferimento, PDF del contratto ("Apri" o "Da caricare"),
  scadenza e stato.

### Assunzione

"Aggiungi persona" apre la scheda vuota; dopo il salvataggio si passa ad **Assumi** per il contratto o l'incarico.
In alternativa: Documenti › Nuovo contratto, scegliendo una persona esistente o creandone una.

Regole: CCNL e livello obbligatori solo per il lavoro dipendente; i professionisti esterni non maturano ferie né ROL;
almeno 16 anni alla data di inizio; niente contratti sovrapposti; il codice fiscale è unico nell'azienda.

### Scheda della persona

Una sola pagina per vedere e modificare, con le sezioni:

- **Anagrafica**: dati personali, residenza, IBAN, foto, genere e pronomi (da cui nasce l'avatar segnaposto), sede di
  lavoro, team, posizione, responsabile diretto, team di cui è responsabile, referente paghe (uno per azienda), accesso
  al portale e i **dati per le paghe** compilati dal dipendente.
- **Contratto**: correzione (stessi controlli dell'assunzione), cessazione, PDF firmato (massimo 10 MB), contratti precedenti.
- **Assenze e presenze**: saldi di ferie e ROL con il residuo dell'anno precedente (modificabile da studio e referente),
  giorni in ufficio, da remoto e di assenza mese per mese, elenco delle richieste.
- **Buste paga** della persona.

### Contratto: modalità di lavoro

Ogni contratto indica la modalità: **in presenza**, **ibrido** (con i giorni in ufficio a settimana) o **full remote**.
A chi lavora in presenza si possono concedere giorni di smart working a settimana o al mese.

## Area del dipendente

- **Marchio**: in alto a sinistra, al posto di Consulens, c'è il logo dell'azienda del dipendente (senza logo, il suo
  nome), così il portale sembra quello interno dell'azienda.
- **I miei dati**: percorso in 6 passi (anagrafica, residenza, stipendio e TFR, detrazioni e familiari a carico, altri
  dati, riepilogo con dichiarazione). Finché non è completo il portale mostra solo questo. IBAN, residenza e cellulare
  vanno anche nell'anagrafica usata dallo studio. I consulenti e i referenti dell'azienda ricevono un'email al completamento
  e a ogni modifica.
- **Il mio contratto**: in sola lettura, con sede, team, responsabile (o chi approva le sue richieste) e contratti precedenti.
- **Calendario** (`/Ufficio`): il mese con le proprie richieste; da qui si segnano i giorni in ufficio o si chiede lo smart working.

## Team e organigramma

- Team con nome, team superiore, responsabile, colore ed emoji. I sotto-team ereditano il colore, più chiaro a ogni
  livello. Un team con sotto-team o con membri non si elimina.
- **Organigramma**: albero azienda → team → persone, con zoom.
- **Chi approva ferie e permessi**: il responsabile diretto; altrimenti il responsabile del team, salendo ai team
  superiori se è la persona stessa; altrimenti il referente dell'azienda.

## Richieste: ferie, permessi e malattia

### Nuova richiesta

Un modulo unico, con i gruppi **Presenze** (smart working, presenza in ufficio, straordinario) e **Assenze** (ferie,
ROL, malattia, L. 104, congedi, lutto, permesso non retribuito, altro). Compaiono solo i tipi abilitati dall'azienda.

- Le ore si calcolano sui giorni lavorativi del contratto, escluse domeniche, festività nazionali e santo patrono
  (il sabato è lavorativo solo con la settimana di 6 giorni). Un permesso a ore vale un solo giorno.
- Servono un contratto attivo per tutto il periodo e nessuna sovrapposizione (ammessi più permessi a ore nello stesso
  giorno entro l'orario). Una richiesta a cavallo d'anno va divisa in due.
- **Malattia**: protocollo del certificato obbligatorio, approvata subito.
- **Ferie e permessi**: restano in attesa. Il dipendente non può superare il saldo disponibile; l'azienda sì. Il referente
  può approvarli subito mentre li registra.

### Decisioni e cancellazioni

- Ferie e permessi li approva il **referente** (per tutta l'azienda) o il **responsabile** indicato dall'organigramma.
  Lo studio non decide. Un rifiuto richiede un motivo, visibile al dipendente.
- **Annulla**: una richiesta in attesa, o approvata e non ancora iniziata.
- **Chiedi cancellazione**: su una richiesta già approvata (anche passata) il dipendente lo chiede con un motivo; il
  referente o il responsabile conferma o respinge.
- **Revoca**: il referente può annullare qualunque richiesta attiva, anche approvata e passata (es. registrata per errore).

### Saldi di ferie e ROL

- Ogni mese con almeno 15 giorni di rapporto matura 1/12 della spettanza annua del contratto (riproporzionata al part-time).
- **Residuo** = residuo dell'anno precedente + maturato − approvate. **Disponibile** = residuo − richieste in attesa.

### Elenco richieste (`/Assenze`)

Un elenco unico di assenze, smart working e straordinari dell'anno scelto, più quelle in attesa di qualunque anno; in
cima ciò che è da decidere. Filtri per stato e anno, ricerca per nome; lo studio filtra anche per azienda. Il dipendente
vede in cima i propri saldi e, se è responsabile di qualcuno, le richieste del suo team da approvare.

### Calendario aziendale (`/Assenze/Calendario`)

Per studio e referente: il mese a settimane intere con presenti (col colore del team), in ufficio, in smart working,
assenti per categoria (approvati e in attesa), chiusure e festività. Filtri per azienda, team (con i sotto-team) e luogo.

### Chiusure aziendali

Si creano dal calendario (es. ferie estive, ponte di Natale): registrano ferie già approvate per chi ha un contratto
nel periodo, saltando chi è già assente. Eliminando la chiusura si tolgono anche quelle ferie. Una chiusura sta in un
solo anno e dura al massimo due mesi.

## Smart working, giorni in ufficio e straordinari

- **Chi lavora da remoto** (contratto ibrido o full remote, o sede "Smart working") segna i giorni in cui va in ufficio,
  senza approvazione, dal primo del mese in corso.
- **Chi lavora in presenza** chiede lo smart working da oggi in poi, entro i giorni concessi dal contratto; lo approva
  il referente.
- **Straordinari**: dal mese scorso fino a 31 giorni da oggi, da 0,5 a 12 ore a mezz'ore, in un giorno coperto da
  contratto. Li approva il referente; una volta approvati compaiono già nelle presenze del mese, separati in feriali e festivi.

## Presenze del mese (`/Presenze`)

- Il **referente** compila il foglio della sua azienda: per persona giorni lavorati, in ufficio e da remoto, ore di
  assenza prese dal portale e variazioni (straordinari, buoni pasto, premi, rimborsi, anticipi). Buoni pasto e
  straordinari approvati sono già proposti. Si salva in bozza o si **invia**: le assenze vengono fotografate e il foglio
  non è più modificabile.
- Lo **studio** ha il cruscotto delle aziende attive: sollecita, esporta in CSV (una o tutte), segna il mese come
  elaborato, riapre per correzioni.
- Nei primi 10 giorni del mese si propone il mese precedente, poi quello in corso.

## Documenti

### Buste paga (`/BustePaga`)

- **Caricamento in blocco** (studio o referente): ogni PDF viene abbinato alla persona dal nome del file, per codice
  fiscale o per matricola (gli zeri iniziali non contano). Massimo 10 MB a file e 200 MB in tutto; i doppioni (stessa
  impronta SHA-256) vengono scartati. Si pubblica subito o si lascia in bozza.
- Lo studio può caricare un singolo cedolino dalla scheda, pubblicare, ritirare o eliminare (solo le bozze).
- Alla pubblicazione la persona riceve un'email (senza allegato). La prima apertura vale come presa visione.
- Il dipendente vede solo i propri cedolini pubblicati.

### CU (`/Documenti/Cu`)

Una riga per persona per anno d'imposta (di norma l'anno precedente). Studio e referente caricano, sostituiscono (una
CU per persona e anno) ed eliminano; la sostituzione azzera la presa visione. Il dipendente vede tutte le sue CU.

### Contratti (`/Documenti/Contratti`)

Tutti i contratti con stato e PDF, oppure "Da caricare". Il dipendente viene portato a "Il mio contratto".

Tutti i file stanno nell'archivio privato e si aprono solo passando dal portale, che controlla chi li chiede.

## Panoramica (home)

Per studio e referente: cose da fare, indicatori, chi c'è oggi (in ufficio, da remoto, assenti), richieste in attesa
(il referente le approva da lì), grafici delle assenze degli ultimi 12 mesi e delle ferie per persona, contratti in
scadenza entro 60 giorni, scadenze dei prossimi due mesi (contratti, chiusure, consegna CU del 16 marzo).
Il dipendente viene portato al suo calendario.

## Utenti e consulenti

- **Utenti** (studio): un elenco degli accessi con filtri per ruolo e testo. Azioni: reinvio del link, attiva/disattiva
  (non su sé stessi), cambio email.
- **Aggiungi altro utente**: referente di un'azienda, accesso per una persona del personale (anche come referente),
  consulente (solo super amministratore).
- **Consulenti** (super amministratore): crea i consulenti e assegna le aziende con le caselle di spunta.

## Notifiche

Ogni evento importante genera una **notifica in piattaforma** per chi è coinvolto: la campanella in alto mostra le
ultime e quante sono da leggere, la pagina **Notifiche** (`/Notifiche`) le elenca tutte con ricerca, filtro per sezione,
"Segna come letta" / "Segna tutte come lette" e "Vai alla sezione", che apre la pagina dell'evento e la segna come letta.
Chi compie un'azione non riceve la notifica della propria azione.

Le notifiche in piattaforma sono **sempre attive**. In **Impostazioni notifiche** (`/Notifiche/Impostazioni`, anche dalla
campanella e dal profilo) ognuno sceglie per quali tipologie ricevere **anche l'email**: interruttore per tipologia,
"Disattiva tutte" per sezione e interruttore generale del canale Email; ogni scelta si salva subito. Senza scelta l'email è
attiva. Compaiono solo le tipologie che il ruolo può ricevere. Il canale **WhatsApp** è previsto ma non ancora disponibile.

"I consulenti dell'azienda" sono quelli a cui l'azienda è assegnata; se non è assegnata a nessuno, la notifica va ai
super amministratori. Le email partono in background; senza server SMTP configurato arrivano solo le notifiche in
piattaforma (il consulente lo vede scritto nelle impostazioni). L'email arriva solo a chi ha già attivato l'accesso.

| Sezione | Tipologia | Chi la riceve |
|---|---|---|
| Ferie e permessi | Nuova richiesta da approvare | Il responsabile (se ha un accesso attivo), altrimenti i referenti |
| | Richiesta approvata o rifiutata | La persona (con il motivo del rifiuto) |
| | Cancellazione da decidere | Il responsabile, altrimenti i referenti |
| | Cancellazione confermata o respinta | La persona |
| | Richiesta revocata (anche smart working e straordinari) | La persona |
| | Chiusura aziendale | Le persone a cui sono state registrate le ferie |
| Smart working e straordinari | Smart working / straordinario da approvare | I referenti |
| | Smart working / straordinario approvato o rifiutato | La persona |
| Turni di lavoro | Turni pubblicati | Ogni persona, con il periodo dei suoi turni |
| | Cambio turno da approvare | I referenti |
| | Cambio turno approvato o rifiutato | Il richiedente e, per uno scambio approvato, il collega |
| Presenze del mese | Presenze inviate | I consulenti dell'azienda |
| | Promemoria presenze, presenze da correggere, presenze elaborate | I referenti |
| Documenti | Nuova busta paga, nuova CU, contratto o lettera di variazione caricati | La persona |
| | Documento personale caricato | Caricato dalla persona: referenti e consulenti; caricato da loro: la persona |
| | Archivio storico | Caricato dallo studio: i referenti; caricato dal referente: i consulenti |
| Personale | Dati personali compilati o modificati, nuovo contratto o incarico, cessazione | Consulenti e referenti dell'azienda |
| Aziende | Profilo azienda completato | I consulenti dell'azienda |
| | Azienda assegnata | Il consulente a cui il super amministratore la assegna |
| Scadenze | Contratto a termine in scadenza (30 giorni, senza contratto successivo né cessazione) | Consulenti e referenti |
| | Documento personale in scadenza (30 giorni) | La persona, i consulenti e i referenti |

I promemoria delle scadenze si controllano poco dopo l'avvio e poi ogni 6 ore; ognuno arriva una volta sola (se la data
cambia, per esempio con una proroga, arriva un nuovo promemoria).

Invito, recupero password e cambio dell'email di accesso arrivano sempre per email e non si possono spegnere.

## Indirizzi e partita IVA

- I campi di indirizzo suggeriscono comuni dall'elenco ISTAT (circa 8.000) e vie dal servizio Photon di OpenStreetMap
  (solo Italia), compilando CAP e provincia.
- La partita IVA di una nuova azienda si verifica sul registro VIES della Commissione europea.

## Turni di lavoro

Il calendario mostra dipendenti, foto e badge del team sulle righe e i giorni sulle colonne. Sono disponibili viste giorno, settimana e mese e filtri per persona, team e sede abituale. La navigazione e i colori riprendono il calendario presenze. Le assenze approvate o in attesa compaiono nelle celle; le assenze approvate di giornata intera sono escluse dal conteggio delle ore pianificate. Non vengono calcolati cedolini, costi retributivi o maggiorazioni.

### Turni salvati e cicli giornalieri

La libreria per azienda contiene turni con nome, giorni della settimana e orari, oppure cicli giornalieri ordinati (es. Mattina 06–14 → Pomeriggio 14–22 → Notte 22–06 → Smonto → Riposo). I notturni terminano il giorno successivo; smonto e riposo non conteggiano ore lavorate. Il ciclo parte dalla prima fase alla data iniziale e avanza ogni giorno di calendario, ripetendosi; nei giorni della settimana esclusi non genera assegnazioni. Si può limitare un modello a uno o più team con una multiselezione facoltativa; senza selezione è disponibile per tutti. La modifica del modello non modifica retroattivamente le assegnazioni.

Da **Nuovo turno** si selezionano uno o più dipendenti, un periodo e un modello salvato; in alternativa si indicano giorni e orari personalizzati. L'inserimento è unico e atomico: sovrapposizioni, team incompatibili, assenze approvate di giornata intera o giornate fuori dal rapporto di lavoro bloccano tutto l'invio. La durata massima di un turno lavorato è 24 ore, il periodo massimo è un anno. Lo smonto può seguire il notturno che termina al mattino.

### Pubblicazione

Le assegnazioni nascono in **bozza** e sono visibili solo al referente e al consulente nel rispettivo perimetro. **Pubblica i turni** rende visibili ai dipendenti i turni del periodo e dei filtri mostrati. Anche il calendario personale include solo turni pubblicati. Una modifica gestionale riporta il turno in bozza fino alla nuova pubblicazione. Prima della pubblicazione vengono ricontrollate le assenze approvate.

### Richiesta di modifica e scambio

Il dipendente sceglie **Modifica / scambio turno** dal modulo **Nuova richiesta**, oppure clicca un turno pubblicato nel proprio calendario turni. Può proporre un nuovo orario o selezionare un collega della stessa azienda e uno dei suoi turni pubblicati. Per lo scambio deve confermare l'accordo già raggiunto con il collega; questa dichiarazione non equivale a una conferma digitale del collega.

La richiesta compare nel normale elenco **Richieste**, con gli stessi filtri per anno e stato. Finché è in attesa, i turni restano invariati e il richiedente può ritirarla. Decide il referente azienda: l'approvazione verifica proprietà, versioni dei turni, pubblicazione, date future, contratto, vincoli di team, assenze approvate e sovrapposizioni. Lo scambio aggiorna entrambe le assegnazioni nello stesso salvataggio; una modifica approvata è subito visibile. Se i turni sono stati modificati nel frattempo, lo scambio non viene applicato e serve una nuova richiesta. Il consulente vede le richieste nel proprio perimetro ma non le approva.
