# Personal completion rules (0.16.0)

Open **JoinAssist → Impostazioni… → Regole**. There are no built-in active rules. Create, edit, duplicate, enable/disable or delete rules, then **Salva** the settings to activate the draft. Closing without accepting its draft leaves active rules unchanged. Duplicates keep a fresh identity and disable automatic Space application.

## SELECT rules

Choose **Nuova SELECT** and enter schema and table/view in separate fields, without SQL brackets. For the log example:

| Field | Value |
| --- | --- |
| Name | Ultimi log |
| Schema | logs |
| Table/view | logs_tb |
| TOP | 100 |
| Order | Prima colonna (ORDER BY 1) |
| Descending | Enabled |

While completing this table in a simple SELECT, a labelled personal choice appears alongside the ordinary table name. Its full preview shows what accepting it inserts:

```sql
select TOP 100 * from logs.logs_tb ORDER BY 1 DESC
```

An ordinary table choice still inserts only its name. After typing a complete table followed by whitespace, the personal choice is also available. Use Tab (or Enter if enabled). Generated keyword case follows **Formato SQL**; existing text retains its case. The change is one editor edit, undoable with Ctrl+Z. Nothing is executed.

TOP is added only when absent; 0 omits it. ORDER BY is added only when absent; choose no ordering, ordinal 1, or a specific column. A named column must be present in catalog metadata; **Carica colonne dalla connessione corrente** reads those names without inspecting rows. Existing TOP and ORDER BY win; if no change is needed the rule is not offered. With SELECT *, ORDER BY 1 means the first output column, and can change meaning if the table schema changes.

Transforms require a strict simple root `SELECT * FROM ...` with one table/view in the current database. WHERE, a table alias, trailing comments, semicolons and later statements are preserved. JOIN, CTE, UNION, DISTINCT, SELECT INTO, explicit projections, nested SELECT targets, GROUP/HAVING, FOR and query hints are excluded. Unknown, ambiguous, local, synonym and table-valued-function targets do not receive table rules. Complete manually typed table names retain their spelling; normal table completion keeps required quoting/schema behavior. No global SQL find-and-replace is performed.

## SQL shortcuts

Choose **Nuova scorciatoia**, enter a word such as `ultimilog`, and literal SQL:

```sql
SELECT TOP 100 * FROM logs.logs_tb
WHERE message LIKE '%$cursor$%'
ORDER BY 1 DESC;
```

At the start of an instruction, type the shortcut or its prefix and accept its proposal. It replaces only that word, removes `$cursor$`, and puts the caret there. Without the marker the caret goes to the end. There is no variable substitution, scripting engine or automatic execution. Shortcuts start with a letter and contain letters, digits or underscore. They work at the beginning of editor text or after `;`/`GO` (comments are ignored), outside strings/comments. A preceding unterminated statement needs a separator. Global shortcuts work without a database connection; scoped ones require a current connection identity.

## Scope and optional Space application

Server and database fields are optional: empty means any. **Usa connessione corrente** fills both from the active query. A newly created rule uses that scope when a connection is available; it can be cleared. Server comparison ignores case; database identity is exact. There are no wildcard/regex scope expressions. For the same resolved table or shortcut, rules specifying more scope fields override broader ones. Equally specific rules remain separate choices, even if their SQL is identical.

By default rules require accepting a proposal. Explicitly enable **Applica anche con Spazio** per rule to expand after typing the complete table/shortcut and one space. This requires automatic completion on, an exact target and only one personal choice. Prefixes do not expand; multiple alternatives block automatic expansion. Merely opening suggestions, moving the cursor or pasting a whole query does not trigger it; a pasted single space is indistinguishable from an inserted space. Further edits/cursor movement while metadata loads cancel the pending action. Existing query/connection/configuration checks still apply before insertion.

## Local files and limits

Rules are stored in `%LOCALAPPDATA%/JoinAssist/preferences.json` with other settings. **Esporta…** writes JSON containing configured SQL and raw scope/object names; review it before sharing. **Importa…** validates the file, generates fresh IDs and imports rules disabled with Space off. Review and enable them, then save settings. The extension never uploads rules or includes them in diagnostics.

Limits: 50 rules, 100-character display names, 64-character shortcuts, 8,192 characters per snippet, 128 KiB total snippet characters and 1 MiB imported/preferences file size. Rules do not execute edited SQL or read business rows. See [privacy](PRIVACY.md).
