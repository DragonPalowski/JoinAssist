# JOIN suggestions for temporary tables

From 0.24.0, **Impostazioni → Completamento → Proponi ON per tabelle temporanee (collegamenti ricavati, da verificare)** enables inferred equalities for local sources declared earlier in the same script. It is initially enabled and can be disabled independently of real FK completion. It supports CREATE TABLE #temp, DECLARE @rows TABLE and resolvable SELECT INTO definitions, whether the local is the current source or the JOIN target, including pairs of locals.

```sql
CREATE TABLE #Subset (Id int, TenantId int);

SELECT * FROM Orders
LEFT JOIN #Sub
-- Select an inferred proposal and press Tab after inspecting its ON.
```

## What is proposed

Candidates prefer a complete original primary key when every member maps through a direct SELECT INTO projection, including renamed columns. Real enabled origin FKs can also support an inferred equality when all column pairs map. These descriptive origins remain separate from the temp's constraint properties: SELECT INTO still does not copy PKs/FKs or guarantee that the temporary rows retain their original values/cardinality.

For arbitrary declared locals with no known origin, the engine can match a complete declared/source primary key by column name and compatible type. Lower-ranked alternatives compare individual same-name columns with compatible known types. It never combines every matching name into a single guessed business key. Inspect **Temporanea · … · da verificare · ON …** and choose the intended equality. An inferred candidate is not an enforced FK, an ownership rule or a promise of one-to-one results; duplicates may multiply rows.

Text lengths can differ within compatible families. Numeric families, textual families, binary/rowversion families, dates, time, bit and uniqueidentifier are supported conservatively. Unknown/user-defined types, xml, text/ntext/image, spatial types and sql_variant receive no guessed equality. Collation, implicit conversion, floating-point comparison and actual data/business meaning still need review. No CAST/COLLATE is inserted automatically.

## Availability and fallbacks

Start typing a target name after JOIN, or type a simple identifier after ON. Explicit manual completion remains available. Tab inserts the selected proposal; whitespace-only positions keep ordinary indentation. Ordinary name-only and column alternatives remain available. Metadata-backed acceptance rechecks the editor/connection; purely declared local pairs can work without a database connection. The normal FK-specific manual picker and DELETE planner continue to use real constraints only.

The extension analyzes definitions, not live tempdb rows or session state. A #temp created only in another tab, global ##temp, opaque dynamic SQL/procedure/conditional definitions, future declarations or invalidated shapes may be unavailable. DROP/ALTER and other existing local-definition invalidation rules still apply. Later data writes can change values even when the original column projection remains known. An arbitrary pair with no resolvable origin, same-name compatible columns or complete mapped key deliberately gets no guessed ON; write that relationship manually.

Work is bounded: up to 20 matching local and 20 persistent target names, 50 visible sources, 32 default common-column alternatives per pair, 1000 intermediate and 200 final inferred candidates. Continue the target prefix to focus a large catalog; a partial ON prefix focuses wide-table alternatives before the column bound. Generated SQL is never executed, and metadata reads do not send the edited SQL, create the temporary table or inspect/count its rows.
