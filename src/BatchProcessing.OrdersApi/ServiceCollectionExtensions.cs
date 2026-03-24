using Microsoft.Extensions.DependencyInjection;

namespace BatchProcessing.OrdersApi;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrdersApi(this IServiceCollection services)
    {
        services.AddSingleton<IOrdersApiClient, MockOrdersApiClient>();
        return services;
    }
}
