using System.Reflection;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace WellSpent.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var thisAssembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(thisAssembly));

        services.AddSingleton<IMapper>(_ =>
        {
            var config = new MapperConfiguration(cfg => cfg.AddMaps(thisAssembly), NullLoggerFactory.Instance);
            return config.CreateMapper();
        });

        services.AddScoped<Common.VerificationMailer>();
        services.AddScoped<Common.SuperuserGuard>();
        services.AddScoped<Common.BudgetAccessGuard>();
        services.AddScoped<Budgets.TaxReserveRecalculator>();

        return services;
    }
}
