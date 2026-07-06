namespace BillSplitter.Api.Configuration;

/// <summary>Named rate-limit policies. The full per-IP policy set lands in M7
/// (docs/10-security-privacy.md#rate-limits); M4 needs only the code-resolve
/// brute-force guard.</summary>
public static class RateLimitPolicies
{
    public const string CodeResolve = "code-resolve";
}
