# Troubleshooting

## Find the compact JoinAssist menu (0.8.0+)

From 0.9.0 the extension sets its main-menu default once when it recognizes the SSMS menu settings. It may require the next normal restart; later user choices are respected. No restart is forced. If SSMS uses a different settings format or preserves its already loaded menu, use the manual path below.

The horizontal strip above Results has been removed. Open **JoinAssist**, or **Extensions → JoinAssist** if SSMS groups extension menus there. To place it on the main menu bar, use **Extensions → Customize Menu… → Extensions Menu**, clear **JoinAssist**, then **Save and Restart**. Exact ordering is controlled by SSMS.

Install into **SSMS 22 x64**, close all SSMS windows and reopen. Enable **JoinAssist → Completamento automatico**. Query-specific commands are disabled unless the active view is a supported SQL editor. **Diagnostica…**, **Impostazioni…** and **Aggiornamenti…** remain available without a query connection. If an earlier version is installed, its strip remains until you install 0.8.0 and restart.

After `JOIN`, include a trailing space. The editor's own completion list can still appear without JoinAssist: its presence alone does not prove a third-party extension is installed.

Version 0.6.1 fixes missing columns in scripts with earlier unfinished SELECT/WHERE/JOIN statements and missing JOIN/ON candidates before existing filter/order clauses. Update before trying to comment out the remainder of a query. Recovery is conservative: invalid or unsupported source definitions may still expose no columns. For a remaining problem, provide a small synthetic script with `|` marking the caret, keeping the statement boundaries and unfinished clauses intact. Do not post production queries or identifying data.

## Partial ON and schema tools (0.9.0)

From 0.15.0, JOIN table positions can also offer **1 JOIN · FK comuni** when two enabled constraints reference exactly the same complete key on the same parent. Use **Impostazioni → Completamento → Proponi percorsi con 2 JOIN e collegamenti tramite FK comuni** to enable/disable both derived candidates and two-hop paths. These equalities are explicitly inferred, not direct FKs; similar column names alone are insufficient. They can multiply child rows and need not be equivalent to a path through the parent, especially with untrusted constraints, parent filters/visibility and outer JOINs. Original two-hop paths remain available. This feature does not extend ON/partial-ON completion or execute SQL.

Partial FK completion supports a simple identifier or equality after ON, such as `o.Cust` or `o.CustomerId = c.I`. It deliberately leaves an existing AND/OR condition alone and keeps ordinary column proposals. Relationships still require visible real constraints and unambiguous lineage; similar names alone do not create a JOIN.

For **Espandi colonne (*)…**, put the cursor directly on the SELECT star or its qualifier. COUNT(*), ambiguous duplicate output names, unknown sources and incomplete/unrecognized projections are refused. Every source of a bare star must be resolvable; expand a qualified star to inspect one source at a time. Pick at least one column. An edit, tab change, reconnection or database change while loading requires reopening the command.

**Informazioni tabella / colonna…** resolves the identifier under the cursor in the current scope. Use `alias.column` for an ambiguous column name. Views/CTEs/derived sources show only safe mapped FK relationships, and computed expressions have no inferred FK lineage. After DDL use **Aggiorna metadati**. Tool windows show metadata, not data rows.

Incremental parsing is conservative: independent semicolon-terminated SELECTs and GO batches are reusable; procedural/stateful or unseparated text can still need a full parse. No execution or query logging is added.

## Metadata cache, settings and diagnostics (0.7.0)

After a schema change, click **Aggiorna metadati**, return to the query and invoke completion again with **Ctrl+Alt+Space**. This invalidates the current session/database cache. Expiry is 2 minutes by default; change it in **Impostazioni…**. A reconnection or different database gets its own cache.

Settings are remembered in `%LOCALAPPDATA%/JoinAssist/preferences.json`. If a configured shortcut conflicts with another extension, choose another of the three supported shortcuts. Disable Enter acceptance if you want only Tab to commit suggestions. A settings load/save error is shown explicitly and does not rewrite existing queries.

