using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentPortifolio.Core.Domain;

/// <summary>One SharePoint document library, addressed only by identifiers, never by path or URL.</summary>
public sealed record SharePointLibrary(string SiteId, string DriveId)
{
    public string Canonical => $"sharepoint:site/{SiteId}/drive/{DriveId}";
}

/// <summary>
/// Accepts a resource only when it is already in canonical form. Nothing is
/// normalised: an input that would need trimming, case folding or decoding is
/// rejected, because two spellings of one resource are an authorisation bug.
/// The identifier shapes are assumptions to confirm in the pilot tenant.
/// </summary>
public static partial class ResourceCanonicalizer
{
    private const string Guid = "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}";

    [GeneratedRegex(@"\A[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+," + Guid + "," + Guid + @"\z")]
    private static partial Regex SiteId();

    [GeneratedRegex(@"\A[A-Za-z0-9!_-]{1,256}\z")]
    private static partial Regex OpaqueId();

    [GeneratedRegex(@"\Asharepoint:site/(?<site>[^/]+)/drive/(?<drive>[^/]+)\z")]
    private static partial Regex CanonicalLibrary();

    public static bool IsOpaqueId(string? value) => value is not null && OpaqueId().IsMatch(value);

    public static bool TryLibrary(string? siteId, string? driveId, out SharePointLibrary library)
    {
        library = null!;
        if (siteId is null || driveId is null || !SiteId().IsMatch(siteId) || !OpaqueId().IsMatch(driveId))
        {
            return false;
        }

        library = new SharePointLibrary(siteId, driveId);
        return true;
    }

    public static bool TryParseCanonical(string? canonical, out SharePointLibrary library)
    {
        library = null!;
        if (canonical is null)
        {
            return false;
        }

        var match = CanonicalLibrary().Match(canonical);
        return match.Success && TryLibrary(match.Groups["site"].Value, match.Groups["drive"].Value, out library);
    }
}

public sealed record ExecutionRequest(string Operation, IReadOnlyDictionary<string, string> Arguments, string IdempotencyKey);

public sealed record ResolvedOperation(string Operation, SharePointLibrary Library, string? ItemId, string ArgumentsDigest)
{
    public string CanonicalResource => Library.Canonical;
}

/// <summary>
/// Closed list of semantic operations. There is deliberately no generic HTTP
/// proxy, no caller-supplied URL and no batch operation.
/// </summary>
public static partial class OperationCatalog
{
    public const string DocumentRead = "sharepoint.document.read";
    public const string DocumentList = "sharepoint.document.list";

    private static readonly Dictionary<string, string[]> RequiredArguments = new(StringComparer.Ordinal)
    {
        [DocumentRead] = ["siteId", "driveId", "itemId"],
        [DocumentList] = ["siteId", "driveId"],
    };

    [GeneratedRegex(@"\A[A-Za-z0-9_-]{8,128}\z")]
    private static partial Regex IdempotencyKey();

    public static bool IsAllowlisted(string? operation) => operation is not null && RequiredArguments.ContainsKey(operation);

    public static bool TryResolve(ExecutionRequest request, out ResolvedOperation resolved, out string reasonCode)
    {
        resolved = null!;
        if (request.Operation is null || !RequiredArguments.TryGetValue(request.Operation, out var required))
        {
            reasonCode = ReasonCodes.OperationNotAllowlisted;
            return false;
        }

        if (request.IdempotencyKey is null || !IdempotencyKey().IsMatch(request.IdempotencyKey)
            || request.Arguments is null || request.Arguments.Count != required.Length
            || required.Any(name => !request.Arguments.ContainsKey(name)))
        {
            reasonCode = ReasonCodes.ArgumentsInvalid;
            return false;
        }

        if (!ResourceCanonicalizer.TryLibrary(request.Arguments["siteId"], request.Arguments["driveId"], out var library))
        {
            reasonCode = ReasonCodes.ResourceNotCanonical;
            return false;
        }

        string? itemId = null;
        if (request.Operation == DocumentRead)
        {
            itemId = request.Arguments["itemId"];
            if (!ResourceCanonicalizer.IsOpaqueId(itemId))
            {
                reasonCode = ReasonCodes.ResourceNotCanonical;
                return false;
            }
        }

        reasonCode = ReasonCodes.Ok;
        resolved = new ResolvedOperation(request.Operation, library, itemId, Digest(request.Operation, request.Arguments));
        return true;
    }

    /// <summary>The ledger keeps this digest instead of the raw arguments (data minimisation).</summary>
    private static string Digest(string operation, IReadOnlyDictionary<string, string> arguments)
    {
        var text = new StringBuilder(operation);
        foreach (var pair in arguments.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            text.Append('\n').Append(pair.Key).Append('=').Append(pair.Value);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }
}
