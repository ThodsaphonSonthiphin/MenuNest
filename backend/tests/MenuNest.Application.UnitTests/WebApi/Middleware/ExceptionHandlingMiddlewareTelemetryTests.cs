using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MenuNest.Domain.Exceptions;
using MenuNest.WebApi.Middleware;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace MenuNest.Application.UnitTests.WebApi.Middleware;

public class ExceptionHandlingMiddlewareTelemetryTests
{
    private sealed class CapturingChannel : ITelemetryChannel
    {
        public List<ITelemetry> Sent { get; } = new();
        public bool? DeveloperMode { get; set; }
        public string? EndpointAddress { get; set; }
        public void Send(ITelemetry item) => Sent.Add(item);
        public void Flush() { }
        public void Dispose() { }
    }

    private static (ExceptionHandlingMiddleware mw, CapturingChannel channel) Build(RequestDelegate inner)
    {
        var channel = new CapturingChannel();
        var config = new TelemetryConfiguration
        {
            TelemetryChannel = channel,
            ConnectionString = $"InstrumentationKey={Guid.NewGuid()}",
        };
        var client = new TelemetryClient(config);
        var mw = new ExceptionHandlingMiddleware(
            inner,
            NullLogger<ExceptionHandlingMiddleware>.Instance,
            client);
        return (mw, channel);
    }

    private static DefaultHttpContext NewContext(string path, string method)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = method;
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Fact]
    public async Task Tracks_DomainException_with_Warning_and_400()
    {
        RequestDelegate inner = _ => throw new DomainException("Group not found.");
        var (mw, channel) = Build(inner);
        var ctx = NewContext("/api/budget/groups", "POST");

        await mw.InvokeAsync(ctx);

        var ex = channel.Sent.OfType<ExceptionTelemetry>().Should().ContainSingle().Subject;
        ex.SeverityLevel.Should().Be(SeverityLevel.Warning);
        ex.Properties["Path"].Should().Be("/api/budget/groups");
        ex.Properties["Method"].Should().Be("POST");
        ex.Properties["StatusCode"].Should().Be("400");
        ex.Exception.Should().BeOfType<DomainException>();
    }

    [Fact]
    public async Task Tracks_ValidationException_with_Warning_and_400()
    {
        RequestDelegate inner = _ => throw new ValidationException(new[]
        {
            new ValidationFailure("Name", "Name is required."),
        });
        var (mw, channel) = Build(inner);
        var ctx = NewContext("/api/budget/categories", "POST");

        await mw.InvokeAsync(ctx);

        var ex = channel.Sent.OfType<ExceptionTelemetry>().Should().ContainSingle().Subject;
        ex.SeverityLevel.Should().Be(SeverityLevel.Warning);
        ex.Properties["StatusCode"].Should().Be("400");
    }

    [Fact]
    public async Task Tracks_UnauthorizedAccess_with_Error_and_401()
    {
        RequestDelegate inner = _ => throw new UnauthorizedAccessException("nope");
        var (mw, channel) = Build(inner);
        var ctx = NewContext("/api/budget/accounts", "GET");

        await mw.InvokeAsync(ctx);

        var ex = channel.Sent.OfType<ExceptionTelemetry>().Should().ContainSingle().Subject;
        ex.SeverityLevel.Should().Be(SeverityLevel.Error);
        ex.Properties["StatusCode"].Should().Be("401");
    }

    [Fact]
    public async Task Tracks_unhandled_with_Critical_and_500()
    {
        RequestDelegate inner = _ => throw new InvalidOperationException("oops");
        var (mw, channel) = Build(inner);
        var ctx = NewContext("/api/anything", "GET");

        await mw.InvokeAsync(ctx);

        var ex = channel.Sent.OfType<ExceptionTelemetry>().Should().ContainSingle().Subject;
        ex.SeverityLevel.Should().Be(SeverityLevel.Critical);
        ex.Properties["StatusCode"].Should().Be("500");
    }
}
