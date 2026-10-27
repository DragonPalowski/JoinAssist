# Productivity workspace — 0.17.0

Open **JoinAssist → Azioni rapide…** from an SQL query tab. Search the action list, choose the source object where relevant, fill the options and prepare an SQL preview. Existing automatic completion, JOIN insertion, typing templates and settings remain available. Ctrl+Alt+P is an optional checkbox in **Impostazioni → Completamento**, off initially.

## Applying a preview

- **Inserisci anteprima** replaces the captured selection or inserts at the captured caret. Alias rename explicitly previews/replaces the entire script. Each action uses one initial editor edit and supports Ctrl+Z.
- Changes to text, caret, selection, active SQL view or captured connection invalidate insertion. Use **Aggiorna dalla query** and regenerate the preview. In keep-open mode, refresh before another insertion into the changed query.
- **Copia SQL** explicitly copies the preview. **Nuova scheda (salva .sql…)** writes the preview to a path selected by the user, then opens that SQL file in SSMS. Check the new tab's connection/database before executing. Inline field navigation applies to insertion in the captured editor, not an independently opened file.
- **Mantieni aperto** leaves a modeless window that can be positioned beside the editor. It is an optional separate window, not a docked SSMS tool window or per-query toolbar.
- Generated SQL is never executed. Metadata operations read system catalogs through the captured metadata connection, with bounded operations/timeouts. No row counts or business results are inspected.

## Available actions

| Action | Behavior |
| --- | --- |
| Record collegati | Pick a target/path, source filter column and value. Prepare TOP 100 target rows using correlated EXISTS, avoiding row multiplication. Empty value produces a parameter to fill. Batch mode prepares at most 30 paths, possibly multiple queries for one target via different relationships. |
| Elenco IN dagli appunti | Click Leggi appunti or paste into the text field. One column, one value per line, no header, up to 1,000 nonempty rows / 128 KiB. Choose text/numeric/GUID/date type and optional deduplication. Text preserves leading zeros and escapes apostrophes; text `NULL` stays quoted text. To test SQL NULL use IS NULL separately. Numeric types reject injected text, invalid fractions/ranges and locale comma decimals. |
| Scegli più colonne | Search/check columns of visible SQL sources, with existing aliases, or the chosen catalog object when there is no resolved source. Hidden selections remain selected; source order is preserved. The preview is a column list to insert at the desired projection position. |
| Modello con campi | Prepare literal SQL with named editable fields. See the syntax below. |
| Filtro temporale | Pick a date column, local-server or UTC clock, and today/yesterday/last seven days/current month/explicit range. Generate `>= start AND < end` without converting the filtered column. Explicit range uses parameters; the final date/time is exclusive. Choose the clock matching how the column stores timestamps. |
| Percorso JOIN e cardinalità | Pick a target and path up to five FK edges. Preview a standalone SELECT including source and final-target columns, all composite equalities and per-edge uniqueness notes. It inserts a complete draft, not another automatic JOIN into the current FROM. |
| Dove viene usato? | Display up to 500 dependency hits registered in sys.sql_expression_dependencies. Dynamic SQL, external/unresolved references, column-level completeness and objects hidden by permissions may be absent. |
| Cerca colonne nello schema | Parameterized catalog search for column-name fragments, up to 500 hits. |
| Query di controllo | Choose columns for duplicates, NULL checks, counts per value/day, or an outgoing FK for orphan checks. Composite FK orphan checks exclude nullable keys according to ordinary SQL Server FK matching. |
| Preferiti per database | Explicitly save selected/current SQL with name, category and description, scoped to current server/database or globally. Search, update, prepare or delete entries. Restore-last-delete remains available while that action panel stays open. Concurrent changes require reopening the favorites panel before saving. |
| Schema affiancabile | Read columns and incoming/outgoing FK/cardinality notes, then keep the window open beside SSMS. Refresh explicitly after source/schema changes. |
| Rinomina alias | Place the cursor in a SELECT scope of a complete query and choose an alias of a named table/view/CTE source. Preview the whole script. Update qualified columns/stars and resolvable correlated references; preserve strings, comments and shadowing inner aliases. DML target aliases, collisions anywhere in the script and unsupported alias declarations are refused conservatively. |
| Prova una regola | Simulate the chosen rule/example without changing active preferences or editor text. Explain scope, disabled status, missing metadata/order column, competing scopes, unsupported SELECT shape and existing TOP/order. The rules settings page also has Prova… for drafts. |
| Diagnostica e recupero | Read local context/timing, use Riprova suggerimenti to recreate a failed session, or Aggiorna metadati to invalidate the cache. The existing diagnostic dialog also has these recovery buttons; copied diagnostics stay sanitized. |

## Linked fields

```sql
SELECT TOP {{limite:100}} *
FROM logs.logs_tb
WHERE entityId = {{id:0}} OR parentEntityId = {{id}}
ORDER BY 1 DESC;
```

The first occurrence sets a field's default. Repeated names share it. After inserting, Tab moves to the next distinct field, Shift+Tab moves back and Escape exits. Editing the active field mirrors its other occurrences using tracking spans; edits outside the active field cancel the session. Fields are literal SQL text: they do not infer types, quote/escape typed text or create/execute SQL parameters. Review the resulting query normally.

In personal shortcuts, explicitly enable **Abilita campi collegati** in the rule editor. It starts off so existing rules containing literal braces keep their behavior. The dedicated workflow model action and favorite preparation support named fields directly. Generated drafts follow keyword-case preferences; literal templates, favorites and existing text in alias refactoring retain their own case.

Limits: 20 distinct fields and 100 occurrences per model; 64 KiB model text (personal shortcuts retain their 8,192-character limit). Personal rules accept either linked fields or the legacy `$cursor$` marker, not both. Existing shortcuts without fields keep their behavior.

## Bounds and interpretation

FK exploration follows visible enabled constraints only, excludes repeated-table/cyclic paths and does not invent relationships from column names. Self-referential and repeated-table traversals are not offered. The search caps depth at five, alternatives at 30 and exploration at 10,000 edges; wide searches may need a smaller depth. The full graph reader caps FK pairs at 20,000 and unique-index columns at 50,000. Unfiltered enabled unique-index metadata supports cardinality notes; 0..N means multiplication is possible, not observed. Untrusted FK warnings remain visible. Permissions can hide metadata.

Clipboard empty lines are skipped; a tab-separated row is refused as multi-column input. Generated previews are limited to 256 KiB. Favorites allow 200 entries / 64 KiB per query / 512 KiB total SQL and are stored locally in plain text with configured scopes. Saving/opening a SQL file is explicit. See [privacy](PRIVACY.md).

## Validation

Core tests use synthetic schemas and strict SQL parsing. Metadata reader tests allow SELECTs on system catalogs only. WPF tests build each action, check previews and dark/light themes without interacting with the clipboard or installed SSMS. Simulated editor tests verify exact replacement, initial single-edit insertion, stale refusal, linked-field growth/mirroring and navigation. These are not live SSMS integration tests; driver/host changes can still affect the extension.
