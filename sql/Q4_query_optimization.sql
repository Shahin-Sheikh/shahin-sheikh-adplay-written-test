-- Q4. Query optimization
--
-- ORIGINAL (slow, ~30s):
-- SELECT * FROM Orders o
-- JOIN Customers c ON c.Id = o.CustomerId
-- JOIN Products p ON p.Id = o.ProductId
-- WHERE YEAR(o.OrderDate) = 2026 AND MONTH(o.OrderDate) = 6
--   AND c.Country = 'BD'
-- ORDER BY o.OrderDate DESC;
--
-- WHY IT'S SLOW:
-- 1. YEAR(o.OrderDate) / MONTH(o.OrderDate) wrap the indexed column in a function.
--    This is "non-sargable" -> MySQL cannot use a B-Tree index on OrderDate and
--    must scan every row to evaluate the function.
-- 2. SELECT * pulls every column from three tables, defeating any covering index
--    and increasing I/O and network payload.
-- 3. No explicit indexes are guaranteed on Customers.Country or the join columns.

-- REWRITTEN (index-friendly, sargable range predicate):
SELECT
    o.Id, o.OrderDate, o.Amount, o.Status,
    c.Id AS CustomerId, c.Name AS CustomerName, c.Country,
    p.Id AS ProductId, p.Name AS ProductName
FROM Orders o
JOIN Customers c ON c.Id = o.CustomerId
JOIN Products p ON p.Id = o.ProductId
WHERE o.OrderDate >= '2026-06-01'
  AND o.OrderDate <  '2026-07-01'   -- half-open range instead of YEAR()/MONTH()
  AND c.Country = 'BD'
ORDER BY o.OrderDate DESC;

-- RECOMMENDED INDEXES:

-- 1. Supports the date range filter + ORDER BY OrderDate DESC in one pass,
--    and lets the optimizer use CustomerId to narrow further before the join.
CREATE INDEX IX_Orders_OrderDate_CustomerId
    ON Orders (OrderDate DESC, CustomerId);

-- 2. Speeds up the Customers.Country filter (and the join back to Orders).
CREATE INDEX IX_Customers_Country
    ON Customers (Country);

-- 3. Foreign-key lookup index for the Products join (often missing by default).
CREATE INDEX IX_Orders_ProductId
    ON Orders (ProductId);

-- ADDITIONAL NOTES:
-- - Replacing SELECT * with only the needed columns lets MySQL potentially use a
--   covering index and avoids fetching unused columns from Products/Customers.
-- - If this report runs often, consider a generated/virtual column
--   (e.g. OrderYearMonth) with its own index as an alternative to a date range,
--   or partition the Orders table by month if the table is very large.
-- - Verify the plan with EXPLAIN ANALYZE after adding indexes to confirm MySQL
--   switches from a full table scan to an index range scan.
