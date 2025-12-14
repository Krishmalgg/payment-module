namespace PaymentModule.Infrastructure.Configuration;

public class PayHereOptions
{
    public string MerchantId { get; set; } = string.Empty;
    public string MerchantSecret { get; set; } = string.Empty;
    public string Mode { get; set; } = "Sandbox";
    public string DefaultCurrency { get; set; } = "LKR";
    public string SuccessUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
    public string NotifyUrl { get; set; } = string.Empty;
}

