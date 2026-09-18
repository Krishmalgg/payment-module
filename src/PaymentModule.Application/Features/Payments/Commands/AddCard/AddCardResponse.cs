namespace PaymentModule.Application.Features.Payments.Commands.AddCard;

/// <summary>
/// Provider-neutral card-setup instruction.
///
/// This replaces the old response whose top-level properties (MerchantId, Hash,
/// NotifyUrl, PreapprovalUrl) were PayHere checkout form fields — none of which
/// exist for Stripe or PayPal. Those values now live inside <see cref="Fields"/>,
/// exactly as they already did for the payment-initiate response.
/// </summary>
public record AddCardResponse(
    bool Success,
    string Provider,
    string Action,
    string OrderId,
    string? Url,
    IReadOnlyDictionary<string, string> Fields
);
