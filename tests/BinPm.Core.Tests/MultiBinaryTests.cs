using BinPm.Core.Install;
using BinPm.Core.Models;

namespace BinPm.Core.Tests;

public class MultiBinaryTests
{
    [Fact]
    public void Every_binary_in_a_package_is_shimmed()
    {
        using var temp = new TempDirectory();
        var paths = new Paths(
            root: temp.Path,
            packagesDir: Path.Combine(temp.Path, "packages"),
            binDir: Path.Combine(temp.Path, "bin"),
            cacheDir: Path.Combine(temp.Path, "cache"),
            installedDbPath: Path.Combine(temp.Path, "installed.json"));

        var store = new InstalledPackageStore(paths.InstalledDbPath);
        store.Load();

        store.Upsert(new InstalledPackage
        {
            Name = "flint",
            Version = "0.1.4",
            Source = "atlis-svg/flint",
            Bins = new List<string> { "flint", "flint-player" },
        });

        store.Save();

        // The package can be found by the reference it was installed from.
        var byReference = store.FindByReference("atlis-svg/flint");
        Assert.NotNull(byReference);
        Assert.Equal("flint", byReference!.Name);

        // ...and by its name.
        Assert.NotNull(store.FindByReference("flint"));
        Assert.Null(store.FindByReference("someone/else"));
    }
}
