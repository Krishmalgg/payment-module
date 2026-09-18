using System.Globalization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PaymentModule.Domain.Enums;
using PaymentModule.Domain.Ports;
using PaymentModule.Domain.ValueObjects;
using PaymentModule.Infrastructure.Configuration;
using PaymentModule.Infrastructure.Gateways.PayHere;
using PaymentModule.Infrastructure.Gateways.PayHere.DTOs;
using Polly;
using Polly.Registry;
using System.Text.Json;

namespace PaymentModule.Infrastructure.Gateways.Adapters;

/// <summary>
/// PayHere implementation of <see cref="IPaymentGateway"/>.
///
/// Every PayHere-specific concept — md5sig, status_code "2", custom_1/custom_2,
/// preapproval, the hidden-form checkout model — is contained in this file and
/// translated into the neutral port vocabulary before it leaves.
/// </summary>
public class PayHereAdapter : IPaymentGateway
{
    private readonly PayHereOptions _options;
    private readonly ResiliencePipeline _pipeline;
    private readonly HttpClient _httpClient;
    private readonly ILogger<PayHereAdapter> _logger;
    private readonly IMemoryCache _cache;

    // Static so all instances share the same lock — guards against cache stampede
    // when multiple concurrent requests all see a token cache miss simultaneously.
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);

    public string Provider => "payhere";

    public PayHereAdapter(
        IOptions<PayHereOptions> options,
        ResiliencePipelineProvider<string> pipelineProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<PayHereAdapter> logger,
        IMemoryCache cache)
    {
        _options = options.Value;
        _pipeline = pipelineProvider.GetPipeline("payment-gateway");
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;
        _cache = cache;
    }

    // ── Status translation ───────────────────────────────────────────────────

    /// <summary>
    /// The single place where PayHere status codes become domain statuses.
    /// Nothing outside this adapter may branch on these raw values.
    /// </summary>
    private static PaymentStatus MapStatus(string? payHereStatusCode) => payHereStatusCode switch
    {
        "2" => PaymentStatus.Completed,
        "0" => PaymentStatus.Pending,
        "-1" => PaymentStatus.Cancelled,
        "-2" => PaymentStatus.Failed,
        "-3" => PaymentStatus.Chargeback,
        _ => PaymentStatus.Unknown
    };

    // ── Payments ─────────────────────────────────────────────────────────────

    public async Task<PaymentIntentResult> CreatePaymentIntent(
        TransactionId id,
        Money amount,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct,
        string? paymentMethodToken = null)
    {
        var meta = metadata ?? new Dictionary<string, string>();
        var orderId = meta.TryGetValue("order_id", out var metaOrderId) ? metaOrderId : id.Value.ToString();
        var amountStr = amount.Value.ToString("0.00", CultureInfo.InvariantCulture);

        // --- Instant charge against a stored card token ---
        if (!string.IsNullOrEmpty(paymentMethodToken))
        {
            _logger.LogInformation("Payment method token present. Attempting instant charge for Order {OrderId}", orderId);

            var accepted = await ChargeCustomerAsync(orderId, amountStr, amount.Currency, paymentMethodToken, meta, ct);
            if (!accepted)
            {
                _logger.LogWarning("Instant charge rejected by PayHere for Order {OrderId}.", orderId);
                throw new InvalidOperationException($"PayHere rejected the instant charge for order {orderId}.");
            }

            // PayHere accepted the charge request, but the final outcome still arrives by webhook.
            return new PaymentIntentResult(
                Provider: Provider,
                Action: CheckoutAction.None,
                Status: PaymentStatus.Pending,
                Url: null,
                Fields: new Dictionary<string, string>
                {
                    ["order_id"] = orderId,
                    ["amount"] = amountStr,
                    ["currency"] = amount.Currency
                });
        }

        // --- Hosted checkout: PayHere requires a signed hidden-form POST ---
        var hash = PayHereSecurity.GenerateHash(
            _options.MerchantId, orderId, amountStr, amount.Currency, _options.MerchantSecret);

        var fields = new Dictionary<string, string>
        {
            ["merchant_id"] = _options.MerchantId,
            ["return_url"] = _options.ReturnUrl,
            ["cancel_url"] = _options.CancelUrl,
            ["notify_url"] = _options.CheckoutNotifyUrl,
            ["order_id"] = orderId,
            ["items"] = meta.GetValueOrDefault("items", "Payment"),
            ["amount"] = amountStr,
            ["currency"] = amount.Currency,
            ["hash"] = hash,
            ["first_name"] = meta.GetValueOrDefault("first_name", ""),
            ["last_name"] = meta.GetValueOrDefault("last_name", ""),
            ["email"] = meta.GetValueOrDefault("email", ""),
            ["address"] = meta.GetValueOrDefault("address", ""),
            ["city"] = meta.GetValueOrDefault("city", ""),
            ["country"] = meta.GetValueOrDefault("country", "")
        };

        // PayHere echoes custom_1/custom_2 back on the webhook — use them to carry our metadata.
        if (meta.TryGetValue("user_id", out var userId)) fields["custom_1"] = userId;
        if (meta.TryGetValue("email", out var email)) fields["custom_2"] = email;

        return new PaymentIntentResult(
            Provider: Provider,
            Action: CheckoutAction.FormPost,
            Status: PaymentStatus.Pending,
            Url: _options.CheckoutUrl,
            Fields: fields);
    }

    // ── Card setup (PayHere calls this "preapproval") ────────────────────────

    public Task<CardSetupResult> InitiateCardSetup(
        string orderId,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct)
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
            ["hash"] = hash
        };

        return Task.FromResult(new CardSetupResult(
            Provider: Provider,
            Action: CheckoutAction.FormPost,
            Url: _options.PreapprovalUrl,
            Fields: fields));
    }

    // ── Webhooks ─────────────────────────────────────────────────────────────

    public Task<WebhookResult> HandleWebhook(
        string payload,
        IDictionary<string, string> headers,
        CancellationToken ct)
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
            return Task.FromResult(WebhookResult.Invalid(orderId ?? "", "Missing required fields", statusCode));
        }

        var signatureValid = PayHereSecurity.VerifyNotificationSignature(
            merchantId!, orderId!, amount!, currency!, statusCode!, _options.MerchantSecret, remoteSig!);

        var customerToken = form.GetValueOrDefault("customer_token", "");
        var paymentId = form.GetValueOrDefault("payment_id", "");

        // PayHere uses the same notification format for both flows. A preapproval
        // notification carries a customer_token but no payment_id.
        var eventType = !string.IsNullOrEmpty(customerToken) && string.IsNullOrEmpty(paymentId)
            ? WebhookEventType.CardSetup
            : WebhookEventType.Payment;

        var card = string.IsNullOrEmpty(customerToken)
            ? null
            : new CardDetails(
                Token: customerToken,
                HolderName: form.GetValueOrDefault("card_holder_name", ""),
                MaskedNumber: form.GetValueOrDefault("card_no", ""),
                Expiry: form.GetValueOrDefault("card_expiry", ""),
                Brand: form.GetValueOrDefault("method", ""));

        // custom_1/custom_2 are PayHere's passthrough slots — unpack them into neutral metadata.
        var metadata = new Dictionary<string, string>();
        var custom1 = form.GetValueOrDefault("custom_1", "");
        var custom2 = form.GetValueOrDefault("custom_2", "");
        if (!string.IsNullOrEmpty(custom1)) metadata["user_id"] = custom1;
        if (!string.IsNullOrEmpty(custom2)) metadata["email"] = custom2;

        decimal? parsedAmount = decimal.TryParse(amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
            ? a
            : null;

        return Task.FromResult(new WebhookResult(
            SignatureValid: signatureValid,
            EventType: eventType,
            Status: MapStatus(statusCode),
            OrderId: orderId ?? string.Empty,
            ProviderReference: string.IsNullOrEmpty(paymentId) ? null : paymentId,
            Amount: parsedAmount,
            Currency: currency,
            Card: card,
            Metadata: metadata,
            RawStatus: statusCode));
    }

    // ── Refunds ──────────────────────────────────────────────────────────────

    public async Task<RefundResult> RefundAsync(
        string providerRefId,
        decimal? amount,
        string currency,
        string description,
        CancellationToken ct)
    {
        try
        {
            var accessToken = await GetAccessTokenAsync(ct);

            _logger.LogInformation("Initiating refund for Payment ID: {PaymentId}, Amount: {Amount}",
                providerRefId, amount?.ToString(CultureInfo.InvariantCulture) ?? "FULL");

            var request = new PayHereRefundRequestDto
            {
                PaymentId = providerRefId,
                Description = description,
                // PayHere treats a zero/omitted amount as a full refund.
                Amount = amount ?? 0m,
                Currency = currency
            };

            var requestJson = JsonSerializer.Serialize(request);

            var response = await _pipeline.ExecuteAsync(async token =>
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.RefundUrl)
                {
                    Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json"),
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
                return new RefundResult(false, "FAILED", null, $"HTTP {response.StatusCode}: {content}");
            }

            var refundResponse = JsonSerializer.Deserialize<PayHereRefundResponseDto>(
                content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (refundResponse == null)
            {
                return new RefundResult(false, "FAILED", null, "Failed to parse PayHere response");
            }

            return new RefundResult(
                IsSuccess: refundResponse.IsSuccess,
                Status: refundResponse.GlobalStatus,
                ProviderRefundId: refundResponse.ProviderRefundId,
                ErrorMessage: refundResponse.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RefundAsync failed after retries for Payment ID: {PaymentId}", providerRefId);
            return new RefundResult(false, "FAILED", null, ex.Message);
        }
    }

    // ── PayHere internals ────────────────────────────────────────────────────

    private async Task<bool> ChargeCustomerAsync(
        string orderId,
        string amount,
        string currency,
        string customerToken,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken ct)
    {
        try
        {
            var accessToken = await GetAccessTokenAsync(ct);

            var payload = new
            {
                order_id = orderId,
                items = metadata.GetValueOrDefault("items", "Payment"),
                currency,
                amount = decimal.Parse(amount, CultureInfo.InvariantCulture),
                customer_token = customerToken,
                notify_url = _options.InstantPayNotifyUrl,
                custom_1 = metadata.GetValueOrDefault("user_id"),
                custom_2 = metadata.GetValueOrDefault("email")
            };

            var response = await _pipeline.ExecuteAsync(async token =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, _options.ChargeUrl)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json"),
                    Headers = { { "Authorization", $"Bearer {accessToken}" } }
                };

                var res = await _httpClient.SendAsync(request, token);

                if ((int)res.StatusCode >= 500 || res.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    res.EnsureSuccessStatusCode();
                }

                return res;
            }, ct);

            var content = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("PayHere Charge Response: {Response}", content);

            if (!response.IsSuccessStatusCode) return false;

            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.TryGetProperty("status", out var statusProp)
                   && statusProp.GetInt32() == 1;
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
            return cachedToken!;
        }

        // Slow path — serialize concurrent fetches so only one thread calls PayHere OAuth
        await _tokenLock.WaitAsync(ct);
        try
        {
            // Double-check: another thread may have fetched while we waited for the lock
            if (_cache.TryGetValue(cacheKey, out cachedToken))
            {
                return cachedToken!;
            }

            _logger.LogInformation("Fetching new PayHere access token...");
            var authToken = Convert.ToBase64String(
                System.Text.Encoding.ASCII.GetBytes($"{_options.AppId}:{_options.AppSecret}"));

            var response = await _pipeline.ExecuteAsync(async token =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, _options.OAuthTokenUrl)
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

            _cache.Set(cacheKey, newToken, new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(50)));

            return newToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static Dictionary<string, string> ParseForm(string payload)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(payload)) return dict;

        foreach (var p in payload.Split('&'))
        {
            var kv = p.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0] ?? "");
            var val = kv.Length == 2 ? Uri.UnescapeDataString(kv[1]) : "";
            if (!string.IsNullOrEmpty(key)) dict[key] = val;
        }
        return dict;
    }
}