Open **JoinAssist → Diagnostica…** to read the current status. **Copia diagnostica** copies only versions and the diagnostic code; paste it into your issue and add a synthetic reproduction separately.

| Code | Meaning / next step |
| --- | --- |
| JA-CONTEXT-01 | Unsupported/currently incomplete position, comment/string, USE or query-size limit; move the caret to a supported column/JOIN position |
| JA-SCOPE-01 / JA-SCOPE-02 | Missing or ambiguous source/alias; check the current FROM and aliases |
| JA-LOCAL-01 | No reliable earlier local-table declaration in this script |
| JA-COLUMNS-01 | No resolvable columns; verify source definition and metadata visibility |
| JA-DML-01 | All known columns are assigned already or excluded from write suggestions |
| JA-FK-01 | No visible FK candidate; ordinary table-name suggestions may still be available |
| JA-MATCH-01 | Candidates exist but none matches the typed name |
| JA-METADATA-01 / JA-TIMEOUT-01 | Metadata/connection failure or timeout; verify the query tab connection and retry |
| JA-SETTINGS-01 / JA-SETTINGS-02 | Preferences could not be loaded/saved |

INSERT and UPDATE complete column names in ordinary column lists/SET assignments, not values. Already assigned columns, known identity/computed/rowversion columns are intentionally absent. TOP and complex/CTE-target DML forms are not fully resolved. Two-hop JOINs are explicitly labelled, bounded and optional; an ON completion never adds intermediate tables.

## No foreign-key candidates

JoinAssist requires real, visible foreign-key metadata. Matching column names alone are insufficient. Check the selected database in the query toolbar and your metadata permissions. Test two simple base tables first. Expressions, cross-database sources or ambiguous CTE/view lineage may intentionally produce no candidates.

Do not grant broader permissions solely to troubleshoot without involving your database administrator.

## Local-table completion

Define local tables earlier in the same script with a top-level `CREATE TABLE #name`, `DECLARE @name TABLE` or a resolvable `SELECT ... INTO #name`. Names are unqualified local names; global `##temp`, declarations in another tab or inside procedures/control-flow blocks, and user-defined table types are not resolved. The plugin analyzes script text and never executes it or inspects the SSMS session's temporary tables.

`@table` variables end at GO. DROP removes the definition; ALTER and conditional changes discard uncertain shapes. Executing dynamic SQL/procedures invalidates temporary-table assumptions. A copied SELECT INTO shape does not gain FK suggestions: the real FK constraints were not transferred. Base-table FK suggestions still work alongside local sources.

If broader search returns too many candidates, continue typing: exact names and prefixes stay ahead of internal fragments and compact abbreviations. Explicit `schema.name` keeps the schema filter exact. Single-letter searches remain prefix-only.

## Connection or Microsoft Entra errors

Read the full status message in **JoinAssist → Diagnostica…**. A connected Object Explorer is not sufficient: the **query tab itself** must have a working connection. Reconnect that tab using its normal SSMS connection dialog. Do not send passwords, access tokens or connection strings in an issue.

If reconnecting does not help, report the SSMS version, JoinAssist version, authentication method and sanitized message. SSMS updates can change internal connection integration. Do not change authentication methods just to bypass the extension's error.

## Query or connection changed

Suggestions belong to a particular query snapshot and connection. If either changed before insertion, reopen the suggestions. Repeated context-change warnings with an unchanged tab are a bug: include a minimal reproduction in an issue. Undo an unexpected insertion with Ctrl+Z.

## Invalid VSIX or installation error

Download the `.vsix` release asset again; do not rename GitHub's source ZIP into a VSIX. If desired, compare `Get-FileHash -Algorithm SHA256` with the release's `SHA256SUMS.txt`. Preserve Microsoft's installer log and remove personal paths or credentials before sharing it.

## Remove JoinAssist

