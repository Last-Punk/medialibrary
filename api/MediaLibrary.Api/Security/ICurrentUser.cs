namespace MediaLibrary.Api.Security;

/// <summary>Provides the authenticated API user's internal identifier.</summary>
public interface ICurrentUser
{
    /// <summary>Gets the positive internal user ID from the validated JWT subject.</summary>
    int UserId { get; }
}
