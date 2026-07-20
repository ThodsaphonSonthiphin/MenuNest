using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Me.GetMe;

/// <summary>
/// Returns the current caller's profile, auto-provisioning a
/// <c>User</c> row on first sign-in via <see cref="IUserProvisioner"/>.
/// Includes the user's <c>HomePath</c> (Home-page preference).
/// </summary>
public sealed class GetMeHandler : IQueryHandler<GetMeQuery, MeDto>
{
    private readonly IUserProvisioner _userProvisioner;
    private readonly IApplicationDbContext _db;

    public GetMeHandler(IUserProvisioner userProvisioner, IApplicationDbContext db)
    {
        _userProvisioner = userProvisioner;
        _db = db;
    }

    public async ValueTask<MeDto> Handle(GetMeQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var settings = await _db.UserSettings
            .FirstOrDefaultAsync(s => s.UserId == user.Id, ct);

        return new MeDto(
            UserId: user.Id,
            Email: user.Email,
            DisplayName: user.DisplayName,
            FamilyId: user.FamilyId,
            FamilyName: user.Family?.Name,
            FamilyInviteCode: user.Family?.InviteCode.Value,
            AuthProvider: user.AuthProvider.ToString(),
            HomePath: settings?.HomePath,
            UvWarnThreshold: settings?.UvWarnThreshold,
            FeelsLikeWarnThreshold: settings?.FeelsLikeWarnThreshold);
    }
}
