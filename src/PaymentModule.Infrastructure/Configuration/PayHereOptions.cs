namespace PaymentModule.Infrastructure.Configuration;

public class PayHereOptions
{
    public string MerchantId { get; set; } = string.Empty;
    public string MerchantSecret { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string CheckoutUrl { get; set; } = string.Empty;
    public string PreapprovalUrl { get; set; } = string.Empty;
    public string PreapprovalAmount { get; set; } = string.Empty;
    public string PreapprovalNotifyUrl { get; set; } = string.Empty;
    public string DefaultCurrency { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
    public string CheckoutNotifyUrl { get; set; } = string.Empty;
    public string InstantPayNotifyUrl { get; set; } = string.Empty;
    public string ChargeUrl { get; set; } = string.Empty;
    public string OAuthTokenUrl { get; set; } = string.Empty;
    public string RefundUrl { get; set; } = string.Empty;
}


