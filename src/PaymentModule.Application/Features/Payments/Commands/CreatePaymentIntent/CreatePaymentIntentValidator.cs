using FluentValidation;
using Microsoft.Extensions.Options;
using PaymentModule.Application.Common.Configuration;

namespace PaymentModule.Application.Features.Payments.Commands.CreatePaymentIntent;

public class CreatePaymentIntentValidator : AbstractValidator<CreatePaymentIntentCommand>
{
    private readonly string[] _acceptedCurrencies;

    public CreatePaymentIntentValidator(IOptions<PaymentGatewayOptions> options)
    {
        // Accepted currencies are deployment policy, not a constant. Read them from
        // PaymentGateway:AcceptedCurrencies so adding a provider with different
        // currency support needs no Application-layer edit.
        _acceptedCurrencies = options.Value.AcceptedCurrencies ?? [];

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Amount must be greater than zero");

        RuleFor(x => x.Amount)
            .LessThanOrEqualTo(1000000)
            .WithMessage("Amount cannot exceed 1,000,000");

        RuleFor(x => x.Currency)
            .NotEmpty()
            .WithMessage("Currency is required");

        // Format and policy are separate questions: "JPY" is a perfectly valid ISO 4217
        // code that this deployment may still not accept. Reporting them with the same
        // message makes the second case confusing to debug.
        When(x => !string.IsNullOrWhiteSpace(x.Currency), () =>
        {
            RuleFor(x => x.Currency)
                .Must(BeIso4217Shaped)
                .WithMessage("Currency must be a three-letter ISO 4217 code (e.g., LKR, USD, EUR)");

            RuleFor(x => x.Currency)
                .Must(BeAccepted)
                .When(x => BeIso4217Shaped(x.Currency))
                .WithMessage(_ => $"Currency is not accepted by this deployment. Accepted: {string.Join(", ", _acceptedCurrencies)}");
        });
    }

    private static bool BeIso4217Shaped(string currency) =>
        !string.IsNullOrWhiteSpace(currency)
        && currency.Length == 3
        && currency.All(char.IsLetter);

    private bool BeAccepted(string currency) =>
        _acceptedCurrencies.Contains(currency.ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);
}
