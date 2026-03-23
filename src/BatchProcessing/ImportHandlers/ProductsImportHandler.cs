using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BatchProcessing.ImportHandlers;

public class ProductsImportHandler(ILogger<ProductsImportHandler> logger) : IImportHandler
{
    public string ImportTypeName => "Products";

    public async Task HandleAsync(Upload upload, IDbConnection db, CancellationToken ct)
    {
        // Root collection "products" — each row is: SKU,Name,Category,Price
        var productRows = (await db.QueryAsync<RowValue>(
            new CommandDefinition(
                "SELECT * FROM RowValues WHERE UploadId = @UploadId AND ParentId IS NULL AND CollectionName = 'products'",
                new { UploadId = upload.Id },
                cancellationToken: ct))).ToList();

        logger.LogInformation("Products import {UploadId}: processing {Count} products.", upload.Id, productRows.Count);

        foreach (var product in productRows)
        {
            var columns = product.Value.Split(',');
            var sku = columns.ElementAtOrDefault(0) ?? "";
            var name = columns.ElementAtOrDefault(1) ?? "";
            var category = columns.ElementAtOrDefault(2) ?? "";
            var price = columns.ElementAtOrDefault(3) ?? "0";

            logger.LogInformation("Product {SKU}: {Name} [{Category}] @ {Price}", sku, name, category, price);

            // Child collection "product-variants" — each row: VariantName,Color,Size
            var variantRows = await db.QueryAsync<RowValue>(
                new CommandDefinition(
                    "SELECT * FROM RowValues WHERE UploadId = @UploadId AND ParentId = @ParentId AND CollectionName = 'product-variants'",
                    new { UploadId = upload.Id, ParentId = product.Id },
                    cancellationToken: ct));

            foreach (var variant in variantRows)
            {
                var varCols = variant.Value.Split(',');
                var variantName = varCols.ElementAtOrDefault(0) ?? "";
                var color = varCols.ElementAtOrDefault(1) ?? "";
                var size = varCols.ElementAtOrDefault(2) ?? "";

                logger.LogInformation("  Variant: {Variant} Color={Color} Size={Size}", variantName, color, size);
            }
        }
    }
}
