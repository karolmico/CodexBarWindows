using CodexBar.Core.Providers;

namespace CodexBar.Core.Refresh;

/// <summary>Latest fetch cost for one provider; diagnostics only, never used for scheduling.</summary>
public sealed record ProviderTiming(ProviderKey Key, TimeSpan? LastFetchDuration, DateTimeOffset? LastFetchAt);
