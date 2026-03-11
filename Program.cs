using BatchProcessing;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Batch Processing Service";
});

var host = builder.Build();
host.Run();
