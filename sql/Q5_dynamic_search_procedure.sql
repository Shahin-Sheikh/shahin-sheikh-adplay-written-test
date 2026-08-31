-- Q5. Dynamic search procedure with optional filters, safe from SQL injection
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
