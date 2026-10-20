# Changelog

## 0.12.1 — 2026-10-20

- Fix missing table/alias and column proposals while editing an unfinished SELECT list before an existing qualified column, including a missing comma. ScriptDOM may retain a partial SELECT without its FROM; recover the current query's sources in an analysis-only copy instead of treating that partial syntax tree as complete.
- Preserve the current query boundary, aliases, CTE/derived sources and the exact replacement span. Add synthetic end-to-end regressions for joined sources, TOP, multiple statements, reference insertion and subsequent dot/column completion. No edited SQL is executed.

## 0.12.0 — 2026-10-20

- Add **Prepara DELETE con dipendenze…** to the compact menu. Parse a complete single-table DELETE with WHERE, load fresh database-wide FK/table metadata and preview the dependency order and generated SQL before replacing only that statement. Never execute edited SQL or read/count business rows.
- Follow incoming NO ACTION/CASCADE relationships across multiple levels. Union scopes from multiple paths, preserve all composite-key pairs and capture referenced non-PK keys. Materialize every row scope before deleting; use primary keys, reverse dependency order and locking hints. Let SQL Server handle cascade-only deletions and SET NULL/SET DEFAULT actions, while retaining ordering constraints between selected branches.
- Generate a transaction script with ROLLBACK by default, error rollback, scope cleanup, XACT_ABORT restoration and guards against ambient/implicit transactions. Explicitly use SET ROWCOUNT 0, which remains a session setting. Capture rowversion keys as binary values. The script is an editable draft; not an execution engine or proof that every database/business rule is covered.
- Require database VIEW DEFINITION and known persistent tables with primary keys. Block cycles/self references, active DML triggers, security policies, temporal/history/memory-optimized tables, hidden/unresolved metadata, case-only column ambiguity and oversized graphs. Reject DELETE without WHERE, TOP/OUTPUT/OPTION/CTEs/joins/cross-database targets and session-state predicates in this first version.
- Add graph, parser/SQL-structure, metadata-only reader, UI blocking/preview and stale-query/connection editor tests. No real DELETE/installer or production database is used by tests.

## 0.11.0 — 2026-10-20

- Add the explicit **Prepara INSERT** proposal after a complete INSERT target and a space. Generate a multiline column list and aligned `VALUES (@column, …)` placeholders, in table column order. Keep necessary delimiters, keyword casing, indentation and line endings; move the caret to the first value.
- Add **Inserisci colonne mancanti** at an empty INSERT column-list position. Exclude names already present before/after the caret; preserve existing parentheses and values and retain single-column suggestions.
- Exclude identity, computed, rowversion and generated-always columns. Read default/generated flags from catalog metadata and explicit local definitions. Add a persistent, default-on option to include columns with defaults in bulk INSERT proposals. Unknown writeability, views/synonyms/CTE targets and existing SELECT/VALUES/OUTPUT syntax do not receive a generated structure.
- Sanitize parameter names and make them distinct within the row. Parameters are editable placeholders, not declarations or fetched data. Add compact preview, SQL syntax/metadata/regression tests and editor insertion/caret checks. Never execute the INSERT.

## 0.10.1 — 2026-10-20

- Fix missing column proposals in unfinished `DELETE … WHERE` and `UPDATE … WHERE` filters. Recover the current DML target and FROM sources from an analysis-only copy of its header, including aliases, schemas, TOP, OUTPUT, hints, CTEs and local tables.
- Keep ordinary readable columns available in filters, including identity and computed columns. Preserve the current token replacement span, remaining predicates and later statements. Never execute the edited DML or inherit sources from an earlier statement.
- Add synthetic regression coverage for simple/qualified filters, joins, CTEs, local tables, nested subqueries, multi-statement recovery and exact text preservation.

## 0.10.0 — 2026-10-19

