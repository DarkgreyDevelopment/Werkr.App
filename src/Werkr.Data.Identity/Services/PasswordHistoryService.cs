using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Werkr.Common.Configuration;
using Werkr.Data.Identity.Entities;

namespace Werkr.Data.Identity.Services;

/// <summary>
/// Records password hashes in <see cref="PasswordHistory"/> and trims entries
/// beyond <see cref="PasswordHistoryOptions.HistoryCount"/>.
/// </summary>
public class PasswordHistoryService(
    WerkrIdentityDbContext db,
    IOptions<PasswordHistoryOptions> options
) {
    /// <summary>
    /// Records a password hash in history and trims entries beyond <see cref="PasswordHistoryOptions.HistoryCount"/>.
    /// </summary>
    /// <param name="userId">The user whose password hash is being recorded.</param>
    /// <param name="passwordHash">The ASP.NET Identity salted hash to store.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task RecordAsync( string userId, string passwordHash, CancellationToken ct = default ) {
        _ = db.PasswordHistory.Add( new PasswordHistory {
            UserId = userId,
            PasswordHash = passwordHash,
            CreatedUtc = DateTime.UtcNow,
        } );

        int count = await db.PasswordHistory.CountAsync( h => h.UserId == userId, ct );
        int limit = options.Value.HistoryCount;

        if (count >= limit) {
            List<PasswordHistory> toRemove = await db.PasswordHistory
                .Where( h => h.UserId == userId )
                .OrderByDescending( h => h.CreatedUtc )
                .Skip( limit - 1 )
                .ToListAsync( ct );

            db.PasswordHistory.RemoveRange( toRemove );
        }

        _ = await db.SaveChangesAsync( ct );
    }
}
