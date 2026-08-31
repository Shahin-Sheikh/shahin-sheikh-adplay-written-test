using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace AdPlay.Api.Data
{
    // Q8. Generic repository: dynamic filtering, dynamic includes, dynamic sorting,
    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    }

    public interface IGenericRepository<T> where T : class
    {
        Task<PagedResult<TResult>> FindAsync<TResult>(
            Expression<Func<T, bool>>? filter = null,
            Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
            Expression<Func<T, TResult>>? select = null,
            string? includeProperties = null,
            int page = 1,
            int pageSize = 20);
    }

    public class GenericRepository<T> : IGenericRepository<T> where T : class
    {
        private readonly DbContext _context;
        private readonly DbSet<T> _dbSet;

        public GenericRepository(DbContext context)
        {
            _context = context;
            _dbSet = context.Set<T>();
        }

        public async Task<PagedResult<TResult>> FindAsync<TResult>(
            Expression<Func<T, bool>>? filter = null,
            Func<IQueryable<T>, IOrderedQueryable<T>>? orderBy = null,
            Expression<Func<T, TResult>>? select = null,
            string? includeProperties = null,
            int page = 1,
            int pageSize = 20)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 200);

            IQueryable<T> query = _dbSet.AsNoTracking();

            // Dynamic filtering
            if (filter != null)
                query = query.Where(filter);

            if (!string.IsNullOrWhiteSpace(includeProperties))
            {
                foreach (var includeProperty in includeProperties.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    query = query.Include(includeProperty.Trim());
            }

            var totalCount = await query.CountAsync();

            // Dynamic sorting
            if (orderBy != null)
                query = orderBy(query);

            var pagedQuery = query
                .Skip((page - 1) * pageSize)
                .Take(pageSize);

            // Projection to a DTO to avoid over-fetching columns.
            List<TResult> items;
            if (select != null)
            {
                items = await pagedQuery.Select(select).ToListAsync();
            }
            else
            {
                // Only valid if TResult is assignable from T
                if (!typeof(TResult).IsAssignableFrom(typeof(T)))
                    throw new InvalidOperationException(
                        $"Cannot project {typeof(T).Name} to {typeof(TResult).Name} without an explicit select expression. " +
                        $"Provide a select expression via the 'select' parameter.");

                items = await pagedQuery.Cast<TResult>().ToListAsync();
            }

            return new PagedResult<TResult>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }
    }
}