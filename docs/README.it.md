# JoinAssist

Completamento gratuito di tabelle, colonne e JOIN basati sulle foreign key per **SSMS 22 x64**. L'interfaccia attuale è in italiano; la documentazione principale è in [inglese](../README.md).

## Installazione e utilizzo

**Dalla 0.19.0:** le liste FK automatiche non si aprono più quando stai solo indentando una posizione JOIN/ON vuota. Inizia il nome della tabella o il riferimento; se segue subito un altro JOIN non viene proposto un nuovo collegamento automatico. Puoi sempre richiamare manualmente la lista con **Mostra suggerimenti** o **JOIN da foreign key…**. **Disinstalla JoinAssist…** apre la procedura Microsoft per rimuovere l'estensione dalla specifica installazione SSMS corrente, conservando regole e preferiti; annulla eventuali aggiornamenti in attesa prima di procedere. La guida integrata include ora 30 argomenti. [Dettagli disinstallazione](UNINSTALL.md).

**Dalla 0.18.0:** apri **JoinAssist → Guida…** per 29 argomenti con passaggi pratici, esempi copiabili e limiti. La ricerca copre tutto il testo e ignora maiuscole e accenti; **Ctrl+F** seleziona il campo. La guida funziona senza query, connessione o Internet. Nelle finestre del plugin **Guida / F1** apre la spiegazione della funzione corrente. I pulsanti **Apri…** portano allo strumento o alla pagina di impostazioni senza applicare modifiche; dalle finestre modali restano disabilitati finché non chiudi la modifica. **Copia esempio** copia soltanto SQL dimostrativo: nulla viene inserito o eseguito. F1 nell'editor SSMS resta invariato. [Dettagli](HELP.md).

1. Scarica il file `.vsix` dall'[ultima release](https://github.com/DragonPalowski/JoinAssist/releases/latest).
2. Salva le query e chiudi tutte le finestre di SSMS.
3. Apri il file e scegli SSMS 22 nell'installer Microsoft, quindi riavvia SSMS.
4. Connetti una scheda query al database e lascia attivo **JoinAssist → Completamento automatico**.

Scrivendo il nome di una tabella o `tabella.colonna`, scegli una proposta e premi **Tab**. Dopo `JOIN ` vengono proposte le tabelle collegate; dopo `ON ` le condizioni ricavate dalle foreign key. Usa le frecce per scegliere, Invio/Tab per inserire ed Esc per chiudere. Il pulsante **JOIN da foreign key…** apre la scelta manuale.

Le proposte privilegiano i nomi più vicini al testo scritto. Le JOIN evitano schema, parentesi quadre e alias automatici quando non sono necessari. CTE, sottoquery e viste permettono proposte basate su collegamenti diretti e risolvibili; il plugin non inventa relazioni.

## Novità della 0.17.0: azioni rapide

Apri **JoinAssist → Azioni rapide…**, cerca un'azione e prepara l'anteprima. **Inserisci anteprima** sostituisce la selezione o inserisce nel punto del cursore; la rinomina degli alias sostituisce invece lo script intero, dopo averlo mostrato. **Nuova scheda (salva .sql…)** salva esplicitamente un file e lo apre nell'editor. Controlla la connessione della nuova scheda prima di eseguire. Nessuna azione esegue la query generata.

Sono disponibili record collegati tramite FK (anche più query), appunti→IN, colonne multiple con ricerca, modelli con campi ripetuti, filtri temporali locale/UTC, percorsi JOIN e cardinalità, dipendenze e ricerca colonne, query per duplicati/NULL/conteggi/record senza corrispondenza, preferiti locali per database, schema affiancabile, rinomina alias, prova delle regole e recupero dei suggerimenti. La finestra può restare aperta accanto all'editor: è una finestra affiancabile, non una toolbar aggiunta alle query.

La scorciatoia **Ctrl+Alt+P** è facoltativa e disattivata inizialmente: si abilita in **Impostazioni → Completamento**. Le scorciatoie precedenti restano invariate. Le regole personali ora supportano anche `{{nome:valore}}` e `{{nome}}`: dopo l'inserimento Tab / Shift+Tab passa tra i campi, i nomi ripetuti vengono aggiornati insieme ed Esc termina la modalità. I campi sono testo SQL libero, non parametri eseguiti o valori automaticamente quotati.

Nelle scorciatoie personali abilita esplicitamente **Abilita campi collegati**: l'opzione parte disattivata per conservare il comportamento delle regole esistenti con parentesi graffe letterali.

