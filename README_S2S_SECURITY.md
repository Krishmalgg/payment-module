# S2S Security: API Authentication Guide

This document explains the Server-to-Server (S2S) security implementation for the Payment Module API.

## Overview

The Payment API uses **API Key + HMAC-SHA256 Signature** authentication to secure all payment intent requests. This ensures:

- **Authentication**: Only authorized servers can make requests
- **Integrity**: Request data cannot be tampered with
- **Replay Protection**: Requests expire after 5 minutes
- **Key Rotation**: Supports multiple active keys for zero-downtime updates

---

## 🔑 Receiver Side (Payment Server)

### Configuration

**Option 1: Environment Variables (.env file)**

```env
S2S_API_KEYS=pm_live_a1b2c3d4e5f6g7h8
S2S_HMAC_SECRETS=d41d8cd98f00b204e9800998ecf8427e3b435b6c7d8e9f0a1b2c3d4e5f6g7h8i
```

**Option 2: User Secrets (Development)**

```bash
dotnet user-secrets set "Security:ApiKeys:0" "pm_live_a1b2c3d4e5f6g7h8"
dotnet user-secrets set "Security:HmacSecrets:0" "secret_here"
```

### How it Works

The `S2SSecurityMiddleware` validates every request to `/api/v1/payments/intents`:

1. **Header Check**: Ensures `x-api-key`, `x-signature`, `x-timestamp`, `x-nonce` are present
2. **Key Verification**: Checks if `x-api-key` exists in configured `ApiKeys` list
3. **Timestamp Validation**: Rejects requests older than 5 minutes (replay protection)
4. **HMAC Verification**: Iterates through all `HmacSecrets` and accepts if ANY match

**Response**: `401 Unauthorized` if any check fails.

---

## 📤 Sender Side (PaperMaker Server)

### Required Headers

| Header        | Example                    | Description                         |
| ------------- | -------------------------- | ----------------------------------- |
| `x-api-key`   | `pm_live_a1b2c3d4e5f6g7h8` | Your API Key (shared with Receiver) |
| `x-timestamp` | `1703865000`               | Unix timestamp (seconds)            |
| `x-nonce`     | `a1b2c3d4-uuid`            | Unique random string per request    |
| `x-signature` | `abc123...`                | HMAC-SHA256 hex (see below)         |

### Generating the Signature

**Canonical String Format:**

```
{METHOD}{PATH}{TIMESTAMP}{NONCE}{BODY}
```

**Example:**

```
POST/api/v1/payments/intents1703865000a1b2c3d4-uuid{"amount":1500,...}
```

**C# Code:**

```csharp
var payload = $"{method.ToUpper()}{path}{timestamp}{nonce}{requestBodyJson}";
using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
var signature = Convert.ToHexString(hashBytes).ToLowerInvariant();
```

**TypeScript/Node.js Code:**

```typescript
import crypto from "crypto";

const payload = `${method.toUpperCase()}${path}${timestamp}${nonce}${requestBodyJson}`;
const hmac = crypto.createHmac("sha256", secret);
hmac.update(payload);
const signature = hmac.digest("hex").toLowerCase();
```

### Full Request Example (TypeScript)

```typescript
const timestamp = Math.floor(Date.now() / 1000).toString();
const nonce = crypto.randomUUID();
const body = JSON.stringify({ amount: 1500, currency: "LKR", ... });
const path = "/api/v1/payments/intents";
const method = "POST";

const payload = `${method}${path}${timestamp}${nonce}${body}`;
const signature = crypto.createHmac('sha256', HMAC_SECRET).update(payload).digest('hex');

await fetch('http://payment-server/api/v1/payments/intents', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'x-api-key': API_KEY,
    'x-timestamp': timestamp,
    'x-nonce': nonce,
    'x-signature': signature
  },
  body
});
```

---

## 🔄 Key Rotation Workflow

### Step 1: Add New Keys to Receiver

Update `.env` with comma-separated values:

```env
S2S_API_KEYS=old_key,new_key
S2S_HMAC_SECRETS=old_secret,new_secret
```

Restart the Payment Server.

✅ **Result**: Server now accepts BOTH old and new credentials.

### Step 2: Update Sender

Update PaperMaker's configuration to use the new key/secret.

✅ **Result**: Sender now uses new credentials. Receiver accepts them.

### Step 3: Remove Old Keys

Remove old credentials from Payment Server:

```env
S2S_API_KEYS=new_key
S2S_HMAC_SECRETS=new_secret
```

✅ **Result**: Only new credentials are valid.

---

## ✅ Testing

**Negative Test (Should Fail with 401):**

```bash
curl -X POST http://localhost:5000/api/v1/payments/intents -H "Content-Type: application/json" -d '{}'
```

**Positive Test (Should Succeed with 200):**
Use the code examples above with valid headers.

---

## 🔐 Security Best Practices

- **Never** commit `.env` files to version control
- Use environment variables or secret managers in production
- Rotate keys every 90 days
- Monitor for 401 errors after rotation (indicates misconfiguration)
- Use HTTPS in production to protect credentials in transit

---

## 📚 Reference Implementation

See `typescript_s2s_middleware_reference.ts` for a complete TypeScript/Express middleware example.
