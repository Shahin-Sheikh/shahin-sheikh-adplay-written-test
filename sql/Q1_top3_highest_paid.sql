-- Q1. Top 3 highest-paid employees in each department
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