Save queries and close every SSMS window. Run [Uninstall-JoinAssist.cmd](../Uninstall-JoinAssist.cmd) from this repository; it invokes Microsoft's installer for **only JoinAssist**, and the installer handles removal confirmation. It does not remove other extensions or SSMS.

For a custom SSMS location, invoke that installation's `VSIXInstaller.exe` with:

```text
/uninstall:JoinAssist.FK.4d45475a-842b-42b3-b2f7-b392df87bdb8
```

If removal fails, keep the installer log. Avoid deleting entire SSMS extension/cache directories: they may contain unrelated components.

## Update button and download problems

The **Aggiornamenti…** button is available from 0.5.0. Install that version manually once if upgrading from 0.4.1. Opening the window checks GitHub; **Ricontrolla** retries the check. No GitHub login is required, and SQL/Entra login problems are unrelated to GitHub updates.

If a network/proxy or GitHub rate limit blocks access, retry later or use **Release su GitHub** for manual download. Only ordinary published releases with a matching VSIX asset and valid SHA-256 digest are accepted. Missing digests, checksum mismatches and incomplete downloads block the in-app download rather than opening an unverified file. Do not bypass a checksum failure; report it with the release version.

**Apri cartella** becomes available after successful verification. Files are saved in `%LOCALAPPDATA%/JoinAssist/Updates`; keep the VSIX you need and remove older verified downloads manually if desired. Close the update window to cancel an active operation. For installation, save queries and close every SSMS window before opening the VSIX. There is no automatic shutdown. A plain download does not launch an installer. From 0.10.0, explicitly requesting **Installa alla prossima chiusura** allows the deferred launch described below.

## Install at next close (0.10.0)

Upgrade from 0.9.0 once through the existing download/close/open-VSIX path. The scheduling button is part of 0.10.0, so an older installed plugin cannot use it for its own first upgrade. It is enabled only for a newer ordinary release; a download alone never arms the worker.

After **Installa alla prossima chiusura**, you may close the Updates window and keep working. The hidden bundled helper waits for all processes named SSMS to close, including other open instances, and for a short quiet interval. Reopening SSMS extends the wait. It rechecks the downloaded package and host-adjacent Microsoft installer before opening the normal installation UI. Finish its ordinary confirmations yourself. An “installer started” status is not an installation-success report.

Use **Annulla installazione in attesa** before launch to stop it. Cancellation is shared across your SSMS windows and serialized with the launch transition. Once the Microsoft installer has opened, use that installer's own cancellation controls. The plugin never closes SSMS or kills a process.

Waiting expires after 24 hours. Closing/logging off Windows, worker termination or a failure does not create a persistent startup task; reopen Updates and schedule again. Read-only/junction-based update locations, missing host installer, changed files, access/security restrictions and a worker blocked by endpoint protection can prevent launch. Use the ordinary manual download path if needed. Never relax security protections to run the helper.

| Code | Meaning |
| --- | --- |
| JA-UPDATE-INTEGRITY-01 | Package/installer size or checksum changed; download and schedule again |
| JA-UPDATE-WORKER-01 | Waiting process unavailable/interrupted; explicitly schedule a new request |
| JA-UPDATE-START-01 | Installer start could not be completed/confirmed; inspect any already opened installer before retrying |

The local record is in `%LOCALAPPDATA%/JoinAssist/Updates/Deferred/pending.json`. Do not post it publicly without removing personal file paths. If its data is corrupt, use manual installation and preserve the sanitized error for diagnosis. Do not delete metadata while a request is still armed: cancel it first. Helper/status/download files can be removed after the request has finished or been cancelled and the helper has exited.

## Report a reproducible problem

Use the [bug report form](https://github.com/DragonPalowski/JoinAssist/issues/new?template=bug_report.yml). Replace business names with generic tables, and include minimal table/foreign-key definitions when relevant. Public issues must not contain customer data, corporate server addresses, account names or secrets.