Corrette le proposte personali obsolete continuando a scrivere, le alternative mancanti dopo Backspace e il riconoscimento delle scorciatoie a metà parola. **Riprova proposte** ricrea la sessione; la prova delle regole spiega anche disattivazione, ambito e alternative concorrenti. [Guida e limiti completi](WORKFLOWS.md).

## Regole personali dalla 0.16.0

Apri **JoinAssist → Impostazioni… → Regole**. **Nuova SELECT** aggiunge TOP e ordinamento per una tabella o vista; **Nuova scorciatoia** espande una parola in SQL personalizzato, con `$cursor$` per scegliere dove continuare a scrivere. Ogni regola ha un'anteprima, può valere per tutti i database o per un ambito server/database, ed è modificabile, duplicabile, disattivabile, importabile ed esportabile.

Per l'esempio dei log: schema `logs`, tabella `logs_tb`, TOP `100`, ordinamento **Prima colonna**, **DESC** attivo. Accettando la proposta con Tab ottieni `select TOP 100 * from logs.logs_tb ORDER BY 1 DESC`. Attiva **Applica anche con Spazio** se vuoi applicarla dopo il nome completo seguito da uno spazio: funziona solo con completamento automatico attivo e una sola regola applicabile. Le nuove parole chiave seguono **Formato SQL**.

Le regole SELECT conservano TOP, ORDER BY e WHERE già presenti e si limitano a SELECT * semplici, senza JOIN, CTE o UNION. Le scorciatoie funzionano all'inizio di un'istruzione, dopo `;` o `GO`. Tutte le modifiche restano in bozza fino a **Salva** nelle impostazioni. Nessuna regola è attiva inizialmente; nulla viene eseguito sul database. [Guida completa](PERSONAL-RULES.md).

## Novità della 0.14.0: mouse, parole chiave e filtri

Il clic seleziona la riga sotto il puntatore; Tab/Invio la inserisce e il doppio clic inserisce subito quella voce. È corretto il difetto che poteva inserire la prima proposta ignorando il mouse.

In **Impostazioni → Completamento** trovi due opzioni indipendenti, attive per impostazione predefinita: completamento delle **parole chiave SQL** e preparazione dei **filtri LIKE/IN**. Esempi: `sel` + Tab → `SELECT `; `colonna LIKE` + Tab → `colonna LIKE '%%'` solo se la colonna è testuale. Il cursore va tra i due `%`. Per `IN` e `NOT IN`, vengono preparati `('', '')` sul testo oppure `( ,)` sugli altri tipi scalari supportati, con il cursore nel primo valore. Funzionano anche `NOT LIKE`, gli alias e le colonne risolvibili di CTE/tabelle locali. I valori già presenti non vengono sovrascritti e i tipi sconosciuti/ambigui non vengono indovinati. [Funzionamento e limiti](TYPING-TEMPLATES.md).

## Novità della 0.13.0: ricerca di tabelle e colonne

- **Errori di battitura e nomi incompleti:** `patietns` trova `patients_tb`; `prescir` può trovare `prescriptions_tb`. I nomi corretti e i prefissi diretti mantengono la precedenza.
- **Parole e vicinanza:** riconosce camelCase/snake_case e abbreviazioni come `presc_det`; nel confronto dei prefissi non conta la lunghezza di `_tb`/`_vw`.
- **Contesto e alias:** cerca solo le sorgenti visibili nelle espressioni e favorisce le FK reali nei JOIN. Con `FROM Orders p`, `Ord` può proporre `p`, mostrando la tabella originale. Le colonne omonime mostrano la provenienza e vengono qualificate quando necessario.
- **Proposte leggibili e stabili:** lettere corrispondenti evidenziate, etichetta per le possibili correzioni, scelta esplicita mantenuta mentre resta compatibile con il testo. Tab accetta la proposta selezionata; nessuna correzione viene applicata da sola.
- **Ricerca configurabile:** in **Impostazioni → Ricerca**, scegli **Precisa**, **Equilibrata** (predefinita) o **Tollerante**. La modalità Equilibrata suggerisce correzioni da quattro lettere quando mancano nomi/prefissi diretti; la Tollerante le mostra anche insieme ai risultati diretti e da tre lettere.
- **Memoria locale delle scelte:** ricorda le proposte accettate, separatamente per server/database e prefisso, solo per ordinare risultati di qualità simile. Puoi disabilitarla o azzerarla. Salva hash, conteggi e date in `choices.json`, senza query, nomi in chiaro o credenziali. Il salvataggio è in background e non è un registro esatto degli utilizzi.

