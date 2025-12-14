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
            .WithMessage("Currency is required")
            .Must(BeValidCurrency)
            .WithMessage("Currency must be a valid ISO 4217 code (e.g., LKR, USD, EUR)");

        RuleFor(x => x.IdempotencyKey)
            .MaximumLength(255)
            .When(x => x.IdempotencyKey != null)
            .WithMessage("Idempotency key must not exceed 255 characters");
    }

    private bool BeValidCurrency(string currency)
    {
        var validCurrencies = new[] { "LKR", "USD", "EUR", "GBP", "INR", "AUD", "CAD" };
        return validCurrencies.Contains(currency.ToUpperInvariant());
    }
}
