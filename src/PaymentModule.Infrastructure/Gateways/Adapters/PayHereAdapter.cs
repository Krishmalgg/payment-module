using System.Globalization;
using Microsoft.Extensions.Options;
using PaymentModule.Domain.Ports;
using PaymentModule.Domain.ValueObjects;
using PaymentModule.Infrastructure.Gateways.PayHere;
using PaymentModule.Infrastructure.Configuration;
using Polly;
using Polly.Registry;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;
using Microsoft.Extensions.Caching.Memory; // Required for Set extension method

namespace PaymentModule.Infrastructure.Gateways.Adapters;

public class PayHereAdapter : IPaymentGateway
{
    private readonly PayHereOptions _options;
    private readonly ResiliencePipeline _pipeline;
    private readonly HttpClient _httpClient;
    private readonly ILogger<PayHereAdapter> _logger;
    private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;

    // Static so all instances share the same lock — guards against cache stampede
    // when multiple concurrent requests all see a token cache miss simultaneously.
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);

    public PayHereAdapter(
        IOptions<PayHereOptions> options, 
        ResiliencePipelineProvider<string> pipelineProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<PayHereAdapter> logger,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
    {
        _options = options.Value;
        _pipeline = pipelineProvider.GetPipeline("payment-gateway");
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;
        _cache = cache;
    }

    public async Task<PaymentIntentResult> CreatePaymentIntent(TransactionId id, Money amount, Dictionary<string, string>? metadata, CancellationToken ct, string? customerToken = null)
    {
        var orderId = metadata != null && metadata.TryGetValue("order_id", out var metaOrderId) ? metaOrderId : id.Value.ToString();
        var amountStr = amount.Value.ToString("0.00", CultureInfo.InvariantCulture);

        // --- Instant Payment Flow ---
        if (!string.IsNullOrEmpty(customerToken))
        {
            _logger.LogInformation("Customer Token found. Attempting instant charge for Order {OrderId}", orderId);
            try 
            {
                var chargeResult = await ChargeCustomerAsync(orderId, amountStr, amount.Currency, customerToken, metadata, ct);
                if (chargeResult)
                {
                    return new PaymentIntentResult(
                        "payhere",
                        "PROCESSING", // Changed from SUCCESS
                        "",
                        new Dictionary<string, string>
                        {
                            ["order_id"] = orderId,
                            ["amount"] = amountStr,
                            ["currency"] = amount.Currency,
                            ["customer_token"] = customerToken,
                            ["info"] = "Instant charge initiated. Final status pending webhook."
                        }
                    );
                }
                else
                {
                     _logger.LogWarning("Instant charge failed for Order {OrderId}.", orderId);
                     throw new Exception("Instant charge failed.");
                }
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Error processing instant charge for Order {OrderId}", orderId);
                  throw;
            }
        }

        var hash = PayHereSecurity.GenerateHash(
            _options.MerchantId, 
            orderId, 
            amountStr, 
            amount.Currency, 
            _options.MerchantSecret);

        var fields = new Dictionary<string, string>
        {
            ["merchant_id"] = _options.MerchantId,
            ["return_url"] = _options.ReturnUrl,
            ["cancel_url"] = _options.CancelUrl,
            ["notify_url"] = _options.CheckoutNotifyUrl,
            ["order_id"] = orderId,
            ["items"] = metadata != null && metadata.TryGetValue("items", out var items) ? items : "Payment",
            ["amount"] = amountStr,
            ["currency"] = amount.Currency,
            ["hash"] = hash,
            ["first_name"] = metadata != null && metadata.TryGetValue("first_name", out var fn) ? fn : "",
            ["last_name"] = metadata != null && metadata.TryGetValue("last_name", out var ln) ? ln : "",
            ["email"] = metadata != null && metadata.TryGetValue("email", out var em) ? em : "",
            ["address"] = metadata != null && metadata.TryGetValue("address", out var ad) ? ad : "",
            ["city"] = metadata != null && metadata.TryGetValue("city", out var ci) ? ci : "",
            ["country"] = metadata != null && metadata.TryGetValue("country", out var co) ? co : ""
        };

        return new PaymentIntentResult(
            Gateway: "payhere",
            Action: "form_post",
            Url: _options.CheckoutUrl,
            Fields: fields
        );
    }

    public Task<PreapprovalResult> InitiatePreapproval(string orderId, Dictionary<string, string>? metadata, CancellationToken ct)
    {
        var hash = PayHereSecurity.GeneratePreapprovalHash(
            _options.MerchantId, 
            orderId, 
            _options.DefaultCurrency,
            _options.PreapprovalAmount,
            _options.MerchantSecret);

        var fields = new Dictionary<string, string>
        {
            ["merchant_id"] = _options.MerchantId,
            ["return_url"] = _options.ReturnUrl,
            ["notify_url"] = _options.PreapprovalNotifyUrl, 
            ["order_id"] = orderId,
            ["items"] = "Add Card",
            ["currency"] = _options.DefaultCurrency,
            ["amount"] = _options.PreapprovalAmount,
            ["hash"] = hash,
        };

        return Task.FromResult(new PreapprovalResult(
            Gateway: "payhere",
            Action: "form_post",
            Url: _options.PreapprovalUrl,
            Fields: fields
        ));
    }

    public Task<WebhookResult> HandleWebhook(string payload, IDictionary<string, string> headers, CancellationToken ct)
    {
        var form = ParseForm(payload);
        
        var hasMerchantId = form.TryGetValue("merchant_id", out var merchantId);
        var hasOrderId = form.TryGetValue("order_id", out var orderId);
        var hasAmount = form.TryGetValue("payhere_amount", out var amount); 
        var hasCurrency = form.TryGetValue("payhere_currency", out var currency);
        var hasStatus = form.TryGetValue("status_code", out var statusCode);
        var hasSignature = form.TryGetValue("md5sig", out var remoteSig);

        if (!hasMerchantId || !hasOrderId || !hasAmount || !hasCurrency || !hasStatus || !hasSignature)
        {
            return Task.FromResult(new WebhookResult(false, orderId ?? "", statusCode ?? "", ErrorMessage: "Missing required fields"));
        }

        var isOk = PayHereSecurity.VerifyNotificationSignature(
            merchantId!, 
            orderId!, 
            amount!, 
            currency!, 
            statusCode!, 
            _options.MerchantSecret, 
            remoteSig!);

        var result = new WebhookResult(
            IsSuccess: isOk,
            OrderId: orderId ?? string.Empty,
            StatusCode: statusCode ?? string.Empty,
            Amount: amount,
            Currency: currency,
            ProviderReference: form.GetValueOrDefault("payment_id", ""),
            CustomerToken: form.GetValueOrDefault("customer_token", ""),
            CardHolderName: form.GetValueOrDefault("card_holder_name", ""),
            CardNo: form.GetValueOrDefault("card_no", ""),
            CardExpiry: form.GetValueOrDefault("card_expiry", ""),
            CardType: form.GetValueOrDefault("method", ""),
            Custom1: form.GetValueOrDefault("custom_1", ""),
            Custom2: form.GetValueOrDefault("custom_2", "")
        );
        
        return Task.FromResult(result);
    }

    private async Task<bool> ChargeCustomerAsync(string orderId, string amount, string currency, string customerToken, Dictionary<string, string>? metadata, CancellationToken ct)
    {
        try 
        {
            var accessToken = await GetAccessTokenAsync(ct);

            var payload = new
            {
                order_id = orderId,
                items = metadata?.GetValueOrDefault("items") ?? "Payment",
                currency = currency,
                amount = double.Parse(amount),
                customer_token = customerToken,
                notify_url = _options.InstantPayNotifyUrl,
                custom_1 = metadata?.GetValueOrDefault("user_id"),
                custom_2 = metadata?.GetValueOrDefault("email")
            };

            var requestUri = _options.ChargeUrl;

            var response = await _pipeline.ExecuteAsync(async token => 
            {
                var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
                {
                    Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json"),
                    Headers = { { "Authorization", $"Bearer {accessToken}" } }
                };

                var res = await _httpClient.SendAsync(request, token);
                
                if ((int)res.StatusCode >= 500 || res.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    res.EnsureSuccessStatusCode();
                }

                return res;
            }, ct);
            Console.WriteLine($"response response : {response.StatusCode}"); // Fixed syntax
            
            var content = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("PayHere Charge Response: {Response}", content);

            if (!response.IsSuccessStatusCode) return false;

            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("status", out var statusProp))
            {
                return statusProp.GetInt32() == 1;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ChargeCustomerAsync failed after retries.");
            throw;
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        const string cacheKey = "PayHere_AccessToken";

        // Fast path — no lock needed (vast majority of calls hit this)
        if (_cache.TryGetValue(cacheKey, out string? cachedToken))
        {
            _logger.LogInformation("Using cached PayHere access token.");
            return cachedToken!;
        }

        // Slow path — serialize concurrent fetches so only one thread calls PayHere OAuth
        await _tokenLock.WaitAsync(ct);
        try
        {
            // Double-check: another thread may have fetched while we waited for the lock
            if (_cache.TryGetValue(cacheKey, out cachedToken))
            {
                _logger.LogInformation("Using cached PayHere access token (after lock).");
                return cachedToken!;
            }

            _logger.LogInformation("Fetching new PayHere access token...");
            var authToken = System.Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{_options.AppId}:{_options.AppSecret}"));
            var requestUri = _options.OAuthTokenUrl;

            var response = await _pipeline.ExecuteAsync(async token =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
                {
                    Content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("grant_type", "client_credentials")
                    }),
                    Headers = { { "Authorization", $"Basic {authToken}" } }
                };

                var res = await _httpClient.SendAsync(request, token);

                if ((int)res.StatusCode >= 500 || res.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    res.EnsureSuccessStatusCode();
                }

                return res;
            }, ct);

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(content);
            var newToken = doc.RootElement.GetProperty("access_token").GetString()!;

            var cacheOptions = new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(50));

            _cache.Set(cacheKey, newToken, cacheOptions);

            return newToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task<PaymentModule.Domain.ValueObjects.RefundResult> RefundAsync(
        string providerRefId,
        decimal amount,
        string currency,
        string description,
        CancellationToken ct)
    {
        try
        {
            var accessToken = await GetAccessTokenAsync(ct);
            var requestUri = _options.RefundUrl;

            _logger.LogInformation("Initiating refund for Payment ID: {PaymentId}, Amount: {Amount}", 
                providerRefId, amount);

            var request = new PaymentModule.Infrastructure.Gateways.PayHere.DTOs.PayHereRefundRequestDto
            {
                PaymentId = providerRefId,
                Description = description,
                Amount = amount,
                Currency = currency
            };

            var requestJson = System.Text.Json.JsonSerializer.Serialize(request);
            _logger.LogInformation("PayHere Refund Request Payload: {Payload}", requestJson);

            var response = await _pipeline.ExecuteAsync(async token =>
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
                {
                    Content = new StringContent(
                        requestJson, 
                        System.Text.Encoding.UTF8, 
                        "application/json"),
                    Headers = { { "Authorization", $"Bearer {accessToken}" } }
                };

                var res = await _httpClient.SendAsync(httpRequest, token);

                // Retry on server errors or rate limiting
                if ((int)res.StatusCode >= 500 || res.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    res.EnsureSuccessStatusCode();
                }

                return res;
            }, ct);

            var content = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("PayHere Refund Response: {Response}", content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayHere refund failed with status code: {StatusCode}", response.StatusCode);
                return new PaymentModule.Domain.ValueObjects.RefundResult(
                    IsSuccess: false,
                    Status: "FAILED",
                    ProviderRefundId: null,
                    ErrorMessage: $"HTTP {response.StatusCode}: {content}"
                );
            }

            // Parse successful response
            var refundResponse = System.Text.Json.JsonSerializer.Deserialize<PaymentModule.Infrastructure.Gateways.PayHere.DTOs.PayHereRefundResponseDto>(
                content, 
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (refundResponse == null)
            {
                return new PaymentModule.Domain.ValueObjects.RefundResult(
                    IsSuccess: false,
                    Status: "FAILED",
                    ProviderRefundId: null,
                    ErrorMessage: "Failed to parse PayHere response"
                );
            }

            return new PaymentModule.Domain.ValueObjects.RefundResult(
                IsSuccess: refundResponse.IsSuccess,
                Status: refundResponse.GlobalStatus,
                ProviderRefundId: refundResponse.ProviderRefundId,
                ErrorMessage: refundResponse.Error
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RefundAsync failed after retries for Payment ID: {PaymentId}", providerRefId);
            return new PaymentModule.Domain.ValueObjects.RefundResult(
                IsSuccess: false,
                Status: "FAILED",
                ProviderRefundId: null,
                ErrorMessage: ex.Message
            );
        }
    }

    private static Dictionary<string, string> ParseForm(string payload)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(payload)) return dict;
        var pairs = payload.Split('&');
        foreach (var p in pairs)
        {
            var kv = p.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0] ?? "");
            var val = kv.Length == 2 ? Uri.UnescapeDataString(kv[1]) : "";
            if (!string.IsNullOrEmpty(key)) dict[key] = val;
        }
        return dict;
    }
}
