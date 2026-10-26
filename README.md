# JoinAssist

Free table, column and foreign-key JOIN completion for **SQL Server Management Studio 22 (x64)**.

[Download the latest release](https://github.com/DragonPalowski/JoinAssist/releases/latest) · [Italiano](docs/README.it.md) · [Report a bug](https://github.com/DragonPalowski/JoinAssist/issues)

JoinAssist reads your database metadata and suggests SQL while you type. JOIN conditions come from actual foreign keys, including composite keys. It does not use AI or guess relationships from column names.

**Early-stage community project.** The current interface is in Italian. SSMS updates can affect integration with its editor and connection internals; please report compatibility problems. This project is not affiliated with Microsoft.

## What it does

- Suggests related tables after `JOIN`, and conditions after `ON`. Also offers explicitly labelled paths through one intermediate table (two JOINs), using real foreign keys and a full SQL preview.
- Completes table/view names and columns, including `table.column` and `alias.column`, INSERT column lists, UPDATE SET assignment names and DELETE/UPDATE WHERE filters. Column details include type/size, nullability and actual key/identity/computed metadata.
- Completes common SQL keywords with Tab and prepares type-aware LIKE/NOT LIKE and IN/NOT IN placeholders. Both helpers have independent settings; existing values are preserved. A mouse click selects the actual row and double click inserts it. See [typing helpers](docs/TYPING-TEMPLATES.md).
- **Impostazioni → Regole** adds personal SELECT rules (TOP and ordering per table/view) and literal SQL shortcuts with a cursor placeholder. Preview and accept with Tab, or explicitly enable automatic application after Space for an exact, unambiguous rule. Scope rules to a server/database and import/export them locally. See [personal rules](docs/PERSONAL-RULES.md).
- After `INSERT INTO TableName `, **Prepara INSERT** generates an ordered column list and matching `VALUES (@column, …)` placeholders. Tab accepts it and places the caret at the first value. Inside an empty column-list slot, **Inserisci colonne mancanti** fills only the missing names without changing existing values. Identity, computed, rowversion and generated-always columns are excluded; default columns are optional in Settings. Nothing is executed.
- **Prepara DELETE con dipendenze…** previews a filtered, multilevel FK deletion draft. It freezes row scopes, orders explicit deletes from children to parents and accounts for database referential actions. Insertion requires confirmation; the script ends with ROLLBACK by default. Requires VIEW DEFINITION and primary keys, with unsupported cases blocked. See [DELETE planning](docs/DELETE-PLANNING.md) before using it.
- Finds names by prefix, word starts, internal fragments, compact abbreviations and bounded typing-error correction, including swapped letters and unfinished prefixes. Exact/prefix matches remain first: `custo` favors `customers_tb`, `itemdet` can find `order_item_details_tb` and `patietns` can find `patients_tb`. Search preferences, matched-letter highlights, original-name/alias lookup and optional local choice memory are described in [smart completion](docs/SMART-COMPLETION.md).
- Shows the complete `ON` condition and actual FK name in each JOIN row, so multiple relationships to the same target are easy to distinguish. Details wrap without changing the inserted SQL.
- Completes names and columns of local `#temp` tables and `@table` variables declared earlier in the script, including resolvable `SELECT ... INTO #temp` projections.
- Understands CTEs, derived queries and direct column lineage through views. It follows real foreign keys only where the relationship can be resolved safely.
- Generates compact SQL: no automatic alias, schema or square brackets unless required for valid, unambiguous SQL. Existing aliases are respected.
- Supports both directions of a foreign key, multiple JOINs and INNER/LEFT/RIGHT/FULL JOINs.

For example, given foreign keys from `OrderItems.OrderId` to `Orders.OrderId` and from `OrderItemDetails.OrderItemId` to `OrderItems.OrderItemId`, it can insert:

```sql
SELECT * FROM Orders
INNER JOIN OrderItems ON Orders.OrderId = OrderItems.OrderId
INNER JOIN OrderItemDetails ON OrderItems.OrderItemId = OrderItemDetails.OrderItemId
```

## Install

1. Download `JoinAssist-<version>.vsix` from [Releases](https://github.com/DragonPalowski/JoinAssist/releases/latest). Use the VSIX asset, rather than GitHub's source ZIP.
2. Save your work and close **all SSMS windows**.
3. Open the VSIX and select **SQL Server Management Studio 22** in Microsoft's installer.
4. Reopen SSMS, connect a query tab to your database and start typing.

You do not need build tools or a separate service. Earlier SSMS versions and other SQL editors are not supported. Avoid running multiple third-party completion providers together while diagnosing problems.

## Use

Open **JoinAssist** in the menu bar. Depending on SSMS customization, it may initially appear under **Extensions → JoinAssist**. To show it beside the other top-level menus, use **Extensions → Customize Menu…**, clear the **JoinAssist** checkbox in **Extensions Menu**, then **Save and Restart**. SSMS controls exact ordering. Version 0.8.0 removes the per-query horizontal strip above Results. From 0.9.0, JoinAssist sets its main-menu default once in recognized SSMS settings; it may take the next normal restart to appear. Other menus and later placement choices are preserved. The manual path above remains the fallback.

| Menu command | Purpose |
| --- | --- |
| `Completamento automatico` | Toggle typing suggestions; remembered across restarts |
| `Mostra suggerimenti` | Invoke completion for the active SQL editor; current shortcut shown alongside |
| `JOIN da foreign key…` | Open the manual relationship picker |
| `Espandi colonne (*)…` | Choose and preview explicit columns for the SELECT star at the cursor |
| `Informazioni tabella / colonna…` | Inspect column properties and real FK relationships for the identifier at the cursor |
| `Aggiorna metadati` | Invalidate the active connection/database metadata cache |
| `Impostazioni…` | Completion, SQL-format, cache, search and personal-rule settings |
| `Aggiornamenti…` | Check/download updates and optionally start installation after SSMS closes |
| `Diagnostica…` | Read status and copy a sanitized diagnostic report |

Query-specific commands are disabled when no supported SQL editor is active. Opening the menu does not execute SQL or read metadata. The native menu follows the SSMS theme; popup and dialog surfaces use host theme resources. Compact suggestion rows show the object/column name and distinguishing metadata or FK name; the complete JOIN preview appears for the selected relationship.

Try `SELECT * FROM Ord`, then press **Tab** to accept the selected table. After `JOIN `, choose a related table; after `ON `, choose its foreign-key condition. Use **↑/↓** to select, **Tab/Enter** to accept and **Esc** to dismiss. Insertions can be undone with **Ctrl+Z**. **Ctrl+Alt+Space** invokes completion manually even with automatic completion off. Change the shortcut or disable Enter acceptance in settings if needed. Typing inside comments and strings is silent; use **Diagnostica…** for actual errors.

Suggestions use the **active query tab's connection and database**. Database permissions determine which metadata is visible. SQL authentication, Windows authentication and Microsoft Entra integration depend on the active SSMS connection; JoinAssist does not provide a separate login screen.

You can add JOIN/ON completions before an existing WHERE, GROUP BY, HAVING or ORDER BY clause without commenting out the rest of the query. Version 0.6.1 recovers ordinary SELECT contexts when earlier statements or unrelated expressions are unfinished. Recovery uses an analysis copy and leaves the actual query, suffix and comments unchanged; it cannot infer columns from an unresolved source definition.

See [a disposable demo schema](examples/demo-schema.sql) and [troubleshooting](docs/TROUBLESHOOTING.md).

Version 0.16.0 adds [personal completion rules](docs/PERSONAL-RULES.md). Configure `logs.logs_tb` with TOP 100 and descending first-column ordering: accepting its rule turns `select * from logs.logs_tb` into `select TOP 100 * from logs.logs_tb ORDER BY 1 DESC`. Existing TOP/ORDER BY and WHERE are retained. New installations have no rules; ordinary completion keeps working as before.

Version 0.15.1 fixes missing proposals beside `OPENJSON`, including CTEs and chained `CROSS APPLY`/`OUTER APPLY`. Ordinary table aliases remain available; JSON aliases expose `key`, `value`, `type` by default, or exactly the names/types in an explicit `WITH` clause. These are static result schemas from [SQL Server's OPENJSON definition](https://learn.microsoft.com/en-us/sql/t-sql/functions/openjson-transact-sql), not properties discovered by reading JSON rows. JSON fields have no inferred FK lineage.

## Productivity tools (0.9.0)

Column ranking keeps exact names first. Within each text-match band, local sources precede correlated outer sources; ON suggestions prefer participating sources, known keys and compatible comparison types. Table/view proximity ordering stays unchanged.

A simple unfinished predicate such as `JOIN Customers c ON o.Cust` can propose `o.CustomerId = c.Id` from the real FK. Starting with `c.Id` preserves that side first. Composite keys include all pairs. Existing AND/OR expressions, comments and query suffixes are not replaced. Ordinary column completions remain available when no FK matches.

Place the cursor on `*` or `alias.*` in a SELECT and choose **Espandi colonne (*)…**. Check the columns you want and review the preview before insertion. The command preserves source order and qualifies names with existing references, replaces only the star, and supports Ctrl+Z. It rejects COUNT(*), unresolved sources and ambiguous output names rather than inserting a partial list. It uses the current FROM scope and does not expand an outer query's source.

Use **Informazioni tabella / colonna…** with the cursor on a table, alias or qualified column to inspect its metadata. In whitespace inside a recognized SELECT it lists current sources. **Colonne** shows types and available properties; **Relazioni FK** shows real constraints only where all necessary columns can be mapped. Information reflects catalog visibility and the metadata cache, not rows from the edited query.

CTE/derived expression aliases can carry statically known output types from CAST, CONVERT, TRY_CAST, TRY_CONVERT, compatible CASE/COALESCE branches, literals, ISNULL and selected count/ranking functions. Mixed/unknown type coercions remain marked as unavailable. Computed expressions never become FK lineage. This is a conservative description, not SQL Server's full expression type checker.

Analysis now reuses unchanged lexical prefixes and independent SELECT statements separated by semicolons, plus GO batches. Unsafe/procedural boundaries and QUOTED_IDENTIFIER use a conservative fallback. Scripts without reliable boundaries can still require a full parse. All analysis stays off the typing UI path and in bounded process memory.

## Completion settings and metadata

Version 0.7.0 analyzes typing snapshots in the background, keeping one parser active per editor and discarding stale results. Bounded token/parse caches stay in memory. Qualified column completion reads the requested source and, when needed, the explicitly compared source rather than every table in a large query. Incomplete correlated subqueries, APPLY, CTE chains and later UNION branches retain the sources they can legitimately see; ordinary derived tables cannot borrow outer aliases.

Catalog names, object details and foreign keys are cached in memory for **2 minutes** by default. The cache belongs to the native SSMS connection and session, server and database; reconnecting or changing database uses a separate scope. After DDL/schema changes, click **Aggiorna metadati** and invoke completion again. Expiry is configurable from 1 to 15 minutes. No query, metadata, password or token is written to a cache file.

**Impostazioni…** remembers automatic completion, the 220 ms default delay (70–2000 ms), Tab/Enter acceptance, a manual shortcut, two-hop paths, and optional schemas/brackets/generated aliases/lowercase keywords. Defaults preserve compact SQL. Required quoting, qualification and self-JOIN aliases remain enforced. Preferences are saved to `%LOCALAPPDATA%/JoinAssist/preferences.json`. From 0.16.0 this also contains SQL and server/database names explicitly entered in personal rules. The editor's query text is not saved there. Only accepting a personal rule, or enabling its optional Space action, transforms its supported SELECT.

INSERT and UPDATE suggestions complete **column names**; they do not generate values, execute statements or determine whether a view is writable. Already listed/assigned columns are omitted, including assignments after the caret. Identity, computed and rowversion columns are omitted when known. Ordinary INSERT column lists and UPDATE targets/`UPDATE alias ... FROM ...` are supported; TOP/complex DML forms and CTE DML targets may remain unresolved.

Two-hop proposals insert both JOINs only when you explicitly accept the labelled path. A path uses one intermediate physical table, includes every column of composite foreign keys, and excludes disabled edges and repeated table IDs. Untrusted constraints retain a warning. Exploration is bounded (up to 1,000 adjacent IDs, 200 candidates per step, 100 displayed paths); this is not an arbitrary-length graph search. Review the complete preview, especially the JOIN kind and alternative relationships.

From 0.15.0, **1 JOIN · FK comuni** offers an equality between two child tables when their enabled FKs reference the same complete parent key. It maps the actual child columns, including composite keys and supported CTE projections. These candidates appear before two-hop paths, while actual direct FKs remain first within the same text-match band. A matching parent entity name only orders already-valid alternatives; it never creates a relationship. The shared parent, predicate and supporting constraints are shown in the preview. The existing JOIN-path setting controls both features (up to 200 inferred candidates).

A shared-key candidate is **not a direct FK or a promise of equivalent results**. Two child tables can have many rows per key, so the equality can multiply rows. Omitting the parent can change results with untrusted constraints/orphans, parent filters or row visibility; different JOIN kinds also matter. Longer paths remain available for review. No existing JOIN is automatically collapsed, and ON/partial-ON completion keeps its direct-FK behavior.

## Local tables and flexible search

Type fragments such as `itemdet` to find `OrderItemDetails` or `order_item_details_tb`. Exact names come first, then prefixes (shorter table names first; columns also use contextual hints), internal matches, separator-insensitive matches and compact ordered abbreviations. Single-character input remains prefix-only; widely scattered letters are excluded. Broader matching also applies to column names and respects explicit schema qualifiers.

Local table shapes come from earlier, complete declarations in the current script:

```sql
CREATE TABLE #Orders (OrderId int, ItemName nvarchar(100));
DECLARE @Items TABLE (ItemId int, ItemName nvarchar(100));
SELECT OrderId AS CopiedId INTO #Copy FROM #Orders;

-- Try #Or after FROM, or type t.Item in this SELECT:
SELECT t.ItemName FROM #Orders t;
```

The plugin never runs these statements. It reads explicit column definitions and resolves SELECT INTO names, aliases and `*` from visible source columns. Local tables can feed CTEs and derived queries. SELECT INTO takes a snapshot of the source shape at its position in the script; it does **not** inherit FK constraints or FK suggestions. Base tables elsewhere in the same query still offer their real FK relationships.

`#temp` declarations can cross `GO` within the script; `@table` variables cannot. DROP removes a local definition; a subsequent CREATE supplies a new shape. ALTER, conditional DDL and dynamic/procedure execution invalidate affected assumptions rather than keep stale columns. Declarations must be ordinary top-level statements with unqualified local names. Global `##temp`, procedure-local declarations, control-flow-dependent shapes, user-defined table types and objects declared only in another tab are not resolved. This is analysis of the script text, not inspection of what has actually executed in your session. See [local completion examples](examples/local-completion.sql).

## Scope and limitations

- CTEs, derived queries and views support direct column mappings, renamed columns and resolvable `*` expansions. Recursive/UNION outputs can offer column names, but ambiguous lineage does not produce inferred JOINs.
- Same-database, schema-qualified synonyms are supported. Table-valued functions can expose catalog column names; they do not acquire invented foreign keys.
- Cross-database/server names, dynamic SQL and PIVOT/UNPIVOT are not supported. Local-table support has the limits described above. SQLCMD and multi-server query contexts are excluded. A `USE` statement in the current text does not switch the metadata connection.
- This is a focused completion helper, not a complete T-SQL language service. Very large inputs are bounded (500,000 characters, 40 resolved sources and 200 visible local tables).
- Automated tests cover parsing, metadata, ranking and editor insertion. They do not replace live testing on every SSMS release and authentication configuration.

## Privacy

JoinAssist reads catalog metadata through the active connection. It does not execute the SQL in your editor, send queries to external services, store passwords/tokens, or collect telemetry. It opens a metadata connection using the active SSMS client's connection information. The update button contacts GitHub on request without sending query text or database information. See [privacy and connection details](docs/PRIVACY.md).

## Updates and roadmap

Click **JoinAssist → Aggiornamenti…** (available since 0.5.0; older versions used the query editor strip). The window checks the latest ordinary GitHub release, shows the installed/available version and release notes, and enables **Scarica aggiornamento** when a newer version exists. No GitHub account is required. Checks run only when you open this window or click **Ricontrolla**; there is no background polling.

The VSIX is downloaded to `%LOCALAPPDATA%/JoinAssist/Updates` and verified against GitHub's SHA-256 digest before it becomes available. Use **Apri cartella** to locate it, save your queries, close every SSMS window and open the VSIX to install. JoinAssist never closes SSMS. A plain download does not start an installer. Old verified downloads remain on disk until you remove them.

From **0.10.0**, **Installa alla prossima chiusura** downloads/verifies a newer release (or reuses the current window's verified download) and explicitly schedules the Microsoft installer for when all SSMS instances have closed. You can close the update window and keep working. The worker waits for a short quiet interval, rechecks the package and installer, then opens the normal Microsoft installation UI. Complete its usual confirmations; the plugin does not confirm success or reopen SSMS.

**Annulla installazione in attesa** cancels a request before installer launch. Status is shared by your SSMS instances and remains visible on reopening Updates. Waiting expires after 24 hours. There is no Windows service, scheduled task, login autorun or automatic resume after worker failure/logoff/reboot. Only explicitly armed waiting uses a separate local process; GitHub is not polled in the background. New SSMS processes extend the wait. Failed integrity checks block launch, and a stale worker/request is never silently resumed.

**Upgrading from 0.9.0 or earlier:** install 0.10.0 using the existing download/close/open-VSIX procedure once. The new scheduling button is provided by 0.10.0 and applies to later updates. It becomes available when a newer ordinary release exists. Manual download/install remains available.

**Upgrading from 0.4.1:** download and install 0.5.0 manually once; the previous version does not have the update button. Manual downloads remain available in [Releases](https://github.com/DragonPalowski/JoinAssist/releases/latest). If Microsoft's installer requests removal of an old copy, follow [the removal guide](docs/TROUBLESHOOTING.md#remove-joinassist).

The updater excludes drafts and prereleases and never offers a downgrade. The checksum detects incomplete or changed downloads; it is not a code-signing certificate. Planned improvements include an English interface and wider compatibility testing.

## Development

[Build and test](docs/BUILDING.md) · [Contribute](CONTRIBUTING.md) · [Release process](docs/RELEASING.md) · [Changelog](CHANGELOG.md)

The source, tests and build scripts are available under the [MIT license](LICENSE). Microsoft components used to build or run the extension retain their own licenses and are not bundled in the VSIX.
