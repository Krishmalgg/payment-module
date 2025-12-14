namespace PaymentModule.Infrastructure.Gateways;

public class PayHereOptions
{
    public string MerchantId { get; set; } = string.Empty;
    public string MerchantSecret { get; set; } = string.Empty;
    public string CheckoutUrl { get; set; } = "https://sandbox.payhere.lk/pay";
    public string ReturnUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
    public string NotifyUrl { get; set; } = string.Empty;
}

