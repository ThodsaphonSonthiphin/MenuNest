using Mediator;

namespace MenuNest.Application.UseCases.Writing.GetActiveTargetRule;

/// <summary>
/// Returns the caller's active target grammar rule, or null when they have
/// never set one — Claude Code then asks in chat and calls
/// set_active_target_rule before correcting (mcp-tool-contract).
/// </summary>
public sealed record GetActiveTargetRuleQuery : IQuery<string?>;
