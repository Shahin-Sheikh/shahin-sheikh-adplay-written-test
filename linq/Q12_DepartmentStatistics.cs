using System;
using System.Collections.Generic;
using System.Linq;

namespace AdPlay.Api.Linq
{
    public class Employee
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;
        public int DepartmentId { get; set; }
        public decimal Salary { get; set; }
    }

    public class DepartmentStatistics
    {
        public int DepartmentId { get; set; }
        public decimal HighestSalary { get; set; }
        public decimal LowestSalary { get; set; }
        public decimal AverageSalary { get; set; }
        public decimal MedianSalary { get; set; }
    }

    public static class Q12_DepartmentStatisticsExample
    {
        // Q12. LINQ has no built-in Median, so it's computed manually inside the
        // Select projection: sort each department's salaries, then take the
        // middle value (or the average of the two middle values for an even count).
        public static List<DepartmentStatistics> Summarize(List<Employee> employees)
        {
            return employees
                .GroupBy(e => e.DepartmentId)
                .Select(g =>
                {
                    var salaries = g.Select(e => e.Salary).OrderBy(s => s).ToList();
                    var count = salaries.Count;
                    var median = count % 2 == 0
                        ? (salaries[count / 2 - 1] + salaries[count / 2]) / 2m
                        : salaries[count / 2];

                    return new DepartmentStatistics
                    {
                        DepartmentId = g.Key,
                        HighestSalary = salaries.Max(),
                        LowestSalary = salaries.Min(),
                        AverageSalary = salaries.Average(),
                        MedianSalary = median
                    };
                })
                .ToList();
        }
    }
}
