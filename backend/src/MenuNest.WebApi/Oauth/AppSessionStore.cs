using MenuNest.Application.Abstractions;
using MenuNest.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.WebApi.Oauth;

/// <summary>
/// Durable SPA sessions (ADR-161). Rotation is single-use like <see cref="TokenStore"/>,
/// but there is no upstream token to exchange — the row IS the session (ADR-162), which
/// is what lets it serve a Google sign-in as well as a Microsoft one.
/// </summary>
public sealed class AppSessionStore(IApplicationDbContext db)
{
    /// <summary>Idle lifetime. Re-stamped on every rotation, so it rolls forward with use.</summary>
    public const int LifetimeDays = 365;

    public async Task<string> IssueAsync(string subject, CancellationToken ct = default)
    {
        // Reclaim this subject's dead rows on the way in. Nothing else ever
        // removes them: TakeAsync only fires on rotation and RevokeAsync only on
        // logout, so a device that is reinstalled, or whose site data is cleared,
        // strands a row for a full year. Doing it here needs no background
        // service and is self-limiting — it runs on a path the user is already
        // waiting on and touches only their own rows, and Subject is indexed.
        //
        // Expired rows ONLY. Deleting this subject's live rows would sign them
        // out on every other device, which ADR-159 forbids: logout revokes just
        // the device that pressed it.
        var now = DateTime.UtcNow;
        var dead = await db.AppSessions
            .Where(s => s.Subject == subject && s.ExpiresAt <= now)
            .ToListAsync(ct);
        if (dead.Count > 0) db.AppSessions.RemoveRange(dead);

        var code = TokenUtil.Opaque();
        db.AppSessions.Add(new AppSession
        {
            RefreshCode = code,
            Subject = subject,
            ExpiresAt = DateTime.UtcNow.AddDays(LifetimeDays),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return code;
    }

    /// <summary>
    /// Single-use: consumes the row and returns its subject, or null when the code is
    /// unknown or expired. An expired row is deleted rather than left to rot.
    /// </summary>
    public async Task<string?> TakeAsync(string refreshCode, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(refreshCode)) return null;

        var row = await db.AppSessions.FirstOrDefaultAsync(s => s.RefreshCode == refreshCode, ct);
        if (row is null) return null;

        db.AppSessions.Remove(row);
        await db.SaveChangesAsync(ct);

        return row.ExpiresAt <= DateTime.UtcNow ? null : row.Subject;
    }

    /// <summary>Deletes only the presented session (ADR-159). True when a row was removed.</summary>
    public async Task<bool> RevokeAsync(string refreshCode, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(refreshCode)) return false;

        var row = await db.AppSessions.FirstOrDefaultAsync(s => s.RefreshCode == refreshCode, ct);
        if (row is null) return false;

        db.AppSessions.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
