using System.Globalization;
using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OnderCode.Application.Behaviors;
using OnderCode.Application.Events;
using OnderCode.Application.Messaging;
using OnderCode.Kernel.Domain;

namespace OnderCode.Application;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Dağıtıcıyı, davranış hattını ve verilen derlemelerdeki işleyicileri, olay işleyicilerini ve doğrulayıcıları kaydeder.
    /// Davranış sırası (dıştan içe): Log → Alan istisnası çevirisi → Doğrulama → İşleyici.
    /// <code>services.AddSuiteApplication(typeof(RezervasyonOlustur).Assembly);</code>
    /// </summary>
    public static IServiceCollection AddSuiteApplication(this IServiceCollection services, params Assembly[] assemblies)
    {
        services.TryAddSingleton<ISender, Sender>();
        services.TryAddSingleton<IDomainEventDispatcher, DomainEventDispatcher>();

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(DomainExceptionBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        var registrable = new[] { typeof(IRequestHandler<,>), typeof(IDomainEventHandler<>), typeof(IValidator<>) };
        foreach (var type in assemblies.Distinct().SelectMany(a => a.GetTypes()))
        {
            if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition) continue;
            foreach (var contract in type.GetInterfaces())
            {
                if (contract.IsGenericType && registrable.Contains(contract.GetGenericTypeDefinition()))
                    services.AddTransient(contract, type);
            }
        }

        // FluentValidation'ın hazır mesajları Türkçe: "'Ad' boş olmamalı."
        ValidatorOptions.Global.LanguageManager.Culture = CultureInfo.GetCultureInfo("tr");
        return services;
    }
}
