using Microsoft.Extensions.Logging.Abstractions;
using PaymentModule.Infrastructure.Gateways.Adapters;
using Xunit;

namespace PaymentModule.SecurityTests;

/// <summary>
/// Guards the other half of Gap 1.
///
/// The controller now hands adapters the raw application/x-www-form-urlencoded body
/// instead of a re-encoded copy. In that encoding a space is '+' and a literal plus is
/// '%2B' — so decoding with Uri.UnescapeDataString (which ignores '+') would silently
/// corrupt names and e-mail addresses while signature checks still passed.
/// </summary>
public class FormDecodingTests
{
    private static MockAdapter Adapter() => new(NullLogger<MockAdapter>.Instance);

    [Fact]
    public async Task Decodes_plus_as_a_space()
    {
        var result = await Adapter().HandleWebhook(
            "order_id=ORD-1&custom_name=John+Smith",
            new Dictionary<string, string>(), CancellationToken.None);

        Assert.Equal("John Smith", result.Meta["custom_name"]);
    }

    [Fact]
    public async Task Decodes_an_escaped_plus_as_a_literal_plus()
    {
        // A tagged e-mail address is the realistic case: "a+tag@example.com" arrives
        // as "a%2Btag@example.com" and must survive intact.
        var result = await Adapter().HandleWebhook(
            "order_id=ORD-1&email=a%2Btag%40example.com",
            new Dictionary<string, string>(), CancellationToken.None);

        Assert.Equal("a+tag@example.com", result.Meta["email"]);
    }

    [Fact]
    public async Task Decodes_percent_escapes_case_insensitively()
    {
        var result = await Adapter().HandleWebhook(
            "order_id=ORD-1&a=x%2fy&b=x%2Fy",
            new Dictionary<string, string>(), CancellationToken.None);

        Assert.Equal("x/y", result.Meta["a"]);
        Assert.Equal("x/y", result.Meta["b"]);
    }

    [Fact]
    public async Task Handles_values_containing_equals_signs()
    {
        // Base64 padding makes this common in real signatures.
        var result = await Adapter().HandleWebhook(
            "order_id=ORD-1&sig=abc==",
            new Dictionary<string, string>(), CancellationToken.None);

        Assert.Equal("abc==", result.Meta["sig"]);
    }

    [Fact]
    public async Task Handles_empty_and_missing_values()
    {
        var result = await Adapter().HandleWebhook(
            "order_id=ORD-1&empty=&novalue",
            new Dictionary<string, string>(), CancellationToken.None);

        Assert.Equal("", result.Meta["empty"]);
        Assert.Equal("", result.Meta["novalue"]);
    }
}
