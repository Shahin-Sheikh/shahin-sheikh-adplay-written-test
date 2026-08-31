using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace AdPlay.Api.Data
{
    // Q9. Dynamic sorting extension method using Expression Trees.
    public static class DynamicSortExtensions
    {
        public static IOrderedQueryable<T> OrderByDynamic<T>(this IQueryable<T> source, string sortExpression)
        {
            if (string.IsNullOrWhiteSpace(sortExpression))
                throw new ArgumentException("Sort expression cannot be empty.", nameof(sortExpression));

            // Prevent DOS attacks with overly long or too many sort expressions
            if (sortExpression.Length > 500)
                throw new ArgumentException("Sort expression is too long (max 500 characters).", nameof(sortExpression));

            var fields = sortExpression.Split(',', StringSplitOptions.RemoveEmptyEntries);
            
            if (fields.Length == 0)
                throw new ArgumentException("No valid sort fields provided.", nameof(sortExpression));

            if (fields.Length > 5)
                throw new ArgumentException("Maximum 5 sort columns allowed.", nameof(sortExpression));

            IOrderedQueryable<T>? ordered = null;

            foreach (var field in fields)
            {
                ordered = ordered == null
                    ? ApplyOrder(source, field.Trim(), useThenBy: false)
                    : ApplyOrder(ordered, field.Trim(), useThenBy: true);
            }

            return ordered ?? throw new ArgumentException("No valid sort fields provided.");
        }

        private static IOrderedQueryable<T> ApplyOrder<T>(IQueryable<T> source, string fieldExpression, bool useThenBy)
        {
            var parts = fieldExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var propertyPath = parts[0];
            var descending = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);

            var parameter = Expression.Parameter(typeof(T), "x");

            // Supports nested properties like "Department.Name"
            Expression propertyAccess = parameter;
            Type propertyType = typeof(T);
            foreach (var propertyName in propertyPath.Split('.'))
            {
                var property = propertyType.GetProperty(propertyName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                    ?? throw new ArgumentException($"Property '{propertyName}' not found on type {propertyType.Name}.");

                propertyAccess = Expression.MakeMemberAccess(propertyAccess, property);
                propertyType = property.PropertyType;
            }

            var lambda = Expression.Lambda(propertyAccess, parameter);

            var methodName = useThenBy
                ? (descending ? "ThenByDescending" : "ThenBy")
                : (descending ? "OrderByDescending" : "OrderBy");

            var resultExpression = Expression.Call(
                typeof(Queryable),
                methodName,
                new[] { typeof(T), propertyType },
                source.Expression,
                Expression.Quote(lambda));

            return (IOrderedQueryable<T>)source.Provider.CreateQuery<T>(resultExpression);
        }
    }
}
