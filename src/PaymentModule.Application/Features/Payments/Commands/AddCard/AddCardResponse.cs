namespace PaymentModule.Application.Features.Payments.Commands.AddCard;

public record AddCardResponse(
    bool Success,
    string MerchantId,
    string OrderId,
    string Currency,
    string Hash,
    string NotifyUrl,
    string PreapprovalUrl,
    string Amount
);
