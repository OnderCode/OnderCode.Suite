using FluentValidation;
using OnderCode.Kernel.Validation;

namespace OnderCode.Application.Validation;

/// <summary>
/// Türkiye'ye özgü doğrulama kuralları. Boş değer geçerli sayılır; zorunluysa <c>NotEmpty()</c> ile birlikte kullanılır:
/// <code>RuleFor(x =&gt; x.Tckn).NotEmpty().Tckn();</code>
/// </summary>
public static class TurkishRules
{
    public static IRuleBuilderOptions<T, TProperty> Tckn<T, TProperty>(this IRuleBuilder<T, TProperty> rule) =>
        rule.Must(v => IsEmpty(v) || TurkishIdentity.IsValidTckn(v as string))
            .WithMessage("'{PropertyName}' geçerli bir T.C. kimlik numarası değil.");

    public static IRuleBuilderOptions<T, TProperty> Vkn<T, TProperty>(this IRuleBuilder<T, TProperty> rule) =>
        rule.Must(v => IsEmpty(v) || TurkishIdentity.IsValidVkn(v as string))
            .WithMessage("'{PropertyName}' geçerli bir vergi kimlik numarası değil.");

    public static IRuleBuilderOptions<T, TProperty> Iban<T, TProperty>(this IRuleBuilder<T, TProperty> rule) =>
        rule.Must(v => IsEmpty(v) || TurkishIdentity.IsValidIban(v as string))
            .WithMessage("'{PropertyName}' geçerli bir IBAN değil.");

    private static bool IsEmpty<TProperty>(TProperty value) => value is null || value is string { Length: 0 };
}