Sono coperti tutti e dieci gli aspetti di ricerca, ordinamento e presentazione; [funzionamento e limiti dettagliati](SMART-COMPLETION.md). Schemi e qualificatori restano letterali: gli errori vengono tollerati nel nome attualmente in corso di completamento.

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
- **FK comuni dalla 0.15.0:** se due tabelle puntano alla stessa chiave completa di una terza, propone anche «1 JOIN · FK comuni» tra le colonne figlie effettive, comprese chiavi composte e CTE con provenienza diretta. Le FK dirette restano davanti, poi i collegamenti ricavati e i percorsi con due JOIN, a parità di corrispondenza del nome. L'affinità del nome della tabella padre serve solo a ordinare alternative già supportate dai vincoli. La stessa opzione dei percorsi controlla entrambe le funzioni.
- **Dettagli colonne:** tipo e dimensione, precisione/scala, NULL/NOT NULL, PK/FK, identity e colonne calcolate, quando i metadati li rendono disponibili.

Le impostazioni sono in `%LOCALAPPDATA%/JoinAssist/preferences.json`; query, credenziali e metadati non vengono salvati. Il formato predefinito resta senza schema, parentesi e alias superflui. I percorsi sono limitati a due JOIN e la ricerca è limitata per evitare espansioni eccessive. Le forme DML complesse (per esempio TOP o target CTE) possono restare irrisolte; i dettagli non certificano che una vista sia modificabile.

Un collegamento da FK comuni **non è una FK diretta e non garantisce gli stessi risultati del percorso con due JOIN**: entrambe le tabelle possono avere più righe per chiave. Vincoli non verificati, righe orfane, filtri/visibilità sul padre e tipo di JOIN possono cambiare il risultato. I percorsi originali restano disponibili; nessun JOIN già scritto viene eliminato. L'anteprima mostra padre comune, condizione e vincoli di supporto. Il completamento dopo ON resta basato sulle FK dirette.

## Ricerca e filtri nella 0.15.2

Scrivendo `pre_vw`, il motore confronta il prefisso `pre` con i nomi che terminano in `_vw`, dando priorità a `prescriptions_vw` rispetto alle abbreviazioni sparse. Dopo una colonna testuale, `li`/`lik` propone direttamente `LIKE '%%'`: basta un Tab e il cursore va tra i due percentuali. Sono supportati anche NOT LIKE e IN/NOT IN, in base al tipo. La riga mostra l'intero modello; le opzioni per parole chiave e modelli restano indipendenti. Quando il tipo non consente un modello, può restare il normale completamento della parola, senza valori inventati.

## Correzione OPENJSON nella 0.15.1

Le sorgenti `OPENJSON` in CTE e catene `CROSS APPLY`/`OUTER APPLY` non bloccano più i suggerimenti degli altri alias del FROM. Per l'alias JSON vengono proposte `key`, `value` e `type`, oppure soltanto le colonne dichiarate in `WITH`, con i tipi indicati. La forma viene letta dal testo SQL, senza eseguire la query o leggere i documenti JSON. Le colonne JSON non ereditano foreign key; i normali campi delle tabelle mantengono la propria provenienza.

## Novità della 0.6.0

- **Ricerca flessibile:** `itemdet` trova anche `OrderItemDetails` e `order_item_details_tb`. Le corrispondenze esatte e quelle che iniziano con il testo scritto restano davanti; seguono frammenti interni e abbreviazioni compatte. Vale anche per le colonne.
- **JOIN più chiare:** ogni riga mostra le colonne della condizione `ON` e il nome reale della foreign key. Puoi distinguere subito, per esempio, `CreatedByUserId` da `ModifiedByUserId` quando entrambi collegano la stessa tabella utenti.
- **Tabelle locali:** completa nomi e colonne di `CREATE TABLE #temp`, `DECLARE @tabella TABLE` e `SELECT ... INTO #temp`, leggendo le dichiarazioni precedenti nello script. Funzionano anche alias, proiezioni, CTE e sottoquery risolvibili.

Dalla 0.19.1, scrivendo `FROM #SSN` o `JOIN #SSN` i nomi locali vengono proposti direttamente dallo script, anche senza accesso al catalogo. Una SELECT incompleta precedente non nasconde più le dichiarazioni indipendenti nei batch semplici. Le colonne esplicite restano disponibili in caso di errore di connessione; per espandere `tabella.*` servono i metadati delle sorgenti. Nei JOIN delle temporanee viene completato il nome, senza inventare una condizione ON.

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
