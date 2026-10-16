# Changelog

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
