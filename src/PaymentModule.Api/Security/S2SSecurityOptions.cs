namespace PaymentModule.Api.Security;

public class S2SSecurityOptions
{
    public string[] ApiKeys { get; set; } = [];
    public string[] HmacSecrets { get; set; } = [];
}
