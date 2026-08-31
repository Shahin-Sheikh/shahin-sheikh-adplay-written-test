using System;
using System.Collections.Generic;
using System.Linq;

namespace AdPlay.Api.Linq
{
    public class Order
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public DateTime OrderDate { get; set; }
        public decimal Amount { get; set; }
    }

    public class CustomerOrderSummary
    {
        public int CustomerId { get; set; }
        public int TotalOrders { get; set; }
        public decimal HighestOrderAmount { get; set; }
        public decimal AverageOrderAmount { get; set; }
        public DateTime LatestOrderDate { get; set; }
    }

    public static class Q11_CustomerOrderSummaryExample
    {
        // Q11. Customer Order Summary
        public static List<CustomerOrderSummary> Summarize(List<Order> orders)
        {
            return orders
                .GroupBy(o => o.CustomerId)
                .Select(g => new CustomerOrderSummary
                {
                    CustomerId = g.Key,
                    TotalOrders = g.Count(),
                    HighestOrderAmount = g.Max(o => o.Amount),
                    AverageOrderAmount = g.Average(o => o.Amount),
                    LatestOrderDate = g.Max(o => o.OrderDate)
                })
                .ToList();
        }
    }
}
