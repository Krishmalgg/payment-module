# Payment Status Notification Architecture

## Overview

This document explains the architecture for notifying external systems (like PaperMaker) about payment status changes using the **Outbox Pattern** with **Hexagonal Architecture** principles.

---

## Architecture Components

### 1. **IPaymentStatusNotifier** (Interface/Port)

- **Location**: `Communication/Features/NotifyPaymentStatus/IPaymentStatusNotifier.cs`
- **Purpose**: Defines the contract for sending payment status notifications
- **Type**: Port (Abstraction)
- **Why it exists**:
  - Allows the Application layer to remain independent of infrastructure details
  - Makes testing easier (you can mock this interface)
  - Follows Dependency Inversion Principle

```csharp
public interface IPaymentStatusNotifier
{
    Task NotifyAsync(PaymentStatusPayload payload, CancellationToken cancellationToken = default);
}
```

### 2. **PaymentStatusNotifier** (Implementation/Adapter)

- **Location**: `Communication/Features/NotifyPaymentStatus/PaymentStatusNotifier.cs`
- **Purpose**: Implements the notification logic using the Outbox pattern
- **Type**: Adapter (Concrete Implementation)
- **How it works**:
  1. Receives a `PaymentStatusPayload`
  2. Serializes it to JSON
  3. Adds it to the Outbox table via `IOutboxService`
  4. Returns immediately (non-blocking)

```csharp
public class PaymentStatusNotifier : IPaymentStatusNotifier
{
    private readonly IOutboxService _outboxService;

    public async Task NotifyAsync(PaymentStatusPayload payload, CancellationToken ct)
    {
        var payloadJson = JsonSerializer.Serialize(payload);
        await _outboxService.AddMessageAsync("PaymentStatus", payloadJson, ct);
    }
}
```

### 3. **PaymentStatusPayload** (DTO)

- **Location**: `Communication/Features/NotifyPaymentStatus/PaymentStatusPayload.cs`
- **Purpose**: Data Transfer Object containing payment status information
- **Fields**:
  - TransactionId
  - OrderId
  - Amount, Currency
  - UserId, UserEmail, FullName
  - Status (PENDING, COMPLETED, FAILED, SUSPICIOUS)
  - ProviderReference
  - OccurredAt

---

## How It Works (Flow)

### Step 1: Business Logic Triggers Notification

When a payment status changes (e.g., in `ProcessPayHereWebhookCommandHandler`):

```csharp
await _paymentStatusNotifier.NotifyAsync(new PaymentStatusPayload
{
    TransactionId = transaction.Id,
    Status = transaction.Status,
    // ... other fields
}, ct);
```

### Step 2: Notification Added to Outbox

The `PaymentStatusNotifier` adds the message to the `OutboxMessages` table:

| Id  | Type          | Payload                                         | ProcessedAt | RetryCount |
| --- | ------------- | ----------------------------------------------- | ----------- | ---------- |
| 123 | PaymentStatus | {"transactionId": "...", "status": "COMPLETED"} | NULL        | 0          |

### Step 3: Background Service Processes Outbox

The `OutboxBackgroundService` runs continuously:

1. Fetches unprocessed messages from the Outbox
2. Uses `ProducerFactory` to get the configured producer (HTTP or RabbitMQ)
3. Sends the message via the producer
4. Marks the message as processed if successful
5. Retries if failed (with delay)

```csharp
// In OutboxBackgroundService.cs
var producer = _producerFactory.GetProducer(); // Gets HTTP or RabbitMQ based on config
var result = await producer.SendAsync(destination, message.Payload, cancellationToken);

if (result.Success)
{
    await outboxService.ProcessMessageAsync(messageId, cancellationToken);
}
```

### Step 4: Producer Sends Message

Depending on configuration (`appsettings.json`):

**HTTP Producer**:

- Sends POST request to configured URL
- Includes S2S security headers
- Returns `ProducerResult` with success/failure

**RabbitMQ Producer**:

- Publishes message to configured queue
- Uses publisher confirms for reliability
- Returns `ProducerResult` with success/failure

---

## Key Differences: IPaymentStatusNotifier vs PaymentStatusNotifier

