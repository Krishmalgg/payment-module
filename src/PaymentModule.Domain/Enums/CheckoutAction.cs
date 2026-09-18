namespace PaymentModule.Domain.Enums;

/// <summary>
/// What the client application must do next to complete a checkout or card setup.
/// This is deliberately the *only* branching a frontend needs: read the action,
/// perform it, ignore the rest.
/// </summary>
public enum CheckoutAction
{
    /// <summary>Nothing to do — the operation already reached a terminal state server-side.</summary>
    None = 0,

    /// <summary>Build an HTML form from <c>Fields</c> and POST it to <c>Url</c>. (PayHere)</summary>
    FormPost,

    /// <summary>Send the browser to <c>Url</c>. (PayPal, 3-D Secure flows)</summary>
    Redirect,

    /// <summary>Hand <c>Fields["client_secret"]</c> to the provider's client SDK. (Stripe)</summary>
    ClientSecret
}
