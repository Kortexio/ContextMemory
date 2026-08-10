using ContextMemory.Core.Configuration;
using ContextMemory.Infrastructure.Session;
using Microsoft.Extensions.Options;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class SessionArtifactStoreTests
{
    [Fact]
    public async Task FileStore_WriteReadTail_RoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-artifacts-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSessionArtifactStore(Options.Create(new ContextMemoryOptions
            {
                DataPath = root,
                ContentRootPath = root
            }));

            const string id = "tool:shell_execute:abcd1234";
            var payload = string.Join('\n', Enumerable.Range(1, 50).Select(i => $"line-{i}-CONTENT"));
            await store.WriteAsync("app", "user", "sess1", id, payload);

            var full = await store.ReadAsync("app", "user", "sess1", id);
            Assert.Equal(payload, full);

            var tail = await store.TailAsync("app", "user", "sess1", id, 40);
            Assert.NotNull(tail);
            Assert.True(tail!.Length <= 40);
            Assert.EndsWith("CONTENT", tail);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
