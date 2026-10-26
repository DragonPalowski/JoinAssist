# Table and column search (0.13.0)

JoinAssist matches real visible metadata, CTE/derived output names and script-local definitions. It does not invent tables, columns or relationships. Matching is local and uses no AI or search service.

## Ten improvements

1. **Typing errors:** omitted, extra or substituted letters and adjacent swaps, such as `patietns` → `patients_tb` and `idPrescripton` → `idPrescription`.
2. **Incomplete names:** compare a typed prefix against the beginning of a candidate word without charging for its untyped suffix. For example `prescir` can find `prescriptions_tb`.
3. **Word structure:** recognize camelCase, acronyms and snake_case. `presc_det` can find `prescriptionItemDetails_tb`; word-prefix abbreviations precede scattered-letter abbreviations. SQL whitespace still separates SQL tokens/aliases: use a compact abbreviation or an unfinished delimited name such as `[presc det` for a multiword search, rather than treating arbitrary SQL tokens as one name.
4. **Logical proximity:** exact names first, then literal prefixes, word-boundary/internal fragments, separator-folded matches, word/scattered abbreviations and corrections. Ignore conventional `_tb`/`_vw` suffix length for direct-prefix proximity, retaining the actual inserted name. `patie` favors `patients_tb` over `patientAppointmentHistory_vw`.
5. **Query context:** SELECT/WHERE expressions use their visible sources; qualified columns use their resolved source only. Within a text-quality band, local scopes, active ON sources and actual FK proposals receive context priority. FROM offers catalog names and available local/CTE definitions. Context never overrides an exact name with a typo guess.
6. **Aliases:** with `FROM Orders p`, typing `Ord` in an expression can find reference `p`. Its detail shows `dbo.Orders`; accepting it inserts `p`, preserving the query's binding. Works for CTE/derived references too.
7. **Homonyms:** keep columns such as `o.Id` and `i.Id` as separate suggestions, show alias/original source and metadata, and qualify insertions where needed. No guessing between identically named sources.
8. **Local choice memory:** remember accepted candidates per server/database and exact typed prefix. Frequency is a bounded tie-breaker among similar text/context results. Exact names and stronger match groups remain ahead. No learning from merely showing/navigating a proposal or from cancelled/stale edits.
9. **Readable, stable suggestions:** bold/underline matching characters, label `possibile correzione`, preserve explicit arrow/mouse selection when further typing still admits that candidate. Automatic selection follows the best match; a candidate that no longer matches is dropped. Tab accepts the visible selected candidate after the existing query/connection checks. The editor never silently corrects text.
10. **Search preferences:** **JoinAssist → Impostazioni… → Ricerca** controls tolerance and local choice memory and can reset all remembered choices immediately. Save persists preference changes; reset is an immediate action even if the dialog is later cancelled.

From 0.15.2, a written terminal naming suffix `_vw` or `_tb` constrains abbreviated matches to the same suffix. For example `pre_vw` compares `pre` to candidate stems and puts `prescriptions_vw` ahead of longer or scattered-letter matches. The suffix is a naming convention, not a replacement for catalog object-kind metadata. Actual full literal names and literal prefixes retain their existing priority. Typo tolerance applies to the stem, and highlighting includes the literal suffix without changing the inserted name; schemas and local markers remain literal.

## Tolerance

| Mode | Behavior |
| --- | --- |
| Precisa | Literal names/fragments, separator folding and word-prefix abbreviations; no edit-distance guesses or scattered-letter matches. |
| Equilibrata (default) | Also accepts compact scattered abbreviations. Typo guesses start at four alphanumeric characters, allow one edit (two from nine characters), and appear only when no literal name/prefix exists. |
| Tollerante | Also shows corrections below direct matches. One edit from three characters; two from seven. Three/four-character corrections must retain the first character. |

Schemas, `#`/`##`/`@` markers and qualifier bindings remain literal. Typo tolerance applies to the name currently being completed, not to a misspelled qualifier to its left. It does not correct SQL keywords or parse arbitrary natural language. Broad matches may not express your intention: inspect and explicitly accept a candidate.

## Local memory and performance

`%LOCALAPPDATA%/JoinAssist/choices.json` contains only hashed scope/prefix/candidate identifiers, capped counts, UTC timestamps and a reset generation. It does not contain query text, raw names, connection strings or credentials. Hashes are local identifiers, not a claim of irreversible anonymization. There are at most 2,048 entries; loading discards entries older than 180 days and ranking reduces the benefit as choices age. Disabling memory stops its use and future recording; use reset to remove saved choices.

Persistence is best effort and happens in background. Separate SSMS processes merge bounded snapshots conservatively rather than maintaining an exact event log. Reset generations prevent a stale process from restoring cleared history on its next save; other already-running processes can retain their old ranking hints until that synchronization or restart. File problems do not stop completion. A final choice immediately before process exit may not be saved.

Name profiles and match results use bounded process caches. Typo distance uses a narrow edit budget, oversized identifiers are rejected, and catalogue matching/ranking runs off the editor UI thread with cancellation and stale snapshot/connection checks. This is not a guarantee that arbitrary SQL syntax can be recovered or that unresolved metadata is available.

Tests use synthetic metadata and disposable history files, cover reference distance equivalence, exact/context/history priorities, aliases, homonyms, reset races, rendered highlighting and editor selection/insertion. Actual typing in every SSMS/theme/authentication combination remains outside the automated suite.
