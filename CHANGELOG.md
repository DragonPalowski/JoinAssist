# Changelog

## 0.21.0 — 2026-10-30

- Add opt-in EXEC/EXECUTE suggestions for visible current-database T-SQL/CLR procedures, including system procedures. Use one bounded metadata-only catalog query, cached in the existing connection/database scope and invalidated by metadata refresh. Show schema-qualified names and typed parameter signatures. Preserve keyword/ordinary completion when disabled and reject dynamic SQL, remote/cross-database calls and return-assignment syntax.
- Prepare named-argument drafts on acceptance, with editable NULL fields traversed through the existing Tab/Shift+Tab/Esc mechanism. Declare collision-free variables for OUTPUT and table-valued parameters; explain table population explicitly. Keep existing argument text and complete only missing named parameters, accounting for preceding positional arguments and existing equals signs. Ambiguous bare procedure names refuse parameter guessing. Conditional/procedural or unsupported signatures receive name completion only. All parameters are modeled; defaults are not inferred from catalog flags.
- Add completion settings and a built-in guide topic. Bound signatures/previews and preserve existing snapshot/connection validation before insertion. Add synthetic trigger/catalog/type/cache/model/scope regressions, simulated editor insertion/stale-session checks and WPF option-binding checks; tests never execute procedures or modify production/user settings.

## 0.20.0 — 2026-10-30

- Add opt-in **Da tabella a SELECT** under completion settings: type at least two characters of a standalone table/view name on a new instruction line, select a candidate and confirm with Tab. Preview the exact SELECT, optionally include TOP (blank/0 means none) and a trailing semicolon. Respect schema/identifier/keyword preferences and retain an existing terminator. Support declared local sources without catalog access; persistent objects require metadata. Preserve keyword/snippet priority and ordinary FROM/JOIN/expression completion. Require ; or GO after preceding instructions and reject uncertain procedural/unfinished contexts. Space, Enter and double-click do not expand this shortcut; no generated SQL is executed and SELECT rules are not applied.
- Stop pending/open automatic suggestions on caret navigation, invalidate in-flight analysis, and leave Tab to SSMS after moving away from the input endpoint. Suppress automatic completion inside existing words, including columns, JOIN names, DML and quoted identifiers; explicit completion remains available. Include both changes in the built-in guide.
- Add synthetic statement-boundary/format/settings/catalog/local-source checks, editor insertion/navigation/timer/stale-Tab tests and WPF option-binding checks. Existing completion/auth/update/package suites remain in place. No production queries, user preferences or installed extension are modified by tests.

## 0.19.1 — 2026-10-29

- Recover independently valid top-level local-table declarations after an earlier unfinished SELECT in a flat batch. Preserve both SELECT INTO projections, including qualified stars plus explicit joined columns, statement order and GO variable scope. Keep recovery bounded and refuse uncertain control-flow/procedure bodies, malformed DDL and future declarations.
- Complete typed #temp/@table names in FROM and JOIN directly from the script without opening a metadata connection. On connection/catalog errors, retain explicitly resolvable local columns; resolve copies so partial offline results cannot poison subsequent metadata-backed resolution. Qualify fallback columns beside unknown physical sources and reject ambiguous aliases.
- Preserve editor snapshot checks for script-only insertions and stale declarations. SELECT INTO does not inherit PK/FK lineage or automatic FK predicates; unknown star columns require visible source metadata. Add synthetic parser/catalog/offline regressions and simulated-editor insertion/staleness checks. No production SQL, user settings or installed extension is changed by tests.

## 0.19.0 — 2026-10-28

