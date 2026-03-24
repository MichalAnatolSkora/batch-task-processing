using BatchProcessing.OrdersApi;
using Microsoft.Extensions.Logging;

namespace BatchProcessing.ImportHandlers;

public class OrdersImportHandler(
    ILogger<OrdersImportHandler> logger,
    IOrdersApiClient ordersApiClient) : IImportHandler
{
    public string ImportTypeName => "Orders";

    public async Task HandleAsync(Upload upload, RowGroup group, CancellationToken ct)
    {
        var orderRows = group.Rows
            .Where(r => r.ParentId == null && r.CollectionName == "orders")
            .ToList();

        logger.LogInformation("Orders import {UploadId}, group {GroupKey}: processing {Count} order headers.",
            upload.Id, group.GroupKey, orderRows.Count);

        foreach (var order in orderRows)
        {
            var columns = order.Value.Split(',');
            var orderNumber = columns.ElementAtOrDefault(0) ?? "";
            var customerName = columns.ElementAtOrDefault(1) ?? "";
            var totalAmount = decimal.TryParse(columns.ElementAtOrDefault(2), out var t) ? t : 0m;

            var lines = group.Rows
                .Where(r => r.ParentId == order.Id && r.CollectionName == "order-lines")
                .Select(r =>
                {
                    var lineCols = r.Value.Split(',');
                    return new OrderLineDto(
                        ProductName: lineCols.ElementAtOrDefault(0) ?? "",
                        Quantity: int.TryParse(lineCols.ElementAtOrDefault(1), out var q) ? q : 0,
                        UnitPrice: decimal.TryParse(lineCols.ElementAtOrDefault(2), out var p) ? p : 0m);
                })
                .ToList();

            var dto = new OrderDto(orderNumber, customerName, totalAmount, lines);
            var result = await ordersApiClient.SubmitOrderAsync(dto, ct);

            if (result.Success)
                logger.LogInformation("Order {OrderNumber} submitted, external ID: {OrderId}.", orderNumber, result.OrderId);
            else
                logger.LogWarning("Order {OrderNumber} failed: {Error}.", orderNumber, result.Error);
        }
    }
}
