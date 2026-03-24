namespace BatchProcessing.OrdersApi;

public record OrderDto(string OrderNumber, string CustomerName, decimal TotalAmount, List<OrderLineDto> Lines);
public record OrderLineDto(string ProductName, int Quantity, decimal UnitPrice);
public record OrderResult(bool Success, string? OrderId, string? Error);

public interface IOrdersApiClient
{
    Task<OrderResult> SubmitOrderAsync(OrderDto order, CancellationToken ct);
}
