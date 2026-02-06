# S2S Security Protocol Specification

To successfully communicate with the Main Server using S2S security, both the Sender (PaperMaker) and Receiver (Main Server) must implement the following specification exactly.

## 1. Required Headers

The request **MUST** include these headers:

- `x-api-key`: Your shared public identifier.
- `x-timestamp`: Current Unix timestamp (seconds). Request is valid for ±5 minutes.
- `x-nonce`: A unique random string (UUID recommended) for each request.
- `x-signature`: The computed HMAC-SHA256 hex string.

## 2. Signature Construction Algorithm

The signature is a **HMAC-SHA256** hash of a "Canonical String", converted to **lowercase hex**.

### A. Construct the Canonical String

Concatenate the following values strictly in this order **(NO SEPARATORS)**:

1.  **Method** (Upper Case)
    - Source: HTTP Method
    - Example: `POST`
2.  **Path + Query**
    - Source: The exact request path including query parameters.
    - Example: `/api/payments/card` OR `/api/payments/card?id=123`
    - _Note: Ensure this matches exactly what the framework sees as `Request.Path + Request.QueryString`._
3.  **Timestamp**
    - Source: `x-timestamp` header value.
    - Example: `1704556800`
4.  **Nonce**
    - Source: `x-nonce` header value.
    - Example: `abc123xyz789`
5.  **Body**
    - Source: The exact raw JSON body string.
    - Example: `{"amount":1500,"currency":"LKR"}`
    - _Critical: Must be the raw bytes sent over the wire. Do not re-serialize valid JSON, as whitespace differences will break the signature._

### B. Compute HMAC

1.  Take your **Secret Key** (e.g., `d41d8cd...`).
2.  Compute `HMACSHA256(SecretKeyBytes, CanonicalStringBytes)`.
3.  Convert the result to a **Lowercase Hex String**.

---

## 3. Implementation Logic (C# / .NET)

Copy this logic into your Main Server's middleware or controller filter.

```csharp
private bool ValidateSignature(HttpContext context, string secret)
{
    // 1. Gather Attributes
    var receivedSignature = context.Request.Headers["x-signature"].ToString();
    var timestamp = context.Request.Headers["x-timestamp"].ToString();
    var nonce = context.Request.Headers["x-nonce"].ToString();
    var apiKey = context.Request.Headers["x-api-key"].ToString();
    var method = context.Request.Method.ToUpperInvariant();

    // 2. Construct Path (Base + Path + Query)
    // IMPORTANT: Ensure this matches exactly what was signed by the client!
    var fullPath = context.Request.PathBase + context.Request.Path + context.Request.QueryString;

    // 3. Read Body strictly
    context.Request.EnableBuffering();
    context.Request.Body.Position = 0;
    using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
    var body = reader.ReadToEndAsync().Result;
    context.Request.Body.Position = 0; // Reset for next middleware

    // 4. Build Canonical String
    var payload = $"{method}{fullPath}{timestamp}{nonce}{body}";

    // 5. Compute Hash
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    var computedSignature = Convert.ToHexString(hashBytes).ToLowerInvariant();

    // 6. Compare (Constant Time)
    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(computedSignature),
        Encoding.UTF8.GetBytes(receivedSignature));
}
```

## 4. Verification Tool

You can use the provided Python script `src/generate_s2s_headers.py` to generate valid headers for testing your Main Server implementation.

**Example Usage:**

```bash
python src/generate_s2s_headers.py \
  --apikey "pm_live_a1b2c3d4e5f6g7h8" \
  --secret "d41d8cd98f00b204e..." \
  --method POST \
  --path "/api/payments/card" \
  --body "{\"amount\":1500,\"currency\":\"LKR\"}"
```
