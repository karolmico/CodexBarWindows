namespace CodexBar.Core.Identity;

/// <summary>Account metadata for one provider. Never merge identity across providers.</summary>
public sealed record ProviderIdentity
{
    public ProviderIdentity(string? planName = null, string? accountLabel = null, string? organization = null)
    {
        this.PlanName = Normalize(planName);
        this.AccountLabel = Normalize(accountLabel);
        this.Organization = Normalize(organization);
    }

    public string? PlanName { get; }

    public string? AccountLabel { get; }

    public string? Organization { get; }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
