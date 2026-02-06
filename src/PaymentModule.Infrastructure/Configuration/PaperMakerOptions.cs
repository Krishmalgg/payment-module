namespace PaymentModule.Infrastructure.Configuration;

public class PaperMakerOptions
{
    public string NotificationUrl { get; set; } = string.Empty;
    public List<string> ApiKeys { get; set; } = [];
    public List<string> HmacSecrets { get; set; } = [];
}
