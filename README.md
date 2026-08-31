# AdPlay Technology — Full Stack Engineer Written Test

Answers to all coding questions, organized by section.

## Section A — MySQL (`/sql`)
- `Q1_top3_highest_paid.sql` — top 3 earners per department (window function, no LIMIT)
- `Q2_consecutive_login_days.sql` — 7-day login streak detection (gaps-and-islands)
- `Q3_customer_order_analysis.sql` — current/previous amount, difference, running total
- `Q4_query_optimization.sql` — sargable rewrite + indexing plan for the slow report query
- `Q5_dynamic_search_procedure.sql` — parameterized dynamic search proc, injection-safe, paginated

## Section B — ASP.NET Core (`/dotnet`)
- `Q6_SubscriptionController.cs` — race-condition-free subscription endpoint (atomic UPDATE + legacy transactional fallback)
- `Q7_PaymentApi.cs` — payment API with idempotency key, retries (Polly), distributed lock, duplicate prevention
- `Q8_GenericRepository.cs` — generic repository: dynamic filter/include/sort, pagination, projection
- `Q9_DynamicSortExtensions.cs` — dynamic `OrderBy("Field desc")` via Expression Trees
- `Q10_RequestLoggingMiddleware.cs` — request/response/timing/correlation-ID/exception logging middleware
- `Q13_ProductsController.cs` — product search API, EF Core, performance-optimized

## Section C — LINQ (`/linq`)
- `Q11_CustomerOrderSummary.cs`
- `Q12_DepartmentStatistics.cs` (includes manual median calculation)

## Section D — React (`/react`)
- `Q14_ProductList.jsx` — virtualized, infinite-scrolling, debounced-search product list for 1M+ rows

See the accompanying Word document for write-ups, explanations, and design rationale for each answer.
