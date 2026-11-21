using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MediaLibrary.Api.Security;

namespace MediaLibrary.Api.Tests;

public sealed class CurrentUserTests
{
    [Fact]
    public void UserId_ReturnsPositiveSubjectFromAuthenticatedPrincipal()
    {
        var context = new DefaultHttpContext();
        context.User = PrincipalWithSubject("4821");
        var accessor = new HttpContextAccessor { HttpContext = context };

        Assert.Equal(4821, new CurrentUser(accessor).UserId);
    }

    [Fact]
    public void UserId_ThrowsWhenThereIsNoHttpRequest()
    {
        var accessor = new HttpContextAccessor();

        Assert.Throws<UnauthorizedAccessException>(() => _ = new CurrentUser(accessor).UserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-integer")]
    [InlineData("0")]
    [InlineData("-9")]
    public void UserId_RejectsMissingMalformedAndNonPositiveSubjects(string? subject)
    {
        var context = new DefaultHttpContext();
        context.User = PrincipalWithSubject(subject);
        var accessor = new HttpContextAccessor { HttpContext = context };

        Assert.Throws<UnauthorizedAccessException>(() => _ = new CurrentUser(accessor).UserId);
    }

    private static ClaimsPrincipal PrincipalWithSubject(string? subject)
    {
        var claims = subject is null
            ? Array.Empty<Claim>()
            : new[] { new Claim("sub", subject) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }
}
