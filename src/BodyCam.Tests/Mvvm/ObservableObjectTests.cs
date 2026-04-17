using BodyCam.Mvvm;
using FluentAssertions;

namespace BodyCam.Tests.Mvvm;

// Concrete subclass for testing the abstract ObservableObject
file class TestObservable : ObservableObject
{
    private string _name = string.Empty;
    private int _count;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public int Count
    {
        get => _count;
        set => SetProperty(ref _count, value);
    }

    // Expose for testing
    public new bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        => base.SetProperty(ref field, value, propertyName);
}

public class ObservableObjectTests
{
    [Fact]
    public void SetProperty_NewValue_FiresPropertyChanged()
    {
        var obj = new TestObservable();
        string? changedProperty = null;
        obj.PropertyChanged += (_, e) => changedProperty = e.PropertyName;

        obj.Name = "Test";

        changedProperty.Should().Be(nameof(TestObservable.Name));
    }

    [Fact]
    public void SetProperty_SameValue_DoesNotFirePropertyChanged()
    {
        var obj = new TestObservable { Name = "Test" };
        bool fired = false;
        obj.PropertyChanged += (_, _) => fired = true;

        obj.Name = "Test";

        fired.Should().BeFalse();
    }

    [Fact]
    public void SetProperty_NewValue_ReturnsTrue()
    {
        var obj = new TestObservable();
        string field = "old";

        var result = obj.SetProperty(ref field, "new", "Field");

        result.Should().BeTrue();
    }

    [Fact]
    public void SetProperty_SameValue_ReturnsFalse()
    {
        var obj = new TestObservable();
        string field = "same";

        var result = obj.SetProperty(ref field, "same", "Field");

        result.Should().BeFalse();
    }

    [Fact]
    public void SetProperty_NewValue_UpdatesField()
    {
        var obj = new TestObservable();

        obj.Name = "Updated";

        obj.Name.Should().Be("Updated");
    }

    [Fact]
    public void PropertyChanged_FiresForCorrectProperty()
    {
        var obj = new TestObservable();
        var changedProperties = new List<string?>();
        obj.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        obj.Name = "A";
        obj.Count = 1;

        changedProperties.Should().ContainInOrder(nameof(TestObservable.Name), nameof(TestObservable.Count));
    }
}