- Suppress automatic JOIN/ON FK proposals for empty/layout-only positions and suppress new-JOIN proposals before an immediately following JOIN clause. Preserve typed prefixes, normal columns/tables and completion before WHERE/GROUP/ORDER. Manual requests retain the complete list. Recheck policy before Tab acceptance to let indentation reach SSMS even if a stale automatic popup remains; clear manual-request state after insertion.
- Add **Disinstalla JoinAssist…** with explicit user/machine scope and Microsoft's interactive VSIX removal procedure for the current SSMS 22 instance. Resolve setup inventory using local vswhere, constrain the extension ID and instance, verify the installer and recheck target/scope and pending updates before launch. Preserve local personal settings/favorites; no recursive deletion, forced shutdown, silent removal or SSMS product uninstall. Document legacy-copy/RootSuffix and live-verification limits.
- Add layout/neighbor regressions, simulated-controller Tab/manual acceptance cases and synthetic uninstall identification/UI/race/theme checks. Update built-in guide instructions and add an uninstall topic (30 total). Existing completion, workflow, auth and packaging suites remain in place; tests never invoke a real uninstaller.

## 0.18.0 — 2026-10-28

- Add **Guida…**, an offline, full-text searchable Italian guide with 29 topics covering ordinary completion, all quick actions, rules/templates, DELETE planning, metadata, updates and troubleshooting. Include practical steps, copyable fictional examples, limits, related links and explicit settings/tool navigation. Search ignores case/accents and supports Ctrl+F; no query/connection/network is needed and searches are not saved.
- Add contextual **Guida / F1** inside plugin dialogs, selecting the active settings tab/action/rule type. Preserve SSMS editor keys and existing completion. Reuse the owned help window; disable tool-opening actions while a modal editor is active to preserve drafts. Wrap diagnostics buttons for smaller windows.
- Add offline content/link/search/context/action checks and WPF dark/light/minimum-size previews alongside existing suites. Guide buttons do not insert or execute SQL; clipboard copying is explicit. Document coverage, maintenance and the live-integration testing limit.

## 0.17.0 — 2026-10-27

- Add **Azioni rapide…**, an explicit searchable productivity workspace with preview, copy, guarded one-edit insertion and user-selected SQL-file/new-tab opening. Include scoped favorite and personal-rule entries. Existing shortcuts remain intact; optional Ctrl+Alt+P starts disabled. Add **Schema affiancabile…** and keep-open behavior without an editor margin.
- Prepare filtered related-record queries through FK paths, individually or as a bounded batch. Add path exploration up to five edges, cycle exclusion, cancellation and 30-path bounds, plus catalog-based 0..1 / possible 0..N cardinality indications. Preserve composite pairs and warn about untrusted FKs; no business-row counts or generated queries are executed.
- Add single-column clipboard IN/NOT IN conversion with explicit type, apostrophe escaping, optional deduplication and bounded input; multi-column selection/search; local/UTC half-open date filters; duplicate, NULL, grouped/daily-count and orphan-query drafts. Add parameterized dependency/column catalog search and named-source schema inspection.
- Add linked `{{name:default}}` / `{{name}}` fields in personal shortcuts and workflow models/favorites, with tracking spans, value mirroring, Tab/Shift+Tab navigation and Escape exit. Add validated explicit local favorites with categories/descriptions/scopes, atomic writes and concurrent stale-write refusal. No execution history is collected.
- Add strict scoped alias rename with full-script preview, preserving strings/comments, correlated references and nested alias shadowing. Refuse incomplete syntax, collisions and unsupported targets. Add rule probes for disabled/out-of-scope/missing-column/unsupported/no-op/conflicting rules and direct suggestion-retry/cache-refresh actions with local preparation timing.
- Fix personal proposals after continued typing/Backspace by reloading prefix-dependent candidates/spans; recognize mid-word shortcuts while replacing the full token. Explicit retry discards failed sessions; automatic same-prefix failures do not loop. Bound duplicated rule names. Add workflow core, metadata-reader, concurrency, dark/light WPF and simulated-editor field/stale-insertion tests, alongside existing suites. No production database, user preference writes or real installer is used by tests.

## 0.16.0 — 2026-10-26

