# Changelog

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
