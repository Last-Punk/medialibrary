using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace MediaLibrary.Api.Security;

/// <summary>Builds stable rate-limit buckets without trusting caller-supplied headers.</summary>
public static class RateLimitPartitionKeys
{
    /// <summary>Uses the validated JWT subject, falling back to the socket peer's IP address.</summary>
    public static string UserOrIp(HttpContext context)
    {
        var subject = context.User.FindFirst("sub")?.Value;
        if (int.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
            && userId > 0)
        {
            return $"user:{userId.ToString(CultureInfo.InvariantCulture)}";
        }

        return Ip(context);
    }

    /// <summary>Uses the socket peer IP for anonymous endpoints such as Telegram authentication.</summary>
    public static string Ip(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
            return "ip:unknown";

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        return $"ip:{address}";
    }
}