- Add **Impostazioni → Regole** for per-table/view simple SELECT rules (optional TOP and first/named-column ordering) and literal SQL shortcuts with one optional `$cursor$` marker. Preview, edit, duplicate, enable/disable, delete and import/export up to 50 local rules. Drafts become active only on settings Save; existing users start with no rules.
- Integrate labelled personal choices into table completion with full SQL previews. Resolve targets against the catalog, verify named order columns and prioritize specific server/database scopes; retain tied alternatives. Global shortcuts work offline and retain ordinary keyword alternatives.
- Add opt-in application after a single inserted Space for one exact rule, with automatic completion on. Prefixes, ordinary presentations, competing rules and changed snapshots/carets/connections do not auto-apply. Revalidate configuration, scope and generated text immediately before one undoable editor edit.
- Restrict table transforms to strict simple root SELECT * statements, preserving existing TOP/ORDER BY, filters, aliases, comments and later statements. Skip JOIN/CTE/UNION/DISTINCT/INTO and other unsupported structures. Snippets remain literal user-authored text; no rule executes SQL. Imports create disabled fresh identities with Space off. Settings/exports contain configured SQL and scope names, documented in privacy guidance.
- Deep-copy settings drafts while keeping internal readers efficient; bound strict parse reuse in memory. Add themed fallback dropdowns when host styles are absent and synthetic core, controller/Space/stale, persistence/import and dark/light settings-editor checks. No production database, edited SQL, user preferences or installer is executed by tests.

## 0.15.2 — 2026-10-26

- Treat a written terminal `_vw`/`_tb` as a literal constraint for abbreviated name matching, comparing the stem separately. `pre_vw` prefers `prescriptions_vw` over scattered-letter matches and wrong-suffix abbreviations. Preserve full literal names/prefixes, schema/local markers, typo tolerance and correct highlight positions.
- Prepare LIKE/NOT LIKE and IN/NOT IN models directly from operator prefixes such as `li`, `lik`, `not li` and `no` after a resolvable simple column. Accept once to insert the complete eligible model and place the caret inside it. A NOT prefix can offer both models; retain ordinary keyword alternatives where no typed model is eligible, respecting both independent settings and keyword casing.
- Show the complete pattern/list in filter titles and the selected SQL preview. Keep existing RHS values/comments, type/ambiguity safeguards and query/connection checks for typed models. Plain keyword fallback requires no metadata connection and never inserts a guessed model.
- Add suffix/ranking/highlight regressions, partial/full operator and setting/type checks, WPF title/preview tests and editor replacement/caret tests with following statements. No production database, edited SQL, user preferences or installer is executed by tests.

## 0.15.1 — 2026-10-26

- Fix missing table/column/JOIN proposals when a FROM contains OPENJSON, including CTEs and chained CROSS/OUTER APPLY. Recognize ScriptDOM's OPENJSON table-reference node rather than rejecting the whole scope and hiding ordinary table aliases.
- Describe JSON result columns statically: key/value/type with the default schema, or exactly the columns/types declared in WITH, including AS JSON. Do not inspect JSON/business rows, invent properties from payloads, bind a JSON alias to a homonymous catalog object or assign JSON fields FK provenance. Keep ordinary projected base-column lineage.
- Add a failing-before-fix regression and synthetic long-CTE, JSON/APPLY, explicit-schema, scope isolation, focused metadata-loading, table/FK completion, exact editor insertion and stale-connection tests. Existing source/database restrictions remain enforced; no edited SQL, production data, user settings or installer is executed by tests.

## 0.15.0 — 2026-10-23

- Add single-JOIN candidates between child columns whose enabled foreign keys reference the same complete key on the same parent object. Pair composite columns by their referenced identity, including differing child names/ordinals; retain supported CTE lineage, SQL formatting and required aliases. Never infer from names alone or rewrite an existing query.
- Label these proposals **1 JOIN · FK comuni**, show the common parent, equality, actual supporting constraint names and untrusted warnings, and keep their history identity separate from two-hop paths. These are inferred equalities, not direct foreign keys or guaranteed replacements for a two-JOIN path.
- Share the same candidates between automatic completion and the manual JOIN picker. Within a text-match band, keep actual direct FKs ahead of inferred single JOINs, two-JOIN paths and plain table names. Parent-name affinity only orders already-supported inferred alternatives. Preserve longer paths and prefer a real FK when it generates identical SQL.
- Extend the existing optional JOIN-path setting to these candidates; disabling it also avoids the second-hop metadata read. Limit exploration and retain all key columns; skip disabled/incomplete keys, different parent objects/keys, repeated target objects and unsupported lineage. Existing ON/partial-ON completion remains based on direct FKs.
- Add synthetic shared-key graph/ranking, metadata-reader, composite/CTE/formatting/history, exact editor insertion and stale-connection tests. No production database, edited SQL, user settings or installer is executed by tests.

