# JoinAssist

Completamento gratuito di tabelle, colonne e JOIN basati sulle foreign key per **SSMS 22 x64**. L'interfaccia attuale è in italiano; la documentazione principale è in [inglese](../README.md).

## Installazione e utilizzo

1. Scarica il file `.vsix` dall'[ultima release](https://github.com/DragonPalowski/JoinAssist/releases/latest).
2. Salva le query e chiudi tutte le finestre di SSMS.
3. Apri il file e scegli SSMS 22 nell'installer Microsoft, quindi riavvia SSMS.
4. Connetti una scheda query al database e lascia attivo **JoinAssist → Completamento automatico**.

Scrivendo il nome di una tabella o `tabella.colonna`, scegli una proposta e premi **Tab**. Dopo `JOIN ` vengono proposte le tabelle collegate; dopo `ON ` le condizioni ricavate dalle foreign key. Usa le frecce per scegliere, Invio/Tab per inserire ed Esc per chiudere. Il pulsante **JOIN da foreign key…** apre la scelta manuale.

Le proposte privilegiano i nomi più vicini al testo scritto. Le JOIN evitano schema, parentesi quadre e alias automatici quando non sono necessari. CTE, sottoquery e viste permettono proposte basate su collegamenti diretti e risolvibili; il plugin non inventa relazioni.

## Novità della 0.12.0: DELETE con dipendenze

Scrivi una DELETE completa con WHERE, posiziona il cursore al suo interno e scegli **JoinAssist → Prepara DELETE con dipendenze…**. Controlla le schede **Dipendenze e ordine** e **Script SQL**; **Inserisci script di prova** sostituisce solo la DELETE corrente. Il plugin legge solo metadati: non esegue cancellazioni e non calcola conteggi sui dati.

Lo script fissa prima gli insiemi di righe, segue le FK su più livelli e ordina le DELETE dai figli ai genitori, gestendo chiavi composte e percorsi multipli. Distingue CASCADE, SET NULL e SET DEFAULT. Usa una transazione con **ROLLBACK finale predefinito**: per una cancellazione definitiva bisogna rivedere lo script e modificare manualmente il punto indicato. I parametri del filtro devono essere disponibili quando lo esegui.

Servono **VIEW DEFINITION sul database** e chiavi primarie visibili. Questa prima versione blocca cicli, relazioni verso la stessa tabella, trigger DML attivi, policy di sicurezza, tabelle temporali/history/memory-optimized e metadati non risolvibili; non gestisce DELETE TOP/OUTPUT/OPTION/CTE/JOIN o senza WHERE. I motivi compaiono nell'anteprima e non viene proposto uno script parziale. Il codice generato imposta `ROWCOUNT 0` nella sessione e richiede l'assenza di una transazione già aperta o implicita. [Funzionamento e limiti dettagliati](DELETE-PLANNING.md).

## Novità della 0.11.0: prepara INSERT

Scrivi `INSERT INTO NomeTabella ` (con uno spazio finale), scegli **Prepara INSERT** e premi **Tab**. Inserisce le colonne nell'ordine della tabella e una sezione `VALUES` con parametri `@nomeColonna`; il cursore va al primo valore. I parametri sono segnaposto da sostituire o dichiarare, non valori letti dal database. Necessarie parentesi quadre, maiuscole/minuscole, rientro e fine riga vengono rispettati.

Se sei già in una posizione vuota dentro la lista `(`, **Inserisci colonne mancanti** aggiunge i nomi non ancora presenti, mantenendo parentesi e valori esistenti. Le singole colonne restano disponibili; iniziando a scrivere un nome si torna al normale completamento.

Le proposte multiple escludono identity, colonne calcolate, rowversion e colonne generated always. In **Impostazioni → Completamento** puoi togliere **Includi colonne con valore predefinito nella struttura INSERT**; inizialmente è attivo. La generazione richiede una tabella con proprietà note, anche temporanea o variabile dichiarata nello script: non viene offerta per viste, sinonimi, CTE o proprietà sconosciute. Non sostituisce INSERT SELECT/VALUES/OUTPUT già iniziati e non esegue mai la query.

## Correzione della 0.10.1: colonne nei filtri DELETE e UPDATE

Scrivendo, per esempio, `DELETE FROM Orders WHERE Cu`, vengono proposte le colonne della tabella filtrata. Funziona anche con schema, alias, sorgenti FROM/JOIN, CTE e tabelle locali riconoscibili. Le colonne identity e calcolate restano disponibili nei filtri. Il completamento modifica solo il nome in corso e non esegue mai la query.

## Novità della 0.10.0: aggiornamento alla chiusura

Apri **JoinAssist → Aggiornamenti…**. Quando esiste una release più recente, **Installa alla prossima chiusura** scarica e verifica il pacchetto e avvia un piccolo processo di attesa. Puoi chiudere la finestra Aggiornamenti e continuare a lavorare: aspetta che tutte le istanze SSMS siano chiuse, ricontrolla i file e apre l'installer Microsoft. Completa i normali passaggi dell'installer; il plugin non chiude SSMS, non conferma automaticamente l'installazione e non lo riapre.

