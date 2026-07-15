using ContextMemory.Core.Configuration;
using ContextMemory.Infrastructure.Observability;
using Microsoft.Extensions.Options;
using Xunit;

namespace ContextMemory.Api.Tests;

public class TelemetryActiveUsersTests
{
    [Fact]
    public void RecordUserActivity_CountsUsersWithinWindow()
    {
        var collector = new TelemetryCollector(Options.Create(new ContextMemoryOptions
        {
            ActiveUserWindowMinutes = 15
        }));

        collector.RecordUserActivity("app-1", "user-a");
        collector.RecordUserActivity("app-1", "user-b");
        collector.RecordUserActivity("app-1", "user-a");

        var snapshot = collector.GetAppSnapshot("app-1");
        Assert.Equal(2, snapshot.ActiveUsers);
    }
}