## 0.14.1 — 2026-10-23

- Fix mixed light/dark settings: explicitly theme tab surfaces and headers instead of relying on Windows' light tab templates or an incomplete host default-style dictionary. Keep selected, hover and keyboard-focus indicators visible.
- Make dialog labels, wrapped checkbox text and button captions follow their containing control's foreground; preserve selected-list text and explicit message colors. Retain host styles for other controls and update brush references when the theme changes while a window is open.
- Add WPF regressions for all four settings pages with inherited light styles, with and without a host default dictionary, across dark, light and synthetic high-contrast palettes. Render settings previews and check actual tab surfaces/header text, input colors, label contrast and selection inheritance. No user preferences, SQL or installed extensions are changed by these tests.

## 0.14.0 — 2026-10-22

- Fix mouse selection in the non-focusable completion popup. Hit-test the actual row, explicitly select its proposal and preserve editor focus; a single click selects, double click inserts that same row. Keyboard acceptance and asynchronous refresh retain the clicked choice.
- Add configurable SQL keyword completion, including SELECT, FROM, WHERE, JOIN variants, GROUP BY, ORDER BY and common condition keywords. Use a conservative context-specific list, literal prefixes, existing keyword casing and normal Tab acceptance. Standalone keywords need no database connection; quoted identifiers, strings, comments, table positions and qualified columns retain their own behavior.
- Add an independent, shared LIKE/NOT LIKE and IN/NOT IN setting. For a uniquely resolved simple column with a known type, propose `LIKE '%%'` on text, `IN ('', '')` on comparable text, or `IN ( ,)` on supported nontext scalar types. Place the caret inside the pattern/first value, replace only the operator, preserve existing RHS values and refuse ambiguous/unreadable/unknown types. Never execute edited SQL.
- Add routed WPF mouse-row, double-click/keyboard insertion, cursor, keyword-without-database, context/prefix-session reuse, metadata type, CTE/local/correlated/DML and independent-setting regressions. Metadata/SQL, installer and user's settings are not modified by tests.

## 0.13.0 — 2026-10-21

- Add bounded, cached name matching for missing/extra/substituted letters and adjacent transpositions, including incomplete prefixes. Recognize camelCase/snake_case word starts and retain literal local-object markers and explicit schemas. Compare logical name lengths without `_tb`/`_vw`, while inserting the original catalog name.
- Add **Impostazioni → Ricerca** with Precisa, Equilibrata (default) and Tollerante modes. Balanced corrections require at least four alphanumeric characters and are hidden when literal names/prefixes exist; tolerant mode starts at three and retains corrections below direct matches. Longer prefixes allow at most two edits. Exact/prefix results always precede corrections.
- Strengthen query-context ordering for current references, local/CTE sources and FK JOIN proposals. Search references by both the existing alias and original table/CTE name, insert the alias already used, and show the original table/schema beside homonymous columns. Qualified column searches stay within the resolved source; no fuzzy qualifier/schema binding or invented metadata.
- Learn accepted choices locally per server/database, prefix and candidate identity. Store bounded SHA-256 identifiers, counts and UTC timestamps in choices.json, with age decay, atomic/background writes and reset-generation coordination between SSMS instances. History only breaks comparable text/context ties, can be disabled and has an explicit reset button; no SQL or credentials are stored/transmitted.
- Highlight matched characters in theme-inheriting suggestion rows and label possible corrections. Retain explicit keyboard/mouse selection across prefix edits while it remains valid. Match/rank off the UI thread, cancel obsolete work, recheck query/connection before presentation and preserve exact Tab insertion semantics.
- Add synthetic matcher/distance, context, history persistence/reset/isolation, settings, WPF rendering, async stale-result and editor insertion regressions. No edited query, production database or installer is executed by tests.

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
