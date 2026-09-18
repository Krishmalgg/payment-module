using PaymentModule.Domain.Enums;

namespace PaymentModule.Application.Common.Mappings;

/// <summary>
/// Converts domain enums into the stable lowercase strings used on the wire
/// (HTTP responses and RabbitMQ payloads).
///
/// Enums are kept out of the serialized contract deliberately: a client should never
/// have to care that Completed happens to be ordinal 2, and adding a new enum member
/// must not renumber anything a client already stores.
/// </summary>
public static class WireValues
{
    /// <summary>"form_post" | "redirect" | "client_secret" | "none"</summary>
    public static string ToWire(this CheckoutAction action) => action switch
    {
        CheckoutAction.FormPost => "form_post",
        CheckoutAction.Redirect => "redirect",
        CheckoutAction.ClientSecret => "client_secret",
        _ => "none"
    };

    /// <summary>"pending" | "completed" | "failed" | "cancelled" | "chargeback" | "refunded" | "unknown"</summary>
    public static string ToWire(this PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "pending",
        PaymentStatus.Completed => "completed",
        PaymentStatus.Failed => "failed",
        PaymentStatus.Cancelled => "cancelled",
        PaymentStatus.Chargeback => "chargeback",
        PaymentStatus.Refunded => "refunded",
        _ => "unknown"
    };

    /// <summary>
    /// Maps a domain status onto the persisted Transaction.Status string.
    /// These values are stored in the database, so they are intentionally left
    /// exactly as they were before this refactor — no migration required.
    /// </summary>
    public static string ToEntityStatus(this PaymentStatus status) => status switch
    {
        PaymentStatus.Completed => "COMPLETED",
        PaymentStatus.Failed => "FAILED",
        PaymentStatus.Cancelled => "FAILED",
        PaymentStatus.Chargeback => "FAILED",
        PaymentStatus.Refunded => "REFUNDED",
        _ => "PENDING"
    };
}
