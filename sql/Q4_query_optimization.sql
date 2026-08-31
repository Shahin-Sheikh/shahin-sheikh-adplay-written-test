-- Q4. Query optimization
SELECT
    o.Id, o.OrderDate, o.Amount, o.Status,
    c.Id AS CustomerId, c.Name AS CustomerName, c.Country,
    p.Id AS ProductId, p.Name AS ProductName
FROM Orders o
JOIN Customers c ON c.Id = o.CustomerId
JOIN Products p ON p.Id = o.ProductId
WHERE o.OrderDate >= '2026-06-01'
  AND o.OrderDate <  '2026-07-01' 
  AND c.Country = 'BD'
ORDER BY o.OrderDate DESC;


CREATE INDEX IX_Orders_OrderDate_CustomerId
    ON Orders (OrderDate DESC, CustomerId);

-- 2. Speeds up the Customers.Country filter (and the join back to Orders).
CREATE INDEX IX_Customers_Country
    ON Customers (Country);

-- 3. Foreign-key lookup index for the Products join (often missing by default).
CREATE INDEX IX_Orders_ProductId
    ON Orders (ProductId);
