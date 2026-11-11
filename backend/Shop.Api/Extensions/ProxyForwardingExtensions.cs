using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Shop.Api.Extensions;

/// <summary>
/// Trusted-proxy <c>X-Forwarded-*</c> handling (risk: per-IP rate limits
/// collapsing behind a reverse proxy).
/// <para>
/// Without this, a deployment behind nginx / a tunnel / an ingress sees
/// <c>RemoteIpAddress</c> as the proxy for every client, so the per-IP buckets
/// (<c>global</c>, <c>auth</c>) and the anonymous fallback of the per-user
/// buckets become one shared quota — the first burst locks everyone out.
/// </para>
/// <para>
/// Opt-in and allow-listed: a directly exposed API must not trust
/// <c>X-Forwarded-For</c>, or any caller could mint a fresh IP per request and
/// dodge the buckets. Enable with <c>Proxy:Enabled=true</c> and name the hops
/// you control in <c>Proxy:KnownProxies</c> (IPs, e.g. a load balancer) and/or
/// <c>Proxy:KnownNetworks</c> (CIDRs, e.g. a docker bridge). When both lists are
/// empty the loopback and RFC1918 ranges are trusted, which covers a single-host
/// docker-compose or LAN proxy; set them explicitly for anything else.
/// </para>
/// </summary>
public static class ProxyForwardingExtensions
{
    public const string SectionName = "Proxy";

    // Default trust list: proxy hops that cannot be reached from the internet.
    private static readonly string[] DefaultNetworkCidrs =
    [
        "127.0.0.0/8", "::1/128",
        "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16",
        "fc00::/7",
    ];

    /// <summary>
    /// Configures <see cref="ForwardedHeadersOptions"/> when
    /// <c>Proxy:Enabled</c> is true. Returns true when the pipeline must call
    /// <see cref="UseTrustedProxyForwarding"/>.
    /// </summary>
    public static bool AddTrustedProxyForwarding(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        if (!section.GetValue("Enabled", false))
            return false;

        var proxies = section.GetSection("KnownProxies").Get<string[]>() ?? [];
        var networks = section.GetSection("KnownNetworks").Get<string[]>() ?? [];
        if (proxies.Length == 0 && networks.Length == 0)
            networks = DefaultNetworkCidrs;

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // Exactly one hop: the proxy in front of us. A longer chain would
            // let a client prepend its own X-Forwarded-For entries.
            options.ForwardLimit = 1;

            // Defaults (loopback) are replaced by the explicit allow-list below.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var proxy in proxies)
            {
                if (IPAddress.TryParse(proxy, out var address))
                    options.KnownProxies.Add(address);
            }

            foreach (var cidr in networks)
            {
                if (System.Net.IPNetwork.TryParse(cidr, out var network))
                    options.KnownIPNetworks.Add(network);
            }
        });

        return true;
    }

    /// <summary>Rewrites RemoteIpAddress / Request.Scheme from the trusted hop.</summary>
    public static void UseTrustedProxyForwarding(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.Logger.LogInformation(
            "Forwarded headers enabled: per-IP rate-limit buckets key on the client IP " +
            "reported by the last hop (Proxy:KnownProxies / Proxy:KnownNetworks).");
    }
}
