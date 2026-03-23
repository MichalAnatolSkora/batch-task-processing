using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BatchProcessing.ImportHandlers;

public class OrdersImportHandler(ILogger<OrdersImportHandler> logger) : IImportHandler
{
    public string ImportTypeName => "Orders";

    public async Task HandleAsync(Upload upload, IDbConnection db, CancellationToken ct)
    {
        // Root collection "orders" — each row is an order header: OrderNumber,CustomerName,TotalAmount
        var orderRows = (await db.QueryAsync<RowValue>(
            new CommandDefinition(
                "SELECT * FROM RowValues WHERE UploadId = @UploadId AND ParentId IS NULL AND CollectionName = 'orders'",
                new { UploadId = upload.Id },
                cancellationToken: ct))).ToList();

        logger.LogInformation("Orders import {UploadId}: processing {Count} order headers.", upload.Id, orderRows.Count);

        foreach (var order in orderRows)
        {
            var columns = order.Value.Split(',');
            var orderNumber = columns.ElementAtOrDefault(0) ?? "";
            var customerName = columns.ElementAtOrDefault(1) ?? "";
            var totalAmount = columns.ElementAtOrDefault(2) ?? "0";

            logger.LogInformation("Order {OrderNumber} for {Customer}, total {Amount}.",
                orderNumber, customerName, totalAmount);

            // Child collection "order-lines" — each row: ProductName,Quantity,UnitPrice
            var lineRows = await db.QueryAsync<RowValue>(
                new CommandDefinition(
                    "SELECT * FROM RowValues WHERE UploadId = @UploadId AND ParentId = @ParentId AND CollectionName = 'order-lines'",
                    new { UploadId = upload.Id, ParentId = order.Id },
                    cancellationToken: ct));

            foreach (var line in lineRows)
            {
                var lineCols = line.Value.Split(',');
                var product = lineCols.ElementAtOrDefault(0) ?? "";
                var qty = lineCols.ElementAtOrDefault(1) ?? "0";
                var price = lineCols.ElementAtOrDefault(2) ?? "0";

                logger.LogInformation("  Line: {Product} x{Qty} @ {Price}", product, qty, price);
            }
        }
    }
}
