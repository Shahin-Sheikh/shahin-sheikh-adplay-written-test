-- Q2. Users who logged in for 7+ consecutive days
WITH DistinctLogins AS (
    SELECT DISTINCT UserId, LoginDate
    FROM UserLogin
),
LoginRanks AS (
    SELECT
        UserId,
        LoginDate,
        ROW_NUMBER() OVER (PARTITION BY UserId ORDER BY LoginDate) AS rn
    FROM DistinctLogins
),
Groups AS (
    SELECT
        UserId,
        LoginDate,
        DATE_SUB(LoginDate, INTERVAL rn DAY) AS GroupKey
    FROM LoginRanks
),
Streaks AS (
    SELECT
        UserId,
        GroupKey,
        MIN(LoginDate) AS StartDate,
        MAX(LoginDate) AS EndDate,
        COUNT(*)       AS StreakLength
    FROM Groups
    GROUP BY UserId, GroupKey
)
SELECT UserId, StartDate, EndDate
FROM Streaks
WHERE StreakLength >= 7
ORDER BY UserId, StartDate;
