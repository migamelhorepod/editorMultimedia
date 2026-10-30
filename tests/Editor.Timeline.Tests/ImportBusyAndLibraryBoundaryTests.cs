using Editor.Security;
using Xunit;

namespace Editor.Timeline.Tests;

/// <summary>
/// Regressions for the physical D: test import and WPF project lifecycle.
/// No personal files or physical system directories are traversed.
/// </summary>
public sealed class ImportBusyAndLibraryBoundaryTests
{
    [Fact]
    public void GivenActiveImport_WhenReplacingOrSavingProject_ThenMutationIsDenied()
    {
        var coordinator = new MediaImportCoordinator(Guid.NewGuid());
        Guid operation = coordinator.BeginImport();

        Assert.False(coordinator.CanMutateProject);
        var error = Assert.Throws<InvalidOperationException>(() =>
            coordinator.DemandProjectMutationAllowed());
        Assert.Equal(MediaImportCoordinator.BusyMessage, error.Message);

        // Opening a new project and switching library are both blocked while busy.
        Assert.False(coordinator.TryChangeLibrary(Guid.NewGuid()).Allowed);
        Assert.Equal(1, coordinator.ActiveImports);

        coordinator.CompleteImport(operation);
        Assert.True(coordinator.CanMutateProject);
        coordinator.DemandProjectMutationAllowed();
    }

    [Fact]
    public void GivenSeveralOperations_WhenOnlyOneCompletes_ThenProjectRemainsLocked()
    {
        var coordinator = new MediaImportCoordinator(Guid.NewGuid());
        Guid first = coordinator.BeginImport(), second = coordinator.BeginImport();
        coordinator.CompleteImport(first);
        Assert.False(coordinator.CanMutateProject);
        Assert.Throws<InvalidOperationException>(() =>
            coordinator.DemandProjectMutationAllowed());
        coordinator.CompleteImport(second);
        Assert.True(coordinator.CanMutateProject);
    }

    [Fact]
    public void GivenStorageFailureDuringImport_WhenProjectReplacementRequested_ThenDeny()
    {
        var coordinator = new MediaImportCoordinator(Guid.NewGuid());
        var task = coordinator.BeginImport();
        _ = coordinator.MarkStorageUnavailable();
        Assert.False(coordinator.CanMutateProject);
        Assert.False(coordinator.TryChangeLibrary(Guid.NewGuid()).Allowed);
        coordinator.CompleteImport(task);
        Assert.True(coordinator.CanMutateProject);
    }

    [Fact]
    public void GivenEntireDriveRoot_WhenSelectedAsLibrary_ThenRejectBeforeEnumeration()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<UnauthorizedAccessException>(() =>
            LibraryRootPolicy.ValidateUserSelected(@"D:\"));
        Assert.Throws<UnauthorizedAccessException>(() =>
            WindowsScopedCodecInput.RecoverAbandoned(@"D:\"));
    }

    [Theory]
    [InlineData(@"D:\WindowsApps")]
    [InlineData(@"D:\System Volume Information")]
    [InlineData(@"D:\$RECYCLE.BIN")]
    [InlineData(@"D:\windowsapps\SomeFolder")]
    public void GivenProtectedWindowsDirectory_WhenSelected_ThenRejectBeforeDiskRead(string folder)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<UnauthorizedAccessException>(() =>
            LibraryRootPolicy.ValidateUserSelected(folder));
    }

    [Fact]
    public void GivenExistingDedicatedDirectory_WhenSelected_ThenRootIsAuthorized()
    {
        if (!OperatingSystem.IsWindows()) return;
        // Our CI checkout resides on D:. Do not write to D:\ itself.
        string workingDirectory = Path.GetFullPath(Directory.GetCurrentDirectory());
        if (!workingDirectory.StartsWith(@"D:\", StringComparison.OrdinalIgnoreCase))
            return;
        string root = Path.Combine(workingDirectory, "TestResults",
            "library-boundary-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Assert.Equal(Path.GetFullPath(root),
                LibraryRootPolicy.ValidateUserSelected(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GivenUnavailableLibrary_WhenSelected_ThenNoRootIsActivated()
    {
        if (!OperatingSystem.IsWindows()) return;
        string candidate = @"D:\editor-library-nonexistent-" + Guid.NewGuid().ToString("N");
        Assert.Throws<DirectoryNotFoundException>(() =>
            LibraryRootPolicy.ValidateUserSelected(candidate));
    }
}
