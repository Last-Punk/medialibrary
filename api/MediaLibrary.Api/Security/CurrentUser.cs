using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace MediaLibrary.Api.Security;

/// <summary>Resolves the request's owner ID from the authenticated JWT subject claim.</summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    /// <inheritdoc />
    public int UserId
    {
        get
        {
            var subject = httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value;
            if (int.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
                && userId > 0)
            {
                return userId;
            }

            throw new UnauthorizedAccessException("The authenticated principal has no valid user ID.");
        }
    }
}
