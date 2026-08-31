-- Q1. Top 3 highest-paid employees in each department
-- Uses a window function (ROW_NUMBER) partitioned by department, single CTE, no LIMIT.

WITH RankedEmployees AS (
    SELECT
        Id,
        Name,
        DepartmentId,
        Salary,
        JoiningDate,
        ROW_NUMBER() OVER (PARTITION BY DepartmentId ORDER BY Salary DESC) AS SalaryRank
    FROM Employee
)
SELECT Id, Name, DepartmentId, Salary, JoiningDate
FROM RankedEmployees
WHERE SalaryRank <= 3
ORDER BY DepartmentId, SalaryRank;

-- Note: ROW_NUMBER() gives exactly 3 rows per department even on salary ties.
-- If tied salaries should all be included as "3rd place", use RANK() instead:
--   RANK() OVER (PARTITION BY DepartmentId ORDER BY Salary DESC) AS SalaryRank
