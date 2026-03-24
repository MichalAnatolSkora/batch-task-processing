using BatchProcessing;
using BatchProcessing.ImportHandlers;
using BatchProcessing.OrdersApi;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

builder.Services.AddOrdersApi();

builder.Services.AddKeyedScoped<IImportHandler, OrdersImportHandler>("Orders");
builder.Services.AddKeyedScoped<IImportHandler, ProductsImportHandler>("Products");

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Batch Processing Service";
});

var host = builder.Build();
host.Run();
