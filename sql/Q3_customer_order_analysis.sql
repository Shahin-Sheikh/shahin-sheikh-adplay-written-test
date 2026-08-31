-- Q3. For each order: current amount, previous amount, difference, running total per customer.
SELECT
    Id,
    CustomerId,
    OrderDate,
    Amount AS CurrentOrderAmount,
    LAG(Amount) OVER (PARTITION BY CustomerId ORDER BY OrderDate) AS PreviousOrderAmount,
    Amount - LAG(Amount) OVER (PARTITION BY CustomerId ORDER BY OrderDate) AS AmountDifference,
    SUM(Amount) OVER (
        PARTITION BY CustomerId
        ORDER BY OrderDate
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    ) AS RunningTotal
FROM Orders
ORDER BY CustomerId, OrderDate;