| Aspect           | IPaymentStatusNotifier            | PaymentStatusNotifier                |
| ---------------- | --------------------------------- | ------------------------------------ |
| **Type**         | Interface (Port)                  | Class (Adapter)                      |
| **Purpose**      | Defines WHAT to do                | Defines HOW to do it                 |
| **Location**     | Application layer depends on this | Infrastructure layer implements this |
| **Testing**      | Easy to mock                      | Concrete implementation              |
| **Dependencies** | None (just a contract)            | Depends on IOutboxService            |

---

## Why This Architecture?

### 1. **Separation of Concerns**

- Business logic (`ProcessPayHereWebhookCommandHandler`) doesn't know about HTTP/RabbitMQ
- It only knows "I need to notify about a payment status change"

### 2. **Testability**

- You can mock `IPaymentStatusNotifier` in unit tests
- No need to set up HTTP clients or RabbitMQ in tests

### 3. **Flexibility**

- Want to switch from HTTP to RabbitMQ? Just change the config
- Want to add a new notification type? Create a new feature folder

### 4. **Reliability (Outbox Pattern)**

- Messages are saved to the database first
- Even if the external service is down, messages won't be lost
- Automatic retries with exponential backoff

### 5. **Hexagonal Architecture**

- **Core (Domain/Application)**: Depends on `IPaymentStatusNotifier` (port)
- **Infrastructure**: Implements `PaymentStatusNotifier` (adapter)
- **Dependency flows inward**: Infrastructure depends on Core, not vice versa

---

## Configuration

### appsettings.json

```json
{
  "Messaging": {
    "Strategy": "Http", // or "RabbitMq"
    "Http": {
      "NotificationUrl": "http://localhost:5201/api/v1/notifications/payment-status"
    },
    "RabbitMq": {
      "HostName": "localhost",
      "Port": 5672
    },
    "Parameters": {
      "QueueName": "payment.notifications"
    }
  }
}
```

---

## Comparison with OutboxBackgroundService

### OutboxBackgroundService

- **Generic**: Works for ALL outbox messages (PaymentStatus, SuspiciousActivity, etc.)
- **Infrastructure**: Background worker that processes the outbox
- **Responsibility**: Reliable delivery of messages

### PaymentStatusNotifier

- **Feature-Specific**: Only for payment status notifications
- **Application-facing**: Used by business logic
- **Responsibility**: Creating payment status messages

### They Work Together!

```
Business Logic → PaymentStatusNotifier → Outbox Table → OutboxBackgroundService → Producer (HTTP/RabbitMQ) → External System
```

---

## Example Usage

### In Your Command Handler

```csharp
public class ProcessPayHereWebhookCommandHandler
{
    private readonly IPaymentStatusNotifier _paymentStatusNotifier;

    public async Task<object> Handle(ProcessPayHereWebhookCommand request, CancellationToken ct)
    {
        // ... process webhook ...

        if (transaction.Status != previousStatus)
        {
            await _dbContext.SaveChangesAsync(ct);

            // Notify external systems
            await _paymentStatusNotifier.NotifyAsync(new PaymentStatusPayload
            {
                TransactionId = transaction.Id,
                Status = transaction.Status,
                // ... other fields
            }, ct);
        }
    }
}
```

---

## Benefits Summary

✅ **Reliable**: Messages are persisted before sending  
✅ **Decoupled**: Business logic doesn't know about HTTP/RabbitMQ  
✅ **Testable**: Easy to mock interfaces  
✅ **Flexible**: Switch protocols via configuration  
✅ **Maintainable**: Clear separation of concerns  
✅ **Scalable**: Background processing doesn't block requests

---

## Common Questions

**Q: Why not call the producer directly from the command handler?**  
A: That would couple your business logic to infrastructure details and make testing harder.

**Q: Why have both IPaymentStatusNotifier and IOutboxService?**  
A: `IOutboxService` is generic (works for any message type). `IPaymentStatusNotifier` is feature-specific and provides type safety.

**Q: Can I send messages without the Outbox?**  
A: Yes, but you lose reliability. If the external service is down or the network fails, your message is lost.

**Q: What if I want to add a new notification type?**  
A: Create a new feature folder (e.g., `NotifyRefundStatus`) with its own interface, implementation, and payload DTO.
