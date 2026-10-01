using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOS.Api.Models;

namespace LifeOS.Tests;

public class DomainEnumSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(null, allowIntegerValues: false) }
    };

    [Theory]
    [InlineData("\"Saving\"", SetAsideKind.Saving)]
    [InlineData("\"RoutineIncremental\"", SetAsideKind.RoutineIncremental)]
    [InlineData("\"RoutineBatch\"", SetAsideKind.RoutineBatch)]
    [InlineData("\"SingleSpend\"", SetAsideKind.SingleSpend)]
    public void SetAsideKind_SerializesAsReadableString(string json, SetAsideKind expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<SetAsideKind>(json, Options));
        Assert.Equal(json, JsonSerializer.Serialize(expected, Options));
    }

    [Theory]
    [InlineData("\"None\"", SetAsideCycleKind.None)]
    [InlineData("\"Weekly\"", SetAsideCycleKind.Weekly)]
    [InlineData("\"Monthly\"", SetAsideCycleKind.Monthly)]
    [InlineData("\"Quarterly\"", SetAsideCycleKind.Quarterly)]
    [InlineData("\"SemiAnnual\"", SetAsideCycleKind.SemiAnnual)]
    [InlineData("\"Annual\"", SetAsideCycleKind.Annual)]
    public void SetAsideCycleKind_SerializesAsReadableString(string json, SetAsideCycleKind expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<SetAsideCycleKind>(json, Options));
        Assert.Equal(json, JsonSerializer.Serialize(expected, Options));
    }

    [Theory]
    [InlineData("\"Opened\"", SetAsideEntryType.Opened)]
    [InlineData("\"Added\"", SetAsideEntryType.Added)]
    [InlineData("\"Withdrawn\"", SetAsideEntryType.Withdrawn)]
    [InlineData("\"Spent\"", SetAsideEntryType.Spent)]
    [InlineData("\"CycleFunding\"", SetAsideEntryType.CycleFunding)]
    [InlineData("\"Released\"", SetAsideEntryType.Released)]
    [InlineData("\"Closed\"", SetAsideEntryType.Closed)]
    public void SetAsideEntryType_SerializesAsReadableString(string json, SetAsideEntryType expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<SetAsideEntryType>(json, Options));
        Assert.Equal(json, JsonSerializer.Serialize(expected, Options));
    }

    [Theory]
    [InlineData("\"Scheduled\"", UpcomingEventStatus.Scheduled)]
    [InlineData("\"Realized\"", UpcomingEventStatus.Realized)]
    [InlineData("\"Skipped\"", UpcomingEventStatus.Skipped)]
    [InlineData("\"Cancelled\"", UpcomingEventStatus.Cancelled)]
    public void UpcomingEventStatus_SerializesAsReadableString(string json, UpcomingEventStatus expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<UpcomingEventStatus>(json, Options));
        Assert.Equal(json, JsonSerializer.Serialize(expected, Options));
    }

    [Fact]
    public void NumericEnumValues_AreRejected()
    {
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<SetAsideKind>("0", Options));
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<UpcomingEventStatus>("1", Options));
    }
}
