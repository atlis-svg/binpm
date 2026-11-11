using System.Text.Json;
using BinPm.Core.Models;

namespace BinPm.Core.Install;

/// <summary>Loads and persists the local database of installed packages.</summary>
public sealed class InstalledPackageStore
{
    private readonly string _filePath;
    private readonly object _gate = new();
    private Dictionary<string, InstalledPackage> _packages = new(StringComparer.OrdinalIgnoreCase);

    public InstalledPackageStore(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public string FilePath => _filePath;

    public void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_filePath))
            {
                _packages = new Dictionary<string, InstalledPackage>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var list = JsonDefaults.Deserialize<List<InstalledPackage>>(json) ?? new List<InstalledPackage>();
                _packages = new Dictionary<string, InstalledPackage>(StringComparer.OrdinalIgnoreCase);
                foreach (var package in list)
                {
                    if (!string.IsNullOrWhiteSpace(package.Name))
                        _packages[package.Name] = package;
                }
            }
            catch (JsonException ex)
            {
                throw new BinPmException($"Could not parse the installed packages database '{_filePath}': {ex.Message}", ex);
            }
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var list = _packages.Values
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            File.WriteAllText(_filePath, JsonDefaults.Serialize(list));
        }
    }

    public InstalledPackage? Get(string name)
    {
        lock (_gate)
        {
            return _packages.TryGetValue(name, out var package) ? package : null;
        }
    }

    /// <summary>
    /// Finds a package by name, or by the reference it was installed from
    /// (e.g. <c>owner/repo</c> when the package name is <c>flint</c>).
    /// </summary>
    public InstalledPackage? FindByReference(string reference)
    {
        lock (_gate)
        {
            if (_packages.TryGetValue(reference, out var byName))
                return byName;

            foreach (var package in _packages.Values)
            {
                if (string.Equals(package.Source, reference, StringComparison.OrdinalIgnoreCase))
                    return package;
            }

            return null;
        }
    }

    public IReadOnlyList<InstalledPackage> GetAll()
    {
        lock (_gate)
        {
            return _packages.Values
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public void Upsert(InstalledPackage package)
    {
        lock (_gate)
        {
            _packages[package.Name] = package;
        }
    }

    public bool Remove(string name)
    {
        lock (_gate)
        {
            return _packages.Remove(name);
        }
    }
}
