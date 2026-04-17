using BodyCam.Mvvm;
using FluentAssertions;

namespace BodyCam.Tests.Mvvm;

public class RelayCommandTests
{
    [Fact]
    public void Execute_CallsAction()
    {
        bool called = false;
        var cmd = new RelayCommand(() => called = true);

        cmd.Execute(null);

        called.Should().BeTrue();
    }

    [Fact]
    public void Execute_PassesParameter()
    {
        object? received = null;
        var cmd = new RelayCommand(p => received = p);

        cmd.Execute("hello");

        received.Should().Be("hello");
    }

    [Fact]
    public void CanExecute_DefaultsToTrue()
    {
        var cmd = new RelayCommand(() => { });

        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void CanExecute_RespectsPredicateTrue()
    {
        var cmd = new RelayCommand(() => { }, () => true);

        cmd.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void CanExecute_RespectsPredicateFalse()
    {
        var cmd = new RelayCommand(() => { }, () => false);

        cmd.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CanExecute_ParameterOverload_RespectsPredicateFalse()
    {
        var cmd = new RelayCommand(_ => { }, _ => false);

        cmd.CanExecute("x").Should().BeFalse();
    }

    [Fact]
    public void RaiseCanExecuteChanged_FiresEvent()
    {
        var cmd = new RelayCommand(() => { });
        bool fired = false;
        cmd.CanExecuteChanged += (_, _) => fired = true;

        cmd.RaiseCanExecuteChanged();

        fired.Should().BeTrue();
    }

    [Fact]
    public void Constructor_NullExecute_Throws()
    {
        Action act = () => new RelayCommand((Action<object?>)null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
