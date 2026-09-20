using Microsoft.Extensions.Options;
using PaymentModule.Application.Common.Configuration;
using PaymentModule.Application.Features.Payments.Commands.CreatePaymentIntent;
using Xunit;

namespace PaymentModule.SecurityTests;

/// <summary>
/// Guards Gap 3: accepted currencies come from configuration, not a hardcoded array,
/// and ISO format is reported separately from deployment policy.
/// </summary>
public class CurrencyValidationTests
{
    private static CreatePaymentIntentValidator Validator(params string[] accepted) =>
        new(Options.Create(new PaymentGatewayOptions { AcceptedCurrencies = accepted }));

    private static CreatePaymentIntentCommand Command(string currency) =>
        new(100m, currency, null, null, "ORD-1", new UserData(UserId: Guid.NewGuid().ToString()));

    [Fact]
    public void Accepts_a_currency_listed_in_configuration()
    {
        var result = Validator("LKR", "USD").Validate(Command("USD"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Accepts_a_currency_that_only_configuration_knows_about()
    {
        // JPY is absent from the old hardcoded list. Configuration alone must decide.
        var result = Validator("JPY").Validate(Command("JPY"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Rejects_a_valid_iso_code_that_this_deployment_does_not_accept()
    {
        var result = Validator("LKR").Validate(Command("USD"));

        Assert.False(result.IsValid);
        Assert.Contains("not accepted", result.Errors[0].ErrorMessage);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("12A")]
    public void Rejects_a_malformed_code_as_a_format_problem(string currency)
    {
        var result = Validator("USD").Validate(Command(currency));

        Assert.False(result.IsValid);
        // Format and policy must not be reported with the same message.
        Assert.Contains("ISO 4217", result.Errors[0].ErrorMessage);
        Assert.DoesNotContain(result.Errors, e => e.ErrorMessage.Contains("not accepted"));
    }

    [Fact]
    public void Currency_matching_is_case_insensitive()
    {
        Assert.True(Validator("USD").Validate(Command("usd")).IsValid);
    }
}