- Add **Installa alla prossima chiusura** to the update window. Download and verify an available release on demand, then explicitly arm a bundled standalone worker.
- Wait for all SSMS processes to close and remain closed for a short quiet interval. Never close or kill SSMS, restart it, or confirm installer prompts. Open the host installation's Microsoft VSIX Installer with the verified package.
- Add **Annulla installazione in attesa**, shared status across SSMS instances and a 24-hour expiry. Closing the update window keeps an armed request; shutdown/logoff or a failed worker require an explicit new request.
- Recheck package size/SHA-256 and installer checksum before launch, bind the worker to the immutable request fingerprint, serialize cancellation/launch across processes, and prevent duplicate launches. Retire waiting requests already covered by the installed plugin version.
- Embed the versioned worker inside the plugin; no SQL/SSMS dependencies, service, scheduled task, Windows autorun or background GitHub polling. Retain local pending status and downloaded files for user inspection.
- Test waiting, cancellation, expiry, quick reopen, changed files, request tampering, worker identity and launch failure using synthetic files and a separate test worker. Extend WPF/button and embedded-resource checks; no real installer is launched in tests.

## 0.9.0 — 2026-10-19

- Rank column candidates by local scope, active ON sources, known key flags and compatible comparison types within each text-match band. Exact names remain first; table/view proximity ranking is unchanged.
- Complete a simple partially written ON identifier/equality from real foreign keys, preserving the typed side, existing aliases, composite keys and the following query clauses. Keep normal column completion as a fallback; do not rewrite an existing AND/OR condition.
- Add **Espandi colonne (*)…**: select a SELECT star, review and choose source columns, then replace only that star in one undoable edit. Preserve source order and existing aliases; reject unresolved/ambiguous sources and COUNT(*).
- Add **Informazioni tabella / colonna…** for the identifier under the cursor, with column types/properties and actual mapped FK relationships. Resolve tables, aliases, CTEs and derived sources without running the edited query.
- Infer computed CTE/derived output types for CAST/CONVERT/TRY variants, compatible CASE/COALESCE branches, literals and selected built-ins. Unknown coercions stay unknown; expressions never inherit FK provenance.
- Reuse lexical prefixes while typing and cache independent SELECT statements/GO batches with original offsets. Fall back conservatively for procedural text and QUOTED_IDENTIFIER. Skip local-definition parsing when there are no local identifiers.
- Set JoinAssist's top-level menu default once in the recognized SSMS Customize Menu settings. Preserve other menu entries and later user choices. Placement may need the next normal restart; no forced restart or installer launch.
- Add synthetic engine, lexer-equivalence, partial-predicate insertion, menu-default and WPF tool-window tests. No production SQL, credentials or telemetry are used.

## 0.8.0 — 2026-10-19

- Replace the per-query horizontal strip with a native, grouped JoinAssist menu. Keep automatic completion attached through a view-creation listener without reserving editor space.
- Route query commands to the currently active registered SQL view, disabling them for unsupported/closed tabs. Retain configured shortcuts and automatic completion preferences.
- Add native theme-aware command icons, host-themed popup/dialog surfaces, compact two-line suggestions and selected-relationship-only SQL preview.
- Group preferences into Completion, SQL format and Cache pages. Fix Save/Cancel for the modeless settings window.
- Move status and sanitized-copy commands into a dedicated diagnostics window. Keep typing inside comments/strings silent.
- Compile/merge command resources and generate package registration with Microsoft VSSDK tools. Document Extensions/Customize Menu placement and add menu/theme/editor-lifecycle regressions.

## 0.7.0 — 2026-10-16

- Move typing analysis off the editor UI thread, coalesce pending snapshots, and reuse bounded in-memory token/parse results. Qualified completion loads only the requested source. Stale snapshots, settings and connections are checked before results or edits are applied.
- Cache catalog headers, object details and foreign keys in memory for two minutes by default, isolated by native connection/session/server/database. Add **Aggiorna metadati** and configurable expiry. No credentials or metadata are persisted.
- Recover incomplete correlated subqueries, APPLY and CTE chains, including WITH definitions in later UNION branches. Honor alias shadowing and correlation barriers; never expose later/self APPLY sources.
- Add explicit missing-scope/local-definition/FK/column and timeout diagnostics, plus **Copia diagnostica** with versions and diagnostic code only.
- Add **Impostazioni…** with persisted automatic completion, delay, cache lifetime, acceptance keys, manual shortcut and SQL formatting preferences. Default manual shortcut: Ctrl+Alt+Space; Tab always accepts, Enter is optional.
- Complete INSERT target column lists and UPDATE SET assignment column names, including ordinary UPDATE alias FROM targets and script-declared local tables. Exclude already listed/assigned, identity, computed and rowversion columns. No DML is executed.
- Offer labelled two-hop FK paths through one intermediate physical table, with full preview and composite conditions. Direct relationships stay ahead of paths to the same target; disabled edges/cycles are excluded. Paths can be disabled.
- Display actual column lengths, precision/scale, nullability, PK/FK and identity/computed flags when available. Derived projections do not claim inherited constraints.
- Extend synthetic core, editor insertion, settings UI and cache-isolation tests.

