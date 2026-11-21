using MediaLibrary.Api.Security;

namespace MediaLibrary.Api.Tests;

/// <summary>Fixed authenticated identity for service tests using a positive internal user ID.</summary>
internal sealed class TestCurrentUser(int userId) : ICurrentUser
{
    /// <summary>The default authenticated owner used by existing service fixtures.</summary>
    public static TestCurrentUser Default { get; } = new(1);

    /// <inheritdoc />
    public int UserId { get; } = userId;
}
