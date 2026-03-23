using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Werkr.Common.Configuration;
using Werkr.Data.Identity.Entities;

namespace Werkr.Data.Identity.Validators;

/// <summary>
/// Password validator that rejects passwords matching any of the user's
/// recent password hashes (configurable via <see cref="PasswordHistoryOptions.HistoryCount"/>).
/// Uses <see cref="PasswordHasher{TUser}.VerifyHashedPassword"/> because ASP.NET Identity
/// hashes are salted and cannot be compared as raw strings.
/// </summary>
public class PasswordHistoryValidator(
    WerkrIdentityDbContext db,
    IOptions<PasswordHistoryOptions> options
) : IPasswordValidator<WerkrUser> {
    /// <inheritdoc/>
    public async Task<IdentityResult> ValidateAsync(
        UserManager<WerkrUser> manager, WerkrUser user, string? password ) {
        if (password is null) {
            return IdentityResult.Success;
        }

        // New users have no Id yet — skip history check
        if (string.IsNullOrEmpty( user.Id )) {
            return IdentityResult.Success;
        }

        int historyCount = options.Value.HistoryCount;

        List<PasswordHistory> history = await db.PasswordHistory
            .Where( h => h.UserId == user.Id )
            .OrderByDescending( h => h.CreatedUtc )
            .Take( historyCount )
            .ToListAsync( );

        foreach (PasswordHistory entry in history) {
            PasswordVerificationResult result = manager.PasswordHasher.VerifyHashedPassword(
                user, entry.PasswordHash, password );

            if (result != PasswordVerificationResult.Failed) {
                return IdentityResult.Failed( new IdentityError {
                    Code = "PasswordRecentlyUsed",
                    Description = $"You cannot reuse any of your last {historyCount} passwords."
                } );
            }
        }

        return IdentityResult.Success;
    }
}
