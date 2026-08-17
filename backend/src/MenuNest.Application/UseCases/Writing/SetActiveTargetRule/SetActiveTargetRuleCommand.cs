using Mediator;

namespace MenuNest.Application.UseCases.Writing.SetActiveTargetRule;

/// <summary>
/// Changes the caller's active target grammar rule. Deliberately NOT part of
/// UpdateUserSettingsCommand's full snapshot (ADR-091): an MCP caller knows
/// only the rule and must not clear HomePath or the weather thresholds.
/// Blank clears the rule. Returns the stored value.
/// </summary>
public sealed record SetActiveTargetRuleCommand(string? Rule) : ICommand<string?>;
