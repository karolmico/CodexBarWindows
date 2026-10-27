using CodexBar.Core.Usage;

namespace CodexBar.Core.Providers;

/// <summary>Why a provider could not produce a snapshot. Drives user-facing copy and retry behaviour.</summary>
public enum ProviderErrorKind
{
    NotConfigured,
    AuthenticationRequired,
    RateLimited,
    Network,
    Parse,
    Unknown,
}

public sealed record ProviderError
{
    public ProviderError(ProviderErrorKind kind, string message, TimeSpan? retryAfter = null, Exception? cause = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("A provider error needs a message.", nameof(message));
        }

        this.Kind = kind;
        this.Message = message.Trim();
        this.RetryAfter = retryAfter;
        this.Cause = cause;
    }

    public ProviderErrorKind Kind { get; }

    public string Message { get; }

    public TimeSpan? RetryAfter { get; }

    public Exception? Cause { get; }

    public static ProviderError NotConfigured(string message) => new(ProviderErrorKind.NotConfigured, message);

    public static ProviderError AuthenticationRequired(string message) =>
        new(ProviderErrorKind.AuthenticationRequired, message);
}

/// <summary>Result of one provider fetch. Expected failures are values, never exceptions.</summary>
public abstract record UsageFetchResult
{
    private UsageFetchResult()
    {
    }

    public sealed record Success(UsageSnapshot Snapshot) : UsageFetchResult;

    public sealed record Failure(ProviderError Error) : UsageFetchResult;
}

/// <summary>Static presentation facts about a provider, independent of any fetch.</summary>
public sealed record ProviderDescriptor
{
    public ProviderDescriptor(ProviderKey key, string displayName, bool enabledByDefault = true)
    {
        this.Key = key ?? throw new ArgumentNullException(nameof(key));
        this.DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? throw new ArgumentException("A provider needs a display name.", nameof(displayName))
            : displayName.Trim();
        this.EnabledByDefault = enabledByDefault;
    }

    public ProviderKey Key { get; }

    public string DisplayName { get; }

    public bool EnabledByDefault { get; }
}

/// <summary>The single seam every provider implements.</summary>
public interface IUsageProvider
{
    ProviderDescriptor Descriptor { get; }

    Task<UsageFetchResult> FetchAsync(CancellationToken cancellationToken);
}