## 0.6.1 — 2026-10-16

- Recover the active SELECT's sources when earlier unfinished statements, incomplete projections or later filter/JOIN expressions prevent normal parsing. Keep resolvable CTE definitions; never borrow sources from unrelated statements.
- Restore column suggestions after SELECT DISTINCT/TOP, CASE branches, LIKE/BETWEEN and comparison operators.
- Complete JOIN/ON before existing WHERE/GROUP/HAVING/ORDER clauses even when those clauses are incomplete. Preserve the original suffix and comments exactly.
- Add synthetic large-script/context regressions, statement/scope isolation checks and editor insertion coverage. Recovery affects only an analysis copy, never the query text.

## 0.6.0

- Add broader table/column search: internal fragments, separator-insensitive matches and compact ordered abbreviations. Preserve exact/prefix priority, deterministic ordering and explicit schema filters.
- Show mapped ON columns and the real FK name in each wrapped JOIN row, including aliases, composite conditions and warnings.
- Add script-based name/column completion for top-level `CREATE TABLE #temp`, `DECLARE @table TABLE` and resolvable `SELECT INTO #temp`. Support projection through CTEs/derived queries, source-shape snapshots, DROP/recreate and GO scoping.
- Invalidate uncertain local shapes after ALTER, conditional DDL or dynamic/procedure execution. Never infer FK constraints for copied/local tables.
- Add focused regression coverage for matching, relationship presentation, local-table analysis and real editor-controller insertion.

## 0.5.0 — 2026-10-15

- Add **Aggiornamenti…** to the query editor strip, with installed/latest version and release notes.
- Check public GitHub Releases on request without login; offer only newer ordinary releases.
- Download the VSIX directly and verify its expected size and GitHub SHA-256 digest. Reject untrusted redirects and incomplete/corrupt files; cancel when the window closes.
- Provide **Apri cartella** and instructions to save queries, close SSMS and run the installer. Never close SSMS or launch an installer automatically.
- Generate the assembly version from the VSIX manifest so update comparisons use the installed version.
- Add synthetic updater tests and a WPF window smoke check, plus an opt-in live download check.

## 0.4.1 — 2026-10-15

First public GitHub release. Experimental; current UI is in Italian.

- Rank table/view and column suggestions by closeness to the typed prefix: exact matches, then fewer extra characters, with alphabetical ties.
- Prefer object-name matches over matches only in the schema name.
- Select the best-ranked item as the prefix changes; preserve deliberate arrow-key selection until then.
- Include public build instructions, synthetic tests, demo schema and installation/troubleshooting guides.

## 0.4.0

- Add table and column completion alongside FK-based JOIN/ON completion.
- Resolve direct column lineage through CTEs, derived queries and views; support same-database synonyms and catalog columns for table-valued functions.
- Generate minimal SQL, adding schemas, quoting and aliases only when needed, and honor existing aliases.
- Support multiple query scopes and correlated sources, with conservative handling of ambiguous lineage.

## 0.3.1

- Stabilize query/connection identity checks before inserting suggestions.

## 0.3.0

- Reuse the active SSMS SQL client's connection information for metadata reads, including supported Microsoft Entra connection contexts.

## Earlier prototypes

Manual foreign-key selection, automatic JOIN suggestions and Microsoft VSIX packaging integration. These prototypes are not published as supported releases.
