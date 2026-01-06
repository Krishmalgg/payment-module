// namespace PaymentModule.Infrastructure.Configuration;

// public class PayHereOptions
// {
//     public string MerchantId { get; set; } = string.Empty;
//     public string MerchantSecret { get; set; } = string.Empty;
//     public string Mode { get; set; } = "Sandbox";
//     public string DefaultCurrency { get; set; } = "LKR";
//     public string SuccessUrl { get; set; } = string.Empty;
//     public string CancelUrl { get; set; } = string.Empty;
//     public string NotifyUrl { get; set; } = string.Empty;
// }

namespace PaymentModule.Infrastructure.Configuration;

public class PayHereOptions
{
    public string MerchantId { get; set; } = string.Empty;
    public string MerchantSecret { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string CheckoutUrl { get; set; } = "https://sandbox.payhere.lk/pay";
    public string ReturnUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
    public string NotifyUrl { get; set; } = string.Empty;
}


