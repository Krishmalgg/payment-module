using FluentValidation;

namespace PaymentModule.Application.Features.Payments.Commands.CreatePaymentIntent;

public class CreatePaymentIntentValidator : AbstractValidator<CreatePaymentIntentCommand>
{
    public CreatePaymentIntentValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Amount must be greater than zero");

        RuleFor(x => x.Amount)
            .LessThanOrEqualTo(1000000)
            .WithMessage("Amount cannot exceed 1,000,000");

        RuleFor(x => x.Currency)
            .NotEmpty()
            .WithMessage("Currency is required");

        RuleFor(x => x.Currency)
            .Must(BeValidCurrency)
            .WithMessage("Currency must be a valid ISO 4217 code (e.g., LKR, USD, EUR)")
            .When(x => !string.IsNullOrWhiteSpace(x.Currency));
    }

    private bool BeValidCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            return false;

        var validCurrencies = new[] { "LKR", "USD", "EUR", "GBP", "INR", "AUD", "CAD" };
        return validCurrencies.Contains(currency.ToUpperInvariant());
    }
}
