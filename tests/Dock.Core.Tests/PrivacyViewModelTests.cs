using Dock.Core.Models;
using Dock.Core.Services;
using Dock.Core.ViewModels;

namespace Dock.Core.Tests;

public class PrivacyViewModelTests
{
    [Fact]
    public void Apply_Empty_ClaimsNothing()
    {
        var viewModel = new PrivacyViewModel(new FakeIcons());

        viewModel.Apply([]);

        Assert.False(viewModel.IsActive);
        Assert.False(viewModel.CameraInUse);
        Assert.Empty(viewModel.Apps);
    }

    [Fact]
    public void Apply_OneApp_NamesIt()
    {
        var viewModel = new PrivacyViewModel(new FakeIcons());

        viewModel.Apply([Usage("Discord")]);

        Assert.True(viewModel.IsActive);
        Assert.True(viewModel.CameraInUse);
        Assert.Equal("Discord · camera", viewModel.Summary);
        Assert.NotNull(viewModel.Icon);
    }

    [Fact]
    public void Apply_SeveralApps_CountsThemInstead()
    {
        var viewModel = new PrivacyViewModel(new FakeIcons());

        viewModel.Apply([Usage("Discord"), Usage("Zoom")]);

        Assert.Equal(2, viewModel.Apps.Count);
        Assert.Equal("2 apps · camera", viewModel.Summary);
    }

    [Fact]
    public void Apply_PackagedApp_HasNoIcon()
    {
        var viewModel = new PrivacyViewModel(new FakeIcons());

        // A package family name carries no path to extract an icon from.
        viewModel.Apply([new DeviceUsage(string.Empty, "WindowsCamera")]);

        Assert.Null(viewModel.Apps[0].IconPng);
        Assert.Equal("WindowsCamera · camera", viewModel.Summary);
    }

    [Fact]
    public void Apply_Empty_HoldsTheDisplayUntilRetired()
    {
        var viewModel = new PrivacyViewModel(new FakeIcons());
        viewModel.Apply([Usage("Discord")]);

        viewModel.Apply([]);

        // The claim is gone, but an app renegotiating a stream drops the camera for a moment and
        // the island is still holding this up across the gap.
        Assert.False(viewModel.IsActive);
        Assert.True(viewModel.CameraInUse);
        Assert.Equal("Discord · camera", viewModel.Summary);

        viewModel.Retire();

        Assert.False(viewModel.CameraInUse);
        Assert.Empty(viewModel.Apps);
        Assert.Equal(string.Empty, viewModel.Summary);
    }

    [Fact]
    public void Apply_UnchangedReading_KeepsTheSameRowInstances()
    {
        var viewModel = new PrivacyViewModel(new FakeIcons());
        viewModel.Apply([Usage("Discord")]);
        var first = viewModel.Apps[0];

        // The registry republishes the whole set on any change, including ones that named the same
        // application; rebuilding the list would blink the row and re-decode its icon.
        viewModel.Apply([Usage("Discord")]);

        Assert.Same(first, viewModel.Apps[0]);
    }

    [Fact]
    public void Apply_DifferentApp_RebuildsTheList()
    {
        var viewModel = new PrivacyViewModel(new FakeIcons());
        viewModel.Apply([Usage("Discord")]);

        viewModel.Apply([Usage("Zoom")]);

        Assert.Equal("Zoom", Assert.Single(viewModel.Apps).Name);
    }

    [Fact]
    public void Priority_RanksBelowMedia()
    {
        var viewModel = new PrivacyViewModel(new FakeIcons());

        // A dot says everything this has to say, so it never takes the pill off a playing track.
        Assert.True(viewModel.Priority < IslandPriority.Ambient);
    }

    private static DeviceUsage Usage(string name) => new($@"C:\Apps\{name}.exe", name);

    private sealed class FakeIcons : IIconProvider
    {
        public byte[]? GetIconPng(string path, int size) => [1, 2, 3];

        public byte[]? GetAppIconPng(string appUserModelId, int size) => [1, 2, 3];
    }
}
