using Mediator;
using MenuNest.Application.Abstractions;

namespace MenuNest.Application.UseCases.Me.GetMe;

/// <summary>
/// Returns the current caller's profile, auto-provisioning a
/// <c>User</c> row on first sign-in via <see cref="IUserProvisioner"/>.
/// </summary>
public sealed class GetMeHandler : IQueryHandler<GetMeQuery, MeDto>
{
    private readonly IUserProvisioner _userProvisioner;

    public GetMeHandler(IUserProvisioner userProvisioner)
    {
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<MeDto> Handle(GetMeQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        return new MeDto(
            UserId: user.Id,
            Email: user.Email,
            DisplayName: user.DisplayName,
            FamilyId: user.FamilyId,
            FamilyName: user.Family?.Name,
            FamilyInviteCode: user.Family?.InviteCode.Value);
    }
}
