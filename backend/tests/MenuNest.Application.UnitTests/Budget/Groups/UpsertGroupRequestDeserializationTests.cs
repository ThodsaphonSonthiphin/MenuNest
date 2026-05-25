using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using MenuNest.Application.UseCases.Budget;

namespace MenuNest.Application.UnitTests.Budget.Groups;

/// <summary>
/// Diagnostic — verify the JSON shape the frontend POSTs deserialises
/// cleanly into the positional <see cref="UpsertGroupRequest"/> record
/// when <c>sortOrder</c> is omitted. The AddGroupDialog only sends
/// <c>{name}</c> because the backend auto-assigns sortOrder on create.
/// </summary>
public class UpsertGroupRequestDeserializationTests
{
    private static readonly JsonSerializerOptions WebDefaults = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void Deserializes_when_only_name_is_provided()
    {
        const string json = """{"name":"Bills"}""";

        var result = JsonSerializer.Deserialize<UpsertGroupRequest>(json, WebDefaults);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Bills");
        result.SortOrder.Should().Be(0);
    }

    [Fact]
    public void Deserializes_when_both_fields_are_provided()
    {
        const string json = """{"name":"Bills","sortOrder":5}""";

        var result = JsonSerializer.Deserialize<UpsertGroupRequest>(json, WebDefaults);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Bills");
        result.SortOrder.Should().Be(5);
    }
}
