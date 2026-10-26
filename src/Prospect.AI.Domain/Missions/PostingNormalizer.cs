using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Prospect.AI.Domain.Missions;

/// <summary>
/// Turns collected postings into stable keys used for deduplication:
/// a canonical URL (same page = same string) and a content hash (same text = same hash).
/// </summary>
public static partial class PostingNormalizer
{
    // Query parameters that only track the visitor and never change the page content.
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "gclid", "fbclid", "msclkid", "igshid", "mc_cid", "mc_eid", "_hsenc", "_hsmi",
        // LinkedIn
        "trk", "trkInfo", "refId", "trackingId", "lipi", "eBP", "originalSubdomain",
    };

    /// <summary>
    /// Normalizes a posting URL: https scheme, lowercase host without "www.", no default port,
    /// no fragment, no tracking parameters, remaining parameters sorted, no trailing slash.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not an absolute http(s) URL.</exception>
    public static string CanonicalizeUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"'{url}' is not an absolute http(s) URL.", nameof(url));
        }

        var host = uri.IdnHost.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host["www.".Length..];
        }

        var port = uri.IsDefaultPort || uri.Port is 80 or 443 ? string.Empty : $":{uri.Port}";

        var path = uri.AbsolutePath;
        if (path.Length > 1)
        {
            path = path.TrimEnd('/');
        }

        var query = CanonicalizeQuery(uri.Query);

        return $"https://{host}{port}{(path == "/" ? string.Empty : path)}{query}";
    }

    /// <summary>
    /// SHA-256 (lowercase hex, 64 characters) of the title and content, after removing HTML,
    /// decoding entities, and normalizing case, Unicode form and whitespace, so cosmetic
    /// differences between two copies of a posting do not change the hash.
    /// </summary>
    public static string ComputeContentHash(string title, string content)
    {
        var normalized = $"{NormalizeText(title)}\n{NormalizeText(content)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(hash);
    }

    private static string CanonicalizeQuery(string query)
    {
        if (query.Length <= 1)
        {
            return string.Empty;
        }

        var parameters = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(parameter => !IsTrackingParameter(ParameterName(parameter)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        return parameters.Length == 0 ? string.Empty : "?" + string.Join('&', parameters);
    }

    private static string ParameterName(string parameter)
    {
        var separator = parameter.IndexOf('=');
        return Uri.UnescapeDataString(separator < 0 ? parameter : parameter[..separator]);
    }

    private static bool IsTrackingParameter(string name) =>
        name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) || TrackingParameters.Contains(name);

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var withoutBlocks = ScriptOrStyleBlock().Replace(text, " ");
        var withoutTags = HtmlTag().Replace(withoutBlocks, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var unicode = decoded.Normalize(NormalizationForm.FormKC).ToLower(CultureInfo.InvariantCulture);

        return Whitespace().Replace(unicode, " ").Trim();
    }

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyleBlock();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
