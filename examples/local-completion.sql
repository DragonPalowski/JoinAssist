-- Optional: run manually only in a disposable test session if you want to execute it.
-- JoinAssist itself does not execute this file. For completion, just open the script.
CREATE TABLE #Orders (
    OrderId int,
    ItemName nvarchar(100)
);
DECLARE @Items TABLE (
    ItemId int,
    ItemDescription nvarchar(200)
);
SELECT OrderId AS CopiedId, ItemName INTO #Copy FROM #Orders;

SELECT t.OrderId, t.ItemName FROM #Orders t;
SELECT i.ItemId, i.ItemDescription FROM @Items i;
SELECT c.CopiedId FROM #Copy c;

-- Try typing these progressively, accepting with Tab:
-- SELECT * FROM #Or
-- SELECT * FROM @It
-- SELECT t.Item FROM #Orders t
-- SELECT i.itdesc FROM @Items i
-- Exact/prefix matches stay ahead of broader matches.

-- For FK-role display, use real base tables with two declared foreign keys:
-- Orders.CreatedByUserId -> Users.Id and Orders.ModifiedByUserId -> Users.Id.
-- After Orders JOIN, each Users candidate shows its distinct ON condition.
-- No FK is invented for #Orders, @Items or the SELECT INTO copy.
