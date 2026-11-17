using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MediaLibrary.Api.Security;

namespace MediaLibrary.Api.Tests;

public sealed class RateLimitPartitionKeysTests
{
    [Fact]
    public void UserOrIp_UsesValidatedSubjectInsteadOfIpForAuthenticatedRequests()
    {
        var context = Request("192.0.2.10", "sub", "17");

        Assert.Equal("user:17", RateLimitPartitionKeys.UserOrIp(context));
    }

    [Fact]
    public void UserOrIp_SeparatesDifferentAuthenticatedUsersOnTheSameIp()
    {
        var alice = Request("192.0.2.10", "sub", "17");
        var bob = Request("192.0.2.10", "sub", "18");

        Assert.NotEqual(RateLimitPartitionKeys.UserOrIp(alice), RateLimitPartitionKeys.UserOrIp(bob));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-user-id")]
    public void UserOrIp_FallsBackToSocketIpForInvalidSubject(string subject)
    {
        var context = Request("198.51.100.7", "sub", subject);

        Assert.Equal("ip:198.51.100.7", RateLimitPartitionKeys.UserOrIp(context));
    }

    [Fact]
    public void UserOrIp_UsesIpForAnonymousRequests()
    {
        var context = Request("203.0.113.8");

        Assert.Equal("ip:203.0.113.8", RateLimitPartitionKeys.UserOrIp(context));
    }

    [Fact]
    public void Ip_NormalizesIPv4MappedIPv6Addresses()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.0.2.99");

        Assert.Equal("ip:192.0.2.99", RateLimitPartitionKeys.Ip(context));
    }

    [Fact]
    public void Ip_UsesStableUnknownBucketWhenSocketAddressIsMissing()
    {
        var context = new DefaultHttpContext();

        Assert.Equal("ip:unknown", RateLimitPartitionKeys.Ip(context));
    }

    [Fact]
    public void Ip_DoesNotTrustForwardedForHeaderWithoutTrustedProxyMiddleware()
    {
        var context = Request("192.0.2.40");
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.200";

        Assert.Equal("ip:192.0.2.40", RateLimitPartitionKeys.Ip(context));
    }

    private static DefaultHttpContext Request(string address, string? claimType = null, string? claimValue = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        if (claimType is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(claimType, claimValue ?? string.Empty) },
                "Bearer"));
        }

        return context;
    }
}
