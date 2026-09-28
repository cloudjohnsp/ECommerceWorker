using ECommerce.Worker.Options;
using ECommerce.Worker.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace ECommerce.Worker.Tests.Resilience;

public sealed class WorkerOperationDrainTests
{
    [Fact]
    public async Task StopAccepting_PreventsNewWorkAndWaitsForInflightOperation()
    {
        using var drain = CreateDrain();
        drain.TryBegin(out var lease).Should().BeTrue();

        drain.StopAccepting();
        drain.TryBegin(out _).Should().BeFalse();
        var waiting = drain.WaitForDrainAsync();
        waiting.IsCompleted.Should().BeFalse();

        lease.Dispose();

        (await waiting).Should().BeTrue();
    }

    [Fact]
    public async Task DrainDeadline_CancelsInflightOperationAndReturnsFalse()
    {
        using var drain = CreateDrain();
        drain.TryBegin(out var lease).Should().BeTrue();
        drain.StopAccepting();

        var drained = await drain.WaitForDrainAsync();

        drained.Should().BeFalse();
        lease.CancellationToken.IsCancellationRequested.Should().BeTrue();
        lease.Dispose();
    }

    private static WorkerOperationDrain CreateDrain() => new(
        Microsoft.Extensions.Options.Options.Create(
            new WorkerRuntimeOptions { ShutdownDrainSeconds = 1 }));
}
