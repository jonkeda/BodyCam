using BodyCam.Orchestration;
using BodyCam.Services;
using BodyCam.Services.Audio;
using BodyCam.Services.Barcode;
using BodyCam.Services.Camera;
using BodyCam.Services.Glasses;
using BodyCam.Services.Glasses.HeyCyan;
using BodyCam.Services.QrCode;
using BodyCam.Tests.Services.Glasses.HeyCyan.Fakes;
using BodyCam.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BodyCam.Tests.ViewModels;

public sealed class MainViewModelProductLookupTests
{
    [Fact]
    public async Task LookupProductFromUiAsync_WhenFound_AddsProductNameButtonOnly()
    {
        var product = new ProductInfo
        {
            Barcode = "4006420012345",
            Source = "test",
            Name = "Mineral Water"
        };
        var workflow = new FixedProductLookupWorkflow(new ProductBarcodeLookupResult(
            ProductBarcodeLookupStatus.Found,
            product.Barcode,
            product,
            product.Name));
        var vm = CreateVm(workflow);

        await vm.LookupProductFromUiAsync();

        var entry = vm.Entries.Single(entry => entry.Role == "Product");
        entry.Text.Should().Be("Mineral Water");
        entry.IsThinking.Should().BeFalse();
        entry.IsActionsOnly.Should().BeTrue();
        entry.ShowText.Should().BeFalse();
        entry.Actions.Should().ContainSingle();
        entry.Actions[0].Label.Should().Be("Mineral Water");
        workflow.Calls.Should().Be(1);
    }

    [Fact]
    public async Task LookupProductFromUiAsync_ProductButton_OpensDetails()
    {
        var product = new ProductInfo
        {
            Barcode = "4006420012345",
            Source = "test",
            Name = "Mineral Water"
        };
        var workflow = new FixedProductLookupWorkflow(new ProductBarcodeLookupResult(
            ProductBarcodeLookupStatus.Found,
            product.Barcode,
            product,
            product.Name));
        var opened = new TaskCompletionSource<ProductInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = CreateVm(workflow, productInfo =>
        {
            opened.TrySetResult(productInfo);
            return Task.CompletedTask;
        });

        await vm.LookupProductFromUiAsync();
        vm.Entries.Single(entry => entry.Role == "Product").Actions.Single().Command.Execute(null);

        (await opened.Task.WaitAsync(TimeSpan.FromSeconds(2))).Should().Be(product);
    }

    [Fact]
    public async Task LookupProductFromUiAsync_WhenNotFound_AddsTranscriptMessageWithoutButton()
    {
        var workflow = new FixedProductLookupWorkflow(new ProductBarcodeLookupResult(
            ProductBarcodeLookupStatus.NotFound,
            "999",
            null,
            "Product not found in any database. Barcode: 999"));
        var vm = CreateVm(workflow);

        await vm.LookupProductFromUiAsync();

        var entry = vm.Entries.Single(entry => entry.Role == "Product");
        entry.Text.Should().Be("Product not found: 999");
        entry.IsThinking.Should().BeFalse();
        entry.IsActionsOnly.Should().BeFalse();
        entry.ShowText.Should().BeTrue();
        entry.Actions.Should().BeEmpty();
    }

    private static MainViewModel CreateVm(
        IProductBarcodeLookupWorkflow workflow,
        Func<ProductInfo, Task>? openProductDetailsAsync = null)
    {
        var settings = new FakeSettingsService();
        var cameraManager = new CameraManager(
            [],
            settings,
            new DefaultCameraSelector(),
            NullLogger<CameraManager>.Instance,
            null);

        return new MainViewModel(
            new StubOrchestrator(),
            Substitute.For<IApiKeyService>(),
            settings,
            cameraManager,
            Substitute.For<IQrCodeScanner>(),
            new QrCodeService(),
            new QrContentResolver([]),
            CreateGlassesManager(settings),
            NullLogger<MainViewModel>.Instance,
            productLookupWorkflow: workflow,
            openProductDetailsAsync: openProductDetailsAsync);
    }

    private static HeyCyanGlassesDeviceManager CreateGlassesManager(ISettingsService settings)
    {
        var session = new FakeHeyCyanSessionWithVersion();
        var transfer = new FakeHeyCyanMediaTransfer();
        var btInput = new FakeBluetoothAudioInputProvider(["AA:BB:CC:DD:EE:FF"]);
        var btOutput = new FakeBluetoothAudioOutputProvider(["AA:BB:CC:DD:EE:FF"]);

        var camera = new HeyCyanCameraProvider(
            session,
            transfer,
            NullLogger<HeyCyanCameraProvider>.Instance,
            photoSettleDelay: TimeSpan.Zero);

        var mic = new HeyCyanAudioInputProvider(
            session,
            btInput,
            NullLogger<HeyCyanAudioInputProvider>.Instance);

        var speaker = new HeyCyanAudioOutputProvider(
            session,
            btOutput,
            NullLogger<HeyCyanAudioOutputProvider>.Instance);

        var button = new HeyCyanButtonProvider(
            session,
            NullLogger<HeyCyanButtonProvider>.Instance);

        return new HeyCyanGlassesDeviceManager(
            session,
            camera,
            mic,
            speaker,
            button,
            transfer,
            settings,
            NullLogger<HeyCyanGlassesDeviceManager>.Instance);
    }

    private sealed class FixedProductLookupWorkflow : IProductBarcodeLookupWorkflow
    {
        private readonly ProductBarcodeLookupResult _result;

        public FixedProductLookupWorkflow(ProductBarcodeLookupResult result)
        {
            _result = result;
        }

        public int Calls { get; private set; }

        public Task<ProductBarcodeLookupResult> LookupAsync(
            Func<CancellationToken, Task<byte[]?>> captureFrame,
            string? barcode = null,
            CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }

    private sealed class StubOrchestrator : AgentOrchestrator
    {
#pragma warning disable CS8618
        public StubOrchestrator()
            : base(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!)
        {
        }
#pragma warning restore CS8618
    }
}
