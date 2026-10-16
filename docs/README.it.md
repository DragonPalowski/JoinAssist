# JoinAssist

Completamento gratuito di tabelle, colonne e JOIN basati sulle foreign key per **SSMS 22 x64**. L'interfaccia attuale è in italiano; la documentazione principale è in [inglese](../README.md).

## Installazione e utilizzo

1. Scarica il file `.vsix` dall'[ultima release](https://github.com/DragonPalowski/JoinAssist/releases/latest).
2. Salva le query e chiudi tutte le finestre di SSMS.
3. Apri il file e scegli SSMS 22 nell'installer Microsoft, quindi riavvia SSMS.
4. Connetti una scheda query al database e lascia attivo **Completamento automatico** nella barra sotto l'editor.

Scrivendo il nome di una tabella o `tabella.colonna`, scegli una proposta e premi **Tab**. Dopo `JOIN ` vengono proposte le tabelle collegate; dopo `ON ` le condizioni ricavate dalle foreign key. Usa le frecce per scegliere, Invio/Tab per inserire ed Esc per chiudere. Il pulsante **JOIN da foreign key…** apre la scelta manuale.

Le proposte privilegiano i nomi più vicini al testo scritto. Le JOIN evitano schema, parentesi quadre e alias automatici quando non sono necessari. CTE, sottoquery e viste permettono proposte basate su collegamenti diretti e risolvibili; il plugin non inventa relazioni.

## Novità della 0.6.0

- **Ricerca flessibile:** `itemdet` trova anche `OrderItemDetails` e `order_item_details_tb`. Le corrispondenze esatte e quelle che iniziano con il testo scritto restano davanti; seguono frammenti interni e abbreviazioni compatte. Vale anche per le colonne.
- **JOIN più chiare:** ogni riga mostra le colonne della condizione `ON` e il nome reale della foreign key. Puoi distinguere subito, per esempio, `CreatedByUserId` da `ModifiedByUserId` quando entrambi collegano la stessa tabella utenti.
- **Tabelle locali:** completa nomi e colonne di `CREATE TABLE #temp`, `DECLARE @tabella TABLE` e `SELECT ... INTO #temp`, leggendo le dichiarazioni precedenti nello script. Funzionano anche alias, proiezioni, CTE e sottoquery risolvibili.

Le query non vengono eseguite. Una `SELECT INTO` non trasferisce foreign key. Le variabili tabellari non attraversano `GO`; le `#temp` possono farlo nello stesso script. Dopo `DROP`, `ALTER`, modifiche condizionali o esecuzione dinamica, le definizioni non più affidabili vengono scartate. Sono escluse definizioni dentro procedure/blocchi di controllo, `##temp`, tipi tabellari definiti dall'utente e oggetti dichiarati soltanto in un'altra scheda. L'analisi descrive il testo dello script, non ciò che è già stato eseguito nella sessione.

È un progetto sperimentale della comunità: non tutte le forme di T-SQL e le versioni di SSMS sono coperte. Non invia query a servizi esterni e non memorizza credenziali.

## Aggiornamenti dalla 0.5.0

Clicca **Aggiornamenti…** nella barra sotto l'editor: vedrai la versione installata e le novità dell'ultima release pubblica su GitHub. Se è più recente, **Scarica aggiornamento** scarica il VSIX e verifica il checksum SHA-256. Non serve un account GitHub e il controllo avviene solo su richiesta.

Dopo il download premi **Apri cartella**, salva le query, chiudi tutte le finestre di SSMS e apri il VSIX. Il plugin non chiude SSMS né avvia installer automaticamente. I file verificati rimangono in `%LOCALAPPDATA%/JoinAssist/Updates` finché non li elimini.

Se usi la 0.4.1, installa manualmente la 0.5.0 una volta: il pulsante sarà disponibile da quella versione. Le anteprime sono escluse dal controllo e non vengono proposti downgrade.

[Limiti e guida completa](../README.md) · [Problemi e disinstallazione](TROUBLESHOOTING.md) · [Licenza MIT](../LICENSE)
