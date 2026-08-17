using Mediator;
using MenuNest.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MenuNest.Application.UseCases.Writing.GetActiveTargetRule;

public sealed class GetActiveTargetRuleHandler : IQueryHandler<GetActiveTargetRuleQuery, string?>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserProvisioner _userProvisioner;

    public GetActiveTargetRuleHandler(IApplicationDbContext db, IUserProvisioner userProvisioner)
    {
        _db = db;
        _userProvisioner = userProvisioner;
    }

    public async ValueTask<string?> Handle(GetActiveTargetRuleQuery query, CancellationToken ct)
    {
        var user = await _userProvisioner.GetOrProvisionCurrentAsync(ct);

        var settings = await _db.UserSettings
            .FirstOrDefaultAsync(s => s.UserId == user.Id, ct);

        return settings?.ActiveTargetRule;
    }
}
