using MonopolyServer.Infrastructure.Auth;

namespace MonopolyServer.Data.Repositories.UserAuth;

/// <summary>
/// Persistence contract for UserDocument records.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Returns the user with the given B2C objectId, or null if not found.
    /// </summary>
    Task<UserDocument?> GetAsync(string objectId);

    /// <summary>
    /// Returns the user with the given email address, or null if not found.
    /// </summary>
    Task<UserDocument?> GetByEmailAsync(string email);

    /// <summary>
    /// Creates or replaces the user document.
    /// </summary>
    Task UpsertAsync(UserDocument user);
}