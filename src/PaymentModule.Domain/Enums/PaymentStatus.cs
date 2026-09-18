namespace PaymentModule.Domain.Enums;

/// <summary>
/// Provider-neutral outcome of a payment. Every gateway adapter is responsible for
/// translating its own status codes into one of these values, so that no part of the
/// Application layer ever has to know what (for example) PayHere's "2" means.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Status could not be determined from the provider payload.</summary>
    Unknown = 0,

    /// <summary>Authorised or awaiting confirmation; the final outcome arrives by webhook.</summary>
    Pending,

    /// <summary>Funds captured successfully.</summary>
    Completed,

    /// <summary>The provider rejected or failed the payment.</summary>
    Failed,

    /// <summary>The payer abandoned or cancelled the checkout.</summary>
    Cancelled,

    /// <summary>Funds were reversed by the issuing bank after capture.</summary>
    Chargeback,

    /// <summary>Funds were returned to the payer by us.</summary>
    Refunded
}