**Annulla installazione in attesa** ferma l'operazione prima dell'avvio dell'installer. Lo stato è condiviso tra le finestre SSMS e viene riletto aprendo Aggiornamenti. L'attesa scade dopo 24 ore. Nessun servizio Windows, attività pianificata o avvio al login: dopo riavvio del PC, disconnessione o errore del processo occorre programmare di nuovo. Non vengono eseguiti controlli GitHub in background.

**Per passare dalla 0.9.0 alla 0.10.0 serve ancora il metodo abituale:** scarica il VSIX, salva le query, chiudi SSMS e apri il file. Il nuovo pulsante sarà disponibile dalla 0.10.0 quando verrà pubblicato un aggiornamento successivo. Il download manuale resta utilizzabile.

## Novità della 0.9.0

- **Proposte nel contesto:** a parità di tipo di corrispondenza, privilegia le colonne dell'ambito locale e, negli ON, le sorgenti coinvolte, le chiavi note e i tipi compatibili con il confronto. Il nome esatto resta primo; l'ordinamento di tabelle e viste non cambia.
- **ON già iniziato:** per esempio `JOIN Customers c ON o.Cust` può completare la condizione reale `o.CustomerId = c.Id`, mantenendo alias, lato iniziato, chiavi composte e clausole successive. Restano disponibili le normali colonne; condizioni AND/OR già scritte non vengono riscritte.
- **Espandi colonne (*)…:** posiziona il cursore sull'asterisco di SELECT oppure su `alias.*`, scegli le colonne e controlla l'anteprima. Inserisce i riferimenti espliciti nell'ordine della sorgente, mantenendo gli alias esistenti; Ctrl+Z annulla. Esclude COUNT(*), sorgenti sconosciute e nomi di output ambigui.
- **Informazioni tabella / colonna…:** con il cursore su tabella, alias o colonna qualificata, mostra tipi, proprietà e foreign key reali nelle schede **Colonne** e **Relazioni FK**. Nello spazio di un SELECT riconoscibile puoi consultare le sorgenti correnti. Non esegue la query e non legge i suoi risultati.
- **Tipi delle CTE:** descrive gli output di CAST/CONVERT e varianti TRY, CASE/COALESCE compatibili, letterali e alcune funzioni note. Coercizioni e tipi non sicuri restano non disponibili; le espressioni non acquisiscono foreign key.
- **Script grandi:** riusa i token precedenti alla modifica e i SELECT indipendenti separati da punto e virgola o batch GO. Per confini incerti, codice procedurale e QUOTED_IDENTIFIER mantiene l'analisi completa; non tutti gli script possono quindi beneficiare dello stesso riuso.
- **Menu visibile:** imposta una sola volta JoinAssist nella barra principale, se riconosce le impostazioni SSMS. Potrebbe servire il riavvio normale successivo. Non riavvia SSMS e rispetta gli spostamenti successivi dell'utente e gli altri menu.

## Interfaccia compatta dalla 0.8.0

Tutti i comandi sono nel menu **JoinAssist**; la barra orizzontale sopra i risultati è stata rimossa. Se il menu è sotto **Extensions → JoinAssist**, apri **Extensions → Customize Menu… → Extensions Menu**, togli la spunta a **JoinAssist** e usa **Save and Restart** per portarlo nella barra principale. La posizione esatta dipende da SSMS e dalle personalizzazioni.

Il menu raggruppa completamento/foreign key, metadati/impostazioni e aggiornamenti/diagnostica. I comandi relativi alle query operano sulla scheda SQL attiva e vengono disabilitati quando non c'è un editor compatibile. **Mostra suggerimenti** riporta anche la scorciatoia configurata.

I suggerimenti sono più compatti: nome in evidenza, dettagli della colonna o nome FK su una seconda riga, condizione SQL completa soltanto nell'anteprima del collegamento selezionato. Menu e finestre seguono il tema di SSMS. Le impostazioni sono divise in **Completamento**, **Formato SQL** e **Cache**.

Il cursore dentro stringhe o commenti non produce più messaggi diagnostici automatici. **JoinAssist → Diagnostica…** raccoglie lo stato corrente e permette di copiarne una versione senza query o dati di connessione. Il salvataggio delle impostazioni chiude correttamente anche la finestra aperta senza bloccare l'editor.

## Novità della 0.7.0

