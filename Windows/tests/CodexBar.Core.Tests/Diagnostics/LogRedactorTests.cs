using CodexBar.Core.Diagnostics;
using Xunit;

namespace CodexBar.Core.Tests.Diagnostics;

public sealed class LogRedactorTests
{
    [Theory]
    [InlineData("Authorization: Bearer abc123.def456", "abc123.def456")]
    [InlineData("authorization=Basic dXNlcjpwYXNz", "dXNlcjpwYXNz")]
    [InlineData("Cookie: session=abc123; theme=dark", "session=abc123")]
    [InlineData("Set-Cookie: token=zzz; Path=/", "zzz")]
    [InlineData("""{"access_token":"ya29.secret","refresh_token":"1//refresh"}""", "ya29.secret")]
    [InlineData("""{"access_token":"ya29.secret","refresh_token":"1//refresh"}""", "1//refresh")]
    [InlineData("""{"cookie":"session=abc123","theme":"dark"}""", "session=abc123")]
    [InlineData("api_key=AIzaSyDummyKeyValue", "AIzaSyDummyKeyValue")]
    [InlineData("password=hunter2", "hunter2")]
    [InlineData("https://example.test/cb?token=abc123&state=ok", "abc123")]
    [InlineData("https://example.test/cb?code=oauth-code-1&state=ok", "oauth-code-1")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.c2lnbmF0dXJlLXZhbHVl", "eyJhbGciOiJIUzI1NiJ9")]
    [InlineData("token ghp_abcdefghijklmnopqrstuvwxyz0123456789", "ghp_abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("key sk-proj-abcdefghijklmnopqrstuvwxyz", "sk-proj-abcdefghijklmnopqrstuvwxyz")]
    [InlineData("slack xoxb-123456789012-abcdefghij", "xoxb-123456789012-abcdefghij")]
    [InlineData("aws AKIAIOSFODNN7EXAMPLE", "AKIAIOSFODNN7EXAMPLE")]
    [InlineData("mailed dev.user+tag@example.co.uk", "dev.user+tag@example.co.uk")]
    [InlineData(@"opened C:\Users\karol\AppData\Roaming\CodexBar\settings.json", "karol")]
    [InlineData(@"read \\fileserver\share\secret.txt", "fileserver")]
    [InlineData("home /home/karol/.codex/auth.json", "karol")]
    public void Secret_shaped_values_disappear(string input, string secret)
    {
        var redacted = LogRedactor.Redact(input);

        Assert.DoesNotContain(secret, redacted, StringComparison.Ordinal);
        Assert.NotEqual(input, redacted);
    }

    [Fact]
    public void Redaction_names_what_it_removed()
    {
        var redacted = LogRedactor.Redact(
            "Authorization: Bearer abc123 mail dev@example.com path C:\\Users\\dev\\auth.json");

        Assert.Contains("Bearer [redacted]", redacted, StringComparison.Ordinal);
        Assert.Contains("[email]", redacted, StringComparison.Ordinal);
        Assert.Contains("[path]", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Ordinary_usage_text_is_untouched()
    {
        const string Safe = "2026-10-27T12:00:00.000Z [INFO] codex fetch ok in 120 ms; session 42% used";

        Assert.Equal(Safe, LogRedactor.Redact(Safe));
    }

    [Fact]
    public void Redaction_is_idempotent()
    {
        const string Input = "Authorization: Bearer abc123 for dev@example.com at C:\\Users\\dev\\auth.json";

        var once = LogRedactor.Redact(Input);

        Assert.Equal(once, LogRedactor.Redact(once));
    }

    [Fact]
    public void Null_and_empty_values_are_safe() =>
        Assert.Empty(LogRedactor.Redact(null));
}
