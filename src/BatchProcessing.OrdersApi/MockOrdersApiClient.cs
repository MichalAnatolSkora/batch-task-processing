using Microsoft.Extensions.Logging;

namespace BatchProcessing.OrdersApi;

public class MockOrdersApiClient(ILogger<MockOrdersApiClient> logger) : IOrdersApiClient
{
    public async Task<OrderResult> SubmitOrderAsync(OrderDto order, CancellationToken ct)
    {
        // Simulate API latency
        await Task.Delay(50, ct);

        var orderId = Guid.NewGuid().ToString("N")[..8];

        logger.LogInformation(
            "[MockOrdersApi] Received order {OrderNumber} for {Customer}, {LineCount} lines, total {Total}. Assigned ID: {OrderId}",
            order.OrderNumber, order.CustomerName, order.Lines.Count, order.TotalAmount, orderId);

        return new OrderResult(Success: true, OrderId: orderId, Error: null);
    }
}
