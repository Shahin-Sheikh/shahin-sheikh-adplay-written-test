using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdPlay.Api.Controllers
{
    public class ProductDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;
        public decimal Price { get; set; }
        public string CategoryName { get; set; } = default!;
        public string BrandName { get; set; } = default!;
    }

    // Q13. GET /api/products
    [ApiController]
    [Route("api/products")]
    public class ProductsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ProductsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetProducts(
            [FromQuery] string? keyword,
            [FromQuery] decimal? minPrice,
            [FromQuery] decimal? maxPrice,
            [FromQuery] List<int>? categoryIds,
            [FromQuery] List<int>? brandIds,
            [FromQuery] string sortBy = "Name",
            [FromQuery] string sortDirection = "asc",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            // Input validation: prevent DOS and invalid queries
            pageSize = Math.Clamp(pageSize, 1, 100);
            page = Math.Max(page, 1);

            // Validate price range
            if (minPrice.HasValue && maxPrice.HasValue && minPrice > maxPrice)
                return BadRequest(new { error = "minPrice cannot be greater than maxPrice." });

            if (minPrice.HasValue && minPrice < 0)
                return BadRequest(new { error = "minPrice cannot be negative." });

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                keyword = keyword.Trim();
                if (keyword.Length > 200)
                    return BadRequest(new { error = "Search keyword must be 200 characters or less." });

                if (keyword.Count(c => c == '%' || c == '_') > 3)
                    return BadRequest(new { error = "Search keyword contains too many wildcard characters." });
            }
            if (categoryIds?.Any(id => id <= 0) == true)
                return BadRequest(new { error = "Category IDs must be positive integers." });

            if (brandIds?.Any(id => id <= 0) == true)
                return BadRequest(new { error = "Brand IDs must be positive integers." });

            // AsNoTracking: read-only query, skips EF's change-tracking overhead.
            var query = _context.Products.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(p =>
                    EF.Functions.Like(p.Name, $"%{keyword}%") ||
                    EF.Functions.Like(p.Description, $"%{keyword}%"));
            }

            if (minPrice.HasValue)
                query = query.Where(p => p.Price >= minPrice.Value);

            if (maxPrice.HasValue)
                query = query.Where(p => p.Price <= maxPrice.Value);

            if (categoryIds is { Count: > 0 })
                query = query.Where(p => categoryIds.Contains(p.CategoryId));

            if (brandIds is { Count: > 0 })
                query = query.Where(p => brandIds.Contains(p.BrandId));

            var totalCount = await query.CountAsync();

            query = (sortBy.ToLowerInvariant(), sortDirection.ToLowerInvariant()) switch
            {
                ("price", "desc") => query.OrderByDescending(p => p.Price),
                ("price", _) => query.OrderBy(p => p.Price),
                ("name", "desc") => query.OrderByDescending(p => p.Name),
                ("category", "desc") => query.OrderByDescending(p => p.Category.Name),
                ("category", _) => query.OrderBy(p => p.Category.Name),
                _ => query.OrderBy(p => p.Name)  
            };

            var products = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new ProductDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Price = p.Price,
                    CategoryName = p.Category.Name,
                    BrandName = p.Brand.Name
                })
                .ToListAsync();

            Response.Headers["X-Total-Count"] = totalCount.ToString();

            return Ok(new
            {
                items = products,
                totalCount,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                sortBy,
                sortDirection
            });
        }
    }
}

