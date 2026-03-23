using BatchProcessing;
using BatchProcessing.ImportHandlers;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

builder.Services.AddKeyedScoped<IImportHandler, OrdersImportHandler>("Orders");
builder.Services.AddKeyedScoped<IImportHandler, ProductsImportHandler>("Products");

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Batch Processing Service";
});

var host = builder.Build();
host.Run();