- **Query grandi:** analisi in background, richieste accorpate e riuso dei risultati di analisi. Dopo `alias.colonna` legge soltanto le colonne della sorgente indicata.
- **Cache metadati:** nomi, colonne e foreign key rimangono in memoria per 2 minuti, separati per connessione/sessione, server e database. **Aggiorna metadati** forza una nuova lettura alla prossima proposta.
- **Contesto più robusto:** recupera sottoquery correlate, APPLY e catene di CTE incomplete, anche nei rami UNION, rispettando gli alias e i confini tra le query.
- **Diagnostica:** la barra spiega più precisamente perché non ci sono proposte. **Copia diagnostica** copia solo versioni e codice del problema, senza query o dettagli della connessione.
- **Impostazioni salvate:** automatico, attesa, durata cache, accettazione con Invio, scorciatoia e formato SQL. **Ctrl+Alt+Spazio** richiama le proposte anche con automatico disattivato; puoi scegliere Ctrl+Shift+J o Ctrl+Alt+J. Tab resta sempre disponibile.
- **INSERT e UPDATE:** propone i nomi delle colonne nell'elenco INSERT e nelle assegnazioni SET, escludendo quelle già indicate e, quando note, identity, calcolate e rowversion. Supporta anche `UPDATE alias ... FROM ...` e tabelle locali. Non esegue DML e non genera valori.
- **Percorsi JOIN:** propone collegamenti attraverso una tabella intermedia, con etichetta «2 JOIN» e anteprima completa. Usa solo foreign key reali, incluse quelle composte; scarta percorsi con vincoli disabilitati o tabelle ripetute. Puoi disabilitarli dalle impostazioni.
- **Dettagli colonne:** tipo e dimensione, precisione/scala, NULL/NOT NULL, PK/FK, identity e colonne calcolate, quando i metadati li rendono disponibili.

Le impostazioni sono in `%LOCALAPPDATA%/JoinAssist/preferences.json`; query, credenziali e metadati non vengono salvati. Il formato predefinito resta senza schema, parentesi e alias superflui. I percorsi sono limitati a due JOIN e la ricerca è limitata per evitare espansioni eccessive. Le forme DML complesse (per esempio TOP o target CTE) possono restare irrisolte; i dettagli non certificano che una vista sia modificabile.

## Novità della 0.6.0

- **Ricerca flessibile:** `itemdet` trova anche `OrderItemDetails` e `order_item_details_tb`. Le corrispondenze esatte e quelle che iniziano con il testo scritto restano davanti; seguono frammenti interni e abbreviazioni compatte. Vale anche per le colonne.
- **JOIN più chiare:** ogni riga mostra le colonne della condizione `ON` e il nome reale della foreign key. Puoi distinguere subito, per esempio, `CreatedByUserId` da `ModifiedByUserId` quando entrambi collegano la stessa tabella utenti.
- **Tabelle locali:** completa nomi e colonne di `CREATE TABLE #temp`, `DECLARE @tabella TABLE` e `SELECT ... INTO #temp`, leggendo le dichiarazioni precedenti nello script. Funzionano anche alias, proiezioni, CTE e sottoquery risolvibili.

Le query non vengono eseguite. Una `SELECT INTO` non trasferisce foreign key. Le variabili tabellari non attraversano `GO`; le `#temp` possono farlo nello stesso script. Dopo `DROP`, `ALTER`, modifiche condizionali o esecuzione dinamica, le definizioni non più affidabili vengono scartate. Sono escluse definizioni dentro procedure/blocchi di controllo, `##temp`, tipi tabellari definiti dall'utente e oggetti dichiarati soltanto in un'altra scheda. L'analisi descrive il testo dello script, non ciò che è già stato eseguito nella sessione.

È un progetto sperimentale della comunità: non tutte le forme di T-SQL e le versioni di SSMS sono coperte. Non invia query a servizi esterni e non memorizza credenziali.

## Correzioni della 0.6.1

Il completamento delle colonne recupera il contesto del SELECT corrente anche se nello script ci sono altri SELECT lasciati con WHERE/JOIN incompleti o espressioni ancora a metà. Sono riconosciute anche posizioni dopo DISTINCT/TOP, nei CASE e dopo LIKE/BETWEEN e operatori di confronto.

Puoi aggiungere un JOIN o la condizione ON prima di WHERE, GROUP BY, HAVING o ORDER BY già presenti, anche se queste clausole sono incomplete. Il resto del testo e i commenti vengono mantenuti esattamente. Il recupero lavora su una copia per l'analisi e non esegue né riscrive la query. Le colonne di sorgenti non risolvibili restano escluse.

## Aggiornamenti dalla 0.5.0

Clicca **Aggiornamenti…** nel menu JoinAssist: vedrai la versione installata e le novità dell'ultima release pubblica su GitHub. Se è più recente, **Scarica aggiornamento** scarica il VSIX e verifica il checksum SHA-256. Non serve un account GitHub e il controllo avviene solo su richiesta.

Dopo il download premi **Apri cartella**, salva le query, chiudi tutte le finestre di SSMS e apri il VSIX. Il plugin non chiude SSMS. Il solo download non avvia l’installer; dalla 0.10.0 puoi richiederne esplicitamente l’avvio alla chiusura come descritto sopra. I file verificati rimangono in `%LOCALAPPDATA%/JoinAssist/Updates` finché non li elimini.

Se usi la 0.4.1, installa manualmente la 0.5.0 una volta: il pulsante sarà disponibile da quella versione. Le anteprime sono escluse dal controllo e non vengono proposti downgrade.

[Limiti e guida completa](../README.md) · [Problemi e disinstallazione](TROUBLESHOOTING.md) · [Licenza MIT](../LICENSE)
