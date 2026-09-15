namespace SchoolCollab.Assignments.Application.Services;

using System.Text;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

/// <summary>
/// WS-B2 (spec §3.4 / decision g) — <see cref="IUrlTextExtractor"/> backed by an
/// <c>url-fetcher</c> named <see cref="HttpClient"/> (5s timeout, NO tenant
/// propagation — external hosts). http/https only (fails fast otherwise), a 512 KB
/// response-body cap, HTML script/style/noscript stripped, whitespace collapsed,
/// and the result truncated to 20,000 chars. Non-success / timeout / network
/// failures fail open with a friendly <see cref="UrlTextExtractionResult.Error"/>.
/// </summary>
public sealed class UrlTextExtractor(
    IHttpClientFactory httpClientFactory,
    ILogger<UrlTextExtractor> logger) : IUrlTextExtractor
{
    private const int MaxBodyBytes = 512 * 1024;  // 512 KB
    private const int MaxTextLength = 20_000;      // 20,000 chars

    public async Task<UrlTextExtractionResult> ExtractAsync(string url, CancellationToken ct = default)
    {
        if (!IsHttpScheme(url))
        {
            return new UrlTextExtractionResult(false, null, "Only http and https URLs are supported.");
        }

        var client = httpClientFactory.CreateClient("url-fetcher");
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                return new UrlTextExtractionResult(false, null, $"The page returned {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var ms = new MemoryStream();
            var buffer = new byte[4096];
            var total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                if (total >= MaxBodyBytes) break;
                var chunk = Math.Min(read, MaxBodyBytes - total);
                ms.Write(buffer, 0, chunk);
                total += chunk;
            }

            ms.Position = 0;
            var html = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true).ReadToEnd();
            var text = StripToText(html);
            if (text.Length > MaxTextLength)
            {
                text = text.Substring(0, MaxTextLength);
            }
            return new UrlTextExtractionResult(true, text, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller cancelled — propagate.
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // The 5s client timeout fired — fail open.
            logger.LogWarning(ex, "URL extraction timed out for {Url}", url);
            return new UrlTextExtractionResult(false, null, "The page took too long to load.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "URL extraction failed for {Url}", url);
            return new UrlTextExtractionResult(false, null, "Couldn't reach that page.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unexpected URL extraction failure for {Url}", url);
            return new UrlTextExtractionResult(false, null, "Couldn't read content from that page.");
        }
    }

    private static bool IsHttpScheme(string url)
    {
        var trimmed = url.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripToText(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        foreach (var node in doc.DocumentNode.Descendants()
                     .Where(n => n.Name is "script" or "style" or "noscript")
                     .ToList())
        {
            node.Remove();
        }
        return CollapseWhitespace(doc.DocumentNode.InnerText);
    }

    private static string CollapseWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);
        var inWhitespace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                inWhitespace = true;
            }
            else
            {
                if (inWhitespace && sb.Length > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(ch);
                inWhitespace = false;
            }
        }
        return sb.ToString().Trim();
    }
}
