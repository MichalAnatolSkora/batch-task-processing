using Microsoft.Extensions.Logging;

namespace BatchProcessing.ImportHandlers;

public class ProductsImportHandler(ILogger<ProductsImportHandler> logger) : IImportHandler
{
    public string ImportTypeName => "Products";

    public Task HandleAsync(Upload upload, RowGroup group, CancellationToken ct)
    {
        var productRows = group.Rows
            .Where(r => r.ParentId == null && r.CollectionName == "products")
            .ToList();

        logger.LogInformation("Products import {UploadId}, group {GroupKey}: processing {Count} products.",
            upload.Id, group.GroupKey, productRows.Count);

        foreach (var product in productRows)
        {
            var columns = product.Value.Split(',');
            var sku = columns.ElementAtOrDefault(0) ?? "";
            var name = columns.ElementAtOrDefault(1) ?? "";
            var category = columns.ElementAtOrDefault(2) ?? "";
            var price = columns.ElementAtOrDefault(3) ?? "0";

            logger.LogInformation("Product {SKU}: {Name} [{Category}] @ {Price}", sku, name, category, price);

            var variantRows = group.Rows
                .Where(r => r.ParentId == product.Id && r.CollectionName == "product-variants");

            foreach (var variant in variantRows)
            {
                var varCols = variant.Value.Split(',');
                var variantName = varCols.ElementAtOrDefault(0) ?? "";
                var color = varCols.ElementAtOrDefault(1) ?? "";
                var size = varCols.ElementAtOrDefault(2) ?? "";

                logger.LogInformation("  Variant: {Variant} Color={Color} Size={Size}", variantName, color, size);
            }
        }

        return Task.CompletedTask;
    }
}
