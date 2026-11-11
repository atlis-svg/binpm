using System.Runtime.InteropServices;
using BinPm.Core.Install;

namespace BinPm.Core.Tests;

public class ShimManagerTests
{
    [Fact]
    public void Creates_a_shim_that_points_at_the_target()
    {
        using var temp = new TempDirectory();
        var binDir = Path.Combine(temp.Path, "bin");
        var target = Path.Combine(temp.Path, "packages", "tool", "1.0.0", "tool");

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "binary");

        var manager = new ShimManager(binDir);
        var shims = manager.CreateShims(new[] { new ShimRequest("tool", target) });

        Assert.Single(shims);

        var shimPath = manager.GetShimPath("tool");
        Assert.True(File.Exists(shimPath) || Directory.Exists(shimPath));
        Assert.Equal(target, manager.GetShimTarget(shimPath));
    }

    [Fact]
    public void Creating_the_same_shim_twice_is_idempotent()
    {
        using var temp = new TempDirectory();
        var target = Path.Combine(temp.Path, "tool");
        File.WriteAllText(target, "binary");

        var manager = new ShimManager(Path.Combine(temp.Path, "bin"));
        manager.CreateShims(new[] { new ShimRequest("tool", target) });
        var second = manager.CreateShims(new[] { new ShimRequest("tool", target) });

        Assert.True(second[0].ExistedAlready);
    }

    [Fact]
    public void Conflicting_shims_are_rejected_unless_overwrite_is_requested()
    {
        using var temp = new TempDirectory();
        var first = Path.Combine(temp.Path, "first");
        var second = Path.Combine(temp.Path, "second");
        File.WriteAllText(first, "one");
        File.WriteAllText(second, "two");

        var manager = new ShimManager(Path.Combine(temp.Path, "bin"));
        manager.CreateShims(new[] { new ShimRequest("tool", first) });

        var exception = Assert.Throws<BinPmException>(() =>
            manager.CreateShims(new[] { new ShimRequest("tool", second) }));

        Assert.Contains("overwrite-shims", exception.Message, StringComparison.OrdinalIgnoreCase);

        manager.CreateShims(new[] { new ShimRequest("tool", second) }, overwrite: true);
        Assert.Equal(second, manager.GetShimTarget(manager.GetShimPath("tool")));
    }

    [Fact]
    public void RemoveShim_only_removes_shims_that_point_at_the_expected_target()
    {
        using var temp = new TempDirectory();
        var target = Path.Combine(temp.Path, "tool");
        File.WriteAllText(target, "binary");

        var manager = new ShimManager(Path.Combine(temp.Path, "bin"));
        manager.CreateShims(new[] { new ShimRequest("tool", target) });

        Assert.False(manager.RemoveShim("tool", Path.Combine(temp.Path, "other")));
        Assert.True(manager.RemoveShim("tool", target));
        Assert.False(manager.ShimExists(manager.GetShimPath("tool")));
    }

    [Fact]
    public void Detects_whether_the_bin_directory_is_on_the_path()
    {
        using var temp = new TempDirectory();
        var binDir = Path.Combine(temp.Path, "bin");
        Directory.CreateDirectory(binDir);

        var manager = new ShimManager(binDir);
        var originalPath = Environment.GetEnvironmentVariable("PATH");

        try
        {
            var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
            Environment.SetEnvironmentVariable("PATH", $"{binDir}{separator}/usr/bin");

            Assert.True(manager.IsBinDirOnPath());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public void Produces_path_instructions_for_each_shell()
    {
        var manager = new ShimManager("/opt/binpm/bin");

        Assert.Contains("/opt/binpm/bin", manager.GetPathInstructions("bash"), StringComparison.Ordinal);
        Assert.Contains("set -gx PATH", manager.GetPathInstructions("fish"), StringComparison.Ordinal);
        Assert.Contains("$env:PATH", manager.GetPathInstructions("powershell"), StringComparison.Ordinal);
        Assert.Contains("setx PATH", manager.GetPathInstructions("cmd"), StringComparison.Ordinal);
    }
}

public class InstalledPackageStoreTests
{
    [Fact]
    public void Saves_and_loads_packages()
    {
        using var temp = new TempDirectory();
        var filePath = Path.Combine(temp.Path, "installed.json");

        var store = new InstalledPackageStore(filePath);
        store.Upsert(new Core.Models.InstalledPackage
        {
            Name = "ripgrep",
            Version = "14.1.1",
            Source = "BurntSushi/ripgrep",
            Bins = new List<string> { "rg" },
        });
        store.Save();

        var reloaded = new InstalledPackageStore(filePath);
        reloaded.Load();

        var package = reloaded.Get("ripgrep");
        Assert.NotNull(package);
        Assert.Equal("14.1.1", package!.Version);
        Assert.Equal(new[] { "rg" }, package.Bins.ToArray());
    }

    [Fact]
    public void Removing_a_package_persists()
    {
        using var temp = new TempDirectory();
        var filePath = Path.Combine(temp.Path, "installed.json");

        var store = new InstalledPackageStore(filePath);
        store.Upsert(new Core.Models.InstalledPackage { Name = "tool", Version = "1.0.0" });
        store.Save();

        Assert.True(store.Remove("tool"));
        store.Save();

        var reloaded = new InstalledPackageStore(filePath);
        reloaded.Load();
        Assert.Null(reloaded.Get("tool"));
    }

    [Fact]
    public void Loading_a_missing_file_yields_an_empty_store()
    {
        using var temp = new TempDirectory();
        var store = new InstalledPackageStore(Path.Combine(temp.Path, "missing.json"));

        store.Load();

        Assert.Empty(store.GetAll());
    }
}
