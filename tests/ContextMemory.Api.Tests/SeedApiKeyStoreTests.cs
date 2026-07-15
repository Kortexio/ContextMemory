using ContextMemory.Core.Configuration;
using ContextMemory.Core.Security;
using ContextMemory.Infrastructure.Security;
using Xunit;

namespace ContextMemory.Api.Tests;

public sealed class SeedApiKeyStoreTests
{
    [Fact]
    public void SaveOverride_IsLoadedOnNextRead()
    {
        var root = Path.Combine(Path.GetTempPath(), "cm-seed-key-" + Guid.NewGuid().ToString("N"));
        var options = new ContextMemoryOptions
        {
            DataPath = root,
            ContentRootPath = AppContext.BaseDirectory
        };

        try
        {
            SeedApiKeyStore.SaveOverride(options, "demo-dev", "cm_live_rotated_test_key");
            var loaded = SeedApiKeyStore.TryLoadOverride(options, "demo-dev");

            Assert.Equal("cm_live_rotated_test_key", loaded);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
