using BodyCam.Mvvm;
using FluentAssertions;

namespace BodyCam.Tests.Mvvm;

public class AsyncRelayCommandTests
{
    [Fact]
    public async Task Execute_RunsAsyncAction()
    {
        bool called = false;
        var tcs = new TaskCompletionSource();
        var cmd = new AsyncRelayCommand(async () =>
        {
            called = true;
            await Task.CompletedTask;
            tcs.SetResult();
        });

        cmd.Execute(null);
        await tcs.Task;

        called.Should().BeTrue();
    }

    [Fact]
    public void CanExecute_DefaultsToTrue()
    {
        var cmd = new AsyncRelayCommand(() => Task.CompletedTask);

        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task CanExecute_FalseWhileExecuting()
    {
        var executingTcs = new TaskCompletionSource();
        var canExecuteDuringRun = true;
        var cmd = new AsyncRelayCommand(async () =>
        {
            await executingTcs.Task;
        });

        // Start execution
        cmd.Execute(null);

        // Check during execution
        canExecuteDuringRun = cmd.CanExecute(null);

        // Release
        executingTcs.SetResult();

        // Small delay to let finally block run
        await Task.Delay(50);

        canExecuteDuringRun.Should().BeFalse();
        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task IsExecuting_TogglesCorrectly()
    {
        var executingTcs = new TaskCompletionSource();
        bool wasExecutingDuringRun = false;
        var cmd = new AsyncRelayCommand(async () =>
        {
            await executingTcs.Task;
        });

        cmd.IsExecuting.Should().BeFalse();

        cmd.Execute(null);
        wasExecutingDuringRun = cmd.IsExecuting;

        executingTcs.SetResult();
        await Task.Delay(50);

        wasExecutingDuringRun.Should().BeTrue();
        cmd.IsExecuting.Should().BeFalse();
    }

    [Fact]
    public void RaiseCanExecuteChanged_FiresEvent()
    {
        var cmd = new AsyncRelayCommand(() => Task.CompletedTask);
        bool fired = false;
        cmd.CanExecuteChanged += (_, _) => fired = true;

        cmd.RaiseCanExecuteChanged();

        fired.Should().BeTrue();
    }

    [Fact]
    public void CanExecute_RespectsCustomPredicate()
    {
        var cmd = new AsyncRelayCommand(() => Task.CompletedTask, () => false);

        cmd.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Constructor_NullExecute_Throws()
    {
        Action act = () => new AsyncRelayCommand((Func<object?, Task>)null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
