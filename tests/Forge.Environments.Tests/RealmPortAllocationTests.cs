using Forge.Contracts;
using Xunit;

namespace Forge.Environments.Tests;

public class RealmPortAllocationTests
{
    [Fact]
    public async Task NoRecordedRealmsYieldsTheBasePort()
    {
        var stateRoot = Path.Combine(Path.GetTempPath(), $"forge_test_state_{Guid.NewGuid():N}");

        var port = await RealmPortAllocator.AllocateAsync(stateRoot, basePort: 8085);

        Assert.Equal(8085, port);
    }

    [Fact]
    public async Task RecordedPortsAreSkipped()
    {
        var stateRoot = Path.Combine(Path.GetTempPath(), $"forge_test_state_{Guid.NewGuid():N}");
        await RecordStateAsync(stateRoot, "searing-gorge", port: 8085);
        await RecordStateAsync(stateRoot, "azshara", port: 8086);

        var port = await RealmPortAllocator.AllocateAsync(stateRoot, basePort: 8085);

        Assert.Equal(8087, port);
    }

    [Fact]
    public async Task TwoRealmsAllocateDifferentPortsAboveTheBase()
    {
        var stateRoot = Path.Combine(Path.GetTempPath(), $"forge_test_state_{Guid.NewGuid():N}");

        var first = await RealmPortAllocator.AllocateAsync(stateRoot, basePort: 8085);
        await RecordStateAsync(stateRoot, "searing-gorge", port: first);
        var second = await RealmPortAllocator.AllocateAsync(stateRoot, basePort: 8085);

        Assert.Equal(8085, first);
        Assert.Equal(8086, second);
        Assert.NotEqual(first, second);
    }

    private static async Task RecordStateAsync(string stateRoot, string name, int port)
    {
        var state = new RealmState(
            name, name, 1, port, RealmNames.WorldDatabase(name), RealmNames.CharacterDatabase(name),
            RealmNames.Container(name), "running", DateTime.UtcNow);
        var written = await RealmStateStore.WriteAsync(stateRoot, state);
        Assert.Null(written.ErrorCode);
    }
}
