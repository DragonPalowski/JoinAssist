# Mouse, keywords and filter templates (0.14.0)

## Mouse

A single left click selects the actual suggestion row. Tab/Enter accepts it; a double left click inserts that row immediately. Clicking the popup scrollbar does not choose a different suggestion. Rows remain non-focusable so typing stays in the SQL editor. A clicked choice survives ranking refreshes while it remains a valid match. Query/connection checks still apply before insertion.

## Settings

**JoinAssist → Impostazioni… → Completamento** has two independent options, enabled by default:

- **Completa le parole chiave SQL con Tab (SELECT, FROM, WHERE…)**.
- **Prepara LIKE / NOT LIKE e IN / NOT IN con Tab, in base al tipo**.

They apply to all query tabs and persist in preferences.json. **Formato SQL → Scrivi le parole chiave generate in MAIUSCOLO** determines generated keyword casing. The existing Enter-acceptance setting applies too; Tab is always available for the visible selected suggestion. These features do not execute queries.

## Keyword completion

For example `sel` → `SELECT `, `SELECT * fr` → `SELECT * FROM `, and `SELECT * FROM Orders wh` → `SELECT * FROM Orders WHERE `. Accept the visible proposal with Tab. A context-specific list includes common SELECT/FROM/filter/JOIN/group/order and DML keywords/phrases; this is a typing aid, not a complete SQL grammar validator.

The current token is replaced in full, including its suffix when editing in the middle. Existing following whitespace is retained instead of adding another space. Keywords match literal prefixes; table/column typo correction does not apply to keywords. Strings, comments, quoted identifiers, qualified column positions and expected table-name positions do not get competing keyword replacements. Standalone keyword suggestions work without reading SQL metadata or capturing a database connection. Mixed expression suggestions retain ordinary column precedence within a match group.

## LIKE and IN

Write a simple column reference followed by the operator, then accept the generated proposal with Tab. From 0.15.2, prefixes such as `li`, `lik` and `not li` can directly offer the complete type-checked model: one Tab inserts `LIKE '%%'` or `NOT LIKE '%%'`, rather than only completing the word and requiring another request. A `no`/`not` prefix can offer NOT LIKE and NOT IN, depending on the known type. The row title and selected preview show the full structure before acceptance.

| Known column type | Typed operator | Inserted operator/value structure |
| --- | --- | --- |
| Text | LIKE / NOT LIKE | `LIKE '%%'` / `NOT LIKE '%%'` |
| Comparable text | IN / NOT IN | `IN ('', '')` / `NOT IN ('', '')` |
| Supported nontext scalar | IN / NOT IN | `IN ( ,)` / `NOT IN ( ,)` |
| Nontext | LIKE / NOT LIKE | No filter template |
| Unknown or ambiguous | Any of these | No filter template |

The column reference and following clauses/statements are preserved. The caret goes between the two percent signs, inside the first pair of quotes, or into the first unquoted value slot. `IN` creates two editable slots, not a completed executable list. For dates, GUIDs, binary values or expressions, fill the slots using appropriate SQL literals/expressions yourself. Empty text values are placeholders too.

Supported LIKE text types: char, varchar, nchar, nvarchar, sysname, text and ntext. IN excludes legacy text/ntext and non-comparable XML/spatial or unresolved user-defined types. Supported nontext scalars include numbers, bit, temporal scalars, uniqueidentifier and binary/rowversion types. Metadata visibility and conservative static type inference determine availability; a type is never guessed from the column name or business rows.

Aliases, schemas, resolvable CTE/derived columns, local declarations, correlated scopes and ordinary DELETE/UPDATE filters can participate. The first version handles simple column references, not arbitrary functions/expressions on the left. Ambiguous unqualified names, unreadable competing sources and shadowed aliases are refused. Schemas and qualifier bindings remain literal.

An existing pattern, parameter, list or subquery to the right is not overwritten. Comments inside a multiword operator prevent bulk replacement. Typing inside a string/comment remains silent. For partial operators with no eligible type-checked model, ordinary keyword alternatives remain available when keyword completion is enabled; they insert only the word/phrase. A plain operator keyword can complete without a metadata connection, while a typed model still requires a current connection and known, unambiguous type. Otherwise Tab retains the normal editor behavior; nothing is inferred or executed.

## Verification

Tests use fictitious names and metadata, generated non-focusable WPF list rows and routed mouse events, not just direct assignment to SelectedItem. They check clicked-row identity, keyboard/double-click insertion, asynchronous refresh, exact replacement spans and caret placement. Keyword tests run without a database; filter tests query a simulated metadata provider only. Live mouse/typing behavior in the user's SSMS session remains to be verified after updating.
