using Mediator;

namespace MenuNest.Application.UseCases.Health.Reports.GetDoctorReport;

/// <summary>
/// Returns the full doctor-report payload for the given share token.
/// Token verification + revocation check live in the handler, so this
/// query is safe to expose anonymously (the controller route is
/// <c>[AllowAnonymous]</c>).
/// </summary>
public sealed record GetDoctorReportQuery(string Token) : IQuery<DoctorReportDto>;
