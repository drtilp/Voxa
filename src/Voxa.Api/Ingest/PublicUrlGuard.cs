using System.Net;
using System.Net.Sockets;

namespace Voxa.Api.Ingest;

/// <summary>
/// The server downloads whatever URL a client sends, so the URL must point to the public internet: never to the
/// container itself, the Azure network or the metadata endpoint (169.254.169.254). Server-side request forgery guard,
/// checked for the page URL and again for the media URL yt-dlp found.
/// </summary>
/// <remarks>DNS can change between this check and the download (rebinding); ffmpeg is further limited to http(s).</remarks>
public sealed class PublicUrlGuard(Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve =
        resolve ?? ((host, ct) => Dns.GetHostAddressesAsync(host, ct));

    /// <returns>Null when the URL may be fetched, otherwise why not.</returns>
    public async Task<string?> CheckAsync(Uri url, CancellationToken ct = default)
    {
        if (url.Scheme is not ("http" or "https")) return "Only http and https URLs can be transcribed.";
        if (string.IsNullOrEmpty(url.Host)) return "The URL has no host.";

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(url.Host.Trim('[', ']'), out var literal) ? [literal] : await _resolve(url.Host, ct);
        }
        catch (SocketException)
        {
            return $"{url.Host} doesn't exist.";
        }

        return addresses.Length == 0 ? $"{url.Host} doesn't exist."
            : addresses.Any(a => !IsPublic(a)) ? $"{url.Host} is not on the public internet."
            : null;
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] is 0 or 10 or 127 or >= 224                 // this network, private, loopback, multicast/reserved
                     || b[0] == 100 && b[1] is >= 64 and <= 127       // carrier-grade NAT
                     || b[0] == 169 && b[1] == 254                    // link-local (cloud metadata)
                     || b[0] == 172 && b[1] is >= 16 and <= 31        // private
                     || b[0] == 192 && b[1] == 168                    // private
                     || b[0] == 192 && b[1] == 0 && b[2] == 0         // IETF protocol assignments
                     || b[0] == 198 && b[1] is 18 or 19);             // benchmarking
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6None)
                     || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                     || (b[0] & 0xFE) == 0xFC);                       // unique local fc00::/7
        }

        return false;
    }
}
