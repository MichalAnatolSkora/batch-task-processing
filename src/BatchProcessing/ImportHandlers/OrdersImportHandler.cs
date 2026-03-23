using Microsoft.Extensions.Logging;

namespace BatchProcessing.ImportHandlers;

public class OrdersImportHandler(ILogger<OrdersImportHandler> logger) : IImportHandler
{
    public string ImportTypeName => "Orders";

    public Task HandleAsync(Upload upload, RowGroup group, CancellationToken ct)
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
            var totalAmount = columns.ElementAtOrDefault(2) ?? "0";

            logger.LogInformation("Order {OrderNumber} for {Customer}, total {Amount}.",
                orderNumber, customerName, totalAmount);

            var lineRows = group.Rows
                .Where(r => r.ParentId == order.Id && r.CollectionName == "order-lines");

            foreach (var line in lineRows)
            {
                var lineCols = line.Value.Split(',');
                var product = lineCols.ElementAtOrDefault(0) ?? "";
                var qty = lineCols.ElementAtOrDefault(1) ?? "0";
                var price = lineCols.ElementAtOrDefault(2) ?? "0";

                logger.LogInformation("  Line: {Product} x{Qty} @ {Price}", product, qty, price);
            }
        }

        return Task.CompletedTask;
    }
}
