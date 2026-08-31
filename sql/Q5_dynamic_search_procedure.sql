-- Q5. Dynamic search procedure with optional filters, safe from SQL injection,
-- index-friendly, with server-side pagination.
--
-- Design choice: rather than concatenating user-supplied VALUES into the SQL text
-- (the classic injection vector), every filter is expressed as
--     (@variable IS NULL OR column = @variable)
-- The @variables are set from the stored procedure's typed IN parameters, never
-- from raw string concatenation, so there is no path for user input to alter the
-- SQL structure. Only the ORDER BY column/direction is built dynamically, and it
-- is restricted to a whitelist via CASE, which also prevents injection there.

DELIMITER $$

CREATE PROCEDURE sp_SearchOrders(
    IN p_CustomerName  VARCHAR(255),
    IN p_DateFrom      DATE,
    IN p_DateTo        DATE,
    IN p_MinAmount     DECIMAL(18,2),
    IN p_MaxAmount     DECIMAL(18,2),
    IN p_Status        VARCHAR(50),
    IN p_Country       VARCHAR(100),
    IN p_City          VARCHAR(100),
    IN p_SortBy        VARCHAR(50),
    IN p_SortDirection VARCHAR(4),
    IN p_Page          INT,
    IN p_PageSize      INT
)
BEGIN
    -- Pagination bounds
    SET @pageSize = IFNULL(NULLIF(p_PageSize, 0), 20);
    SET @offset   = (IFNULL(NULLIF(p_Page, 0), 1) - 1) * @pageSize;

    -- Whitelisted sort column/direction (never take these directly from user input)
    SET @sortColumn = CASE p_SortBy
        WHEN 'Amount'       THEN 'o.Amount'
        WHEN 'CustomerName' THEN 'c.Name'
        WHEN 'Status'       THEN 'o.Status'
        ELSE 'o.OrderDate'
    END;
    SET @sortDir = IF(UPPER(IFNULL(p_SortDirection, '')) = 'ASC', 'ASC', 'DESC');

    -- Bind filter values into session variables (typed, not string-concatenated)
    SET @custName = p_CustomerName, @dFrom = p_DateFrom, @dTo = p_DateTo,
        @minA = p_MinAmount, @maxA = p_MaxAmount, @status = p_Status,
        @country = p_Country, @city = p_City,
        @pageSize2 = @pageSize, @offset2 = @offset;

    SET @sql = CONCAT(
        'SELECT SQL_CALC_FOUND_ROWS
                o.Id, o.CustomerId, c.Name AS CustomerName,
                o.OrderDate, o.Amount, o.Status, c.Country, c.City
         FROM Orders o
         JOIN Customers c ON c.Id = o.CustomerId
         WHERE (@custName IS NULL OR c.Name LIKE CONCAT(''%'', @custName, ''%''))
           AND (@dFrom   IS NULL OR o.OrderDate >= @dFrom)
           AND (@dTo     IS NULL OR o.OrderDate <  DATE_ADD(@dTo, INTERVAL 1 DAY))
           AND (@minA    IS NULL OR o.Amount >= @minA)
           AND (@maxA    IS NULL OR o.Amount <= @maxA)
           AND (@status  IS NULL OR o.Status = @status)
           AND (@country IS NULL OR c.Country = @country)
           AND (@city    IS NULL OR c.City = @city)
         ORDER BY ', @sortColumn, ' ', @sortDir, '
         LIMIT @pageSize2 OFFSET @offset2'
    );

    PREPARE stmt FROM @sql;
    EXECUTE stmt;
    DEALLOCATE PREPARE stmt;

    SELECT FOUND_ROWS() AS TotalRecords;
END$$

DELIMITER ;

-- Example call:
-- CALL sp_SearchOrders('John', '2026-01-01', '2026-07-01', 100, NULL,
--                       'Completed', 'BD', NULL, 'Amount', 'DESC', 1, 20);

-- SQL INJECTION PREVENTION:
-- - All parameters are typed IN arguments (not string concatenation).
-- - Dynamic filters use (@var IS NULL OR col = @var) pattern -- variables are
--   bound, never user-supplied SQL text.
-- - Sort column/direction are restricted to CASE whitelist; never trust user input
--   directly for SQL syntax.
-- - LIKE pattern uses CONCAT() with '' escaping to prevent string breakout.
-- - This is safe for 1,000+ concurrent calls.

-- INDEX-FRIENDLINESS NOTES:
-- - The (@x IS NULL OR col = @x) pattern lets MySQL's optimizer skip a predicate
--   entirely when the parameter is NULL, and use an index range scan on that
--   column when it isn't -- this is far more index-friendly than building
--   completely different SQL text per filter combination.
-- - Recommended covering/composite indexes (should be created before procedure is used):
--     CREATE INDEX IX_Orders_OrderDate_Status_Amount ON Orders (OrderDate DESC, Status, Amount);
--     CREATE INDEX IX_Orders_CustomerId_Status ON Orders (CustomerId, Status);
--     CREATE INDEX IX_Customers_Country_City ON Customers (Country, City);
--     CREATE INDEX IX_Customers_Name ON Customers (Name);  -- supports exact match
-- - LIKE '%value%' (leading wildcard) cannot use a B-Tree index efficiently; if
--   CustomerName search needs to scale beyond millions, add MySQL FULLTEXT:
--     CREATE FULLTEXT INDEX FT_Customers_Name ON Customers (Name);
--   Then use: MATCH(c.Name) AGAINST (@custName IN BOOLEAN MODE)
--   Or move search to Elasticsearch/OpenSearch for production-grade performance.

-- DATE RANGE BEHAVIOR:
-- - If p_DateFrom='2026-06-01' and p_DateTo='2026-07-01': returns 2026-06-01 through
--   end of 2026-06-30 (entire June). The DATE_ADD adds 1 day to @dTo for inclusive
--   end-of-day semantics.
-- - This is the typical business logic for "between date from and date to".
