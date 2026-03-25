using BatchProcessing;
using BatchProcessing.ImportHandlers;
using BatchProcessing.OrdersApi;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

builder.Services.AddOrdersApi();

builder.Services.AddKeyedScoped<IImportHandler, OrdersImportHandler>("Orders");
builder.Services.AddKeyedScoped<IImportHandler, ProductsImportHandler>("Products");

var serviceName = builder.Configuration["ServiceName"] ?? "Batch Processing Service";

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = serviceName;
});

var host = builder.Build();
host.Run();
