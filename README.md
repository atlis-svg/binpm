# BinPm

A package manager for **binaries**, written in C#.

BinPm installs and manages pre-built executables the way `npm` or `cargo` manage libraries:
it resolves a package to a version, downloads the right asset for your OS and CPU, verifies
its checksum, extracts it, and puts the resulting commands on your `PATH`.

It ships as two things:

| Project | What it is |
| --- | --- |
| `src/BinPm.Core` | A reusable library: manifest models, registry clients, version ranges, the installer, shim management. |
| `src/BinPm.Cli` | The `binpm` command-line tool built on top of that library. |

Targets **.NET 8**.

---

## Quick start

```bash
# Install straight from a GitHub release (no manifest needed - BinPm reads the releases API).
binpm install BurntSushi/ripgrep

# Run it.
rg --version
```

```
✓ ripgrep 15.2.0 installed.
Commands: rg
```

If the shim directory is not on your `PATH` yet, BinPm tells you how to add it:

```bash
binpm init          # prints the exact line for your shell
```

```bash
# Install from your own HTTP registry.
binpm install ripgrep --registry example
```

---

## How it works

```
binpm install owner/repo
        │
        ├── resolve reference ─────────► registry client (GitHub / HTTP registry)
        │                                 └── package manifest
        ├── select version ───────────► version range (latest, 1.2.3, ^1.0, >=1.0 <2.0)
        ├── select asset ─────────────► matching OS + CPU (linux-x64, osx-arm64, ...)
        ├── download + verify ────────► SHA-256 checksum
        ├── extract ──────────────────► zip / tar.gz / tar / gz / raw executable
        ├── create shims ─────────────► symlink (Unix) or .cmd wrapper (Windows)
        └── record installation ──────► <root>/installed.json
```

### Layout on disk

| Platform | Root | Shims |
| --- | --- | --- |
| Linux | `~/.local/share/binpm` | `~/.local/bin` |
| macOS | `~/Library/Application Support/binpm` | `~/.local/bin` |
| Windows | `%LOCALAPPDATA%\binpm` | `%LOCALAPPDATA%\binpm\bin` |

```
<root>/
├── packages/<name>/<version>/   extracted package files
├── cache/                       downloaded assets, reused across installs
└── installed.json               local database of installed packages
```

Override the root with `--root <path>` or the `BINPM_ROOT` environment variable.

---

## Commands

```
binpm install <package>[@version] [options]   Install a package
binpm uninstall <package>                      Remove a package and its shims
binpm update [package]                         Update one package or all of them
binpm list                                     List installed packages
binpm list --available                         List packages in the configured registries
binpm list --outdated                          Show packages with newer versions
binpm search <term>                            Search the registries and GitHub
binpm info <package>                           Show details about a package
binpm which <command>                          Show which package provides a command
binpm init                                     Print PATH setup instructions
binpm doctor                                   Diagnostics
binpm cache clean                              Empty the download cache
```

Run `binpm <command> --help` for the options of a single command.

### Package references

| Reference | Meaning |
| --- | --- |
| `ripgrep` | Looked up in the default registry |
| `owner/repo` | GitHub |
| `mirror:ripgrep` | A registry named `mirror` in your config |
| `github:owner/repo` | GitHub, explicitly |

### Version selectors

Append a selector with `@` (or pass it as the second argument):

| Selector | Meaning |
| --- | --- |
| `@latest` | The highest published version (default) |
| `@1.2.3` | Exactly that version |
| `@1.2` / `@1.2.x` | The newest `1.2.z` |
| `@1.x` | The newest `1.z.w` |
| `@^1.2.3` | `>=1.2.3 <2.0.0` (caret semantics) |
| `@~1.2.3` | `>=1.2.3 <1.3.0` (tilde semantics) |
| `@>=1.0.0` | Comparison operators: `>=`, `<=`, `>`, `<`, `=`, `!=` |
| `@>=1.0.0 <2.0.0` | Multiple clauses are combined with AND |

Prereleases are only selected when the selector mentions a prerelease explicitly.

---

## Registries

### GitHub

`binpm install owner/repo` looks for a `binpm.json` manifest in the repository (on the `main`
branch by default). If there is none, BinPm builds a manifest automatically from the repository's
**releases**: each release tag becomes a version, each release asset becomes a downloadable asset,
and the asset that matches your platform is installed.

Release tags do not have to be plain version numbers: `v1.2.3`, `jq-1.7.1` and `release-2.0.0`
are all understood.

```bash
binpm install BurntSushi/ripgrep      # auto mode, uses the releases API
binpm info BurntSushi/ripgrep          # shows every published version
```

Set a GitHub token in the config file to raise the API rate limit:

```json
{ "githubToken": "ghp_..." }
```

### HTTP registry

A registry is static JSON served over HTTP:

```
<baseUrl>/index.json                 the package index
<baseUrl>/<package>/manifest.json     one manifest per package
```

`index.json` is either a bare array or an object with a `packages` array:

```json
{
  "packages": [
    { "name": "ripgrep", "description": "ripgrep recursively searches directories." }
  ]
}
```

`manifest.json` describes the package and its versions:

```json
{
  "name": "ripgrep",
  "description": "ripgrep recursively searches directories for a regex pattern.",
  "homepage": "https://github.com/BurntSushi/ripgrep",
  "license": "MIT",
  "repository": "https://github.com/BurntSushi/ripgrep",
  "assetUrlTemplate": "https://github.com/BurntSushi/ripgrep/releases/download/{version}/{name}",
  "defaultVersion": "14.1.1",
  "versions": [
    {
      "version": "14.1.1",
      "published": "2024-09-10T00:00:00Z",
      "notes": "Release notes go here.",
      "assets": [
        {
          "name": "ripgrep-14.1.1-x86_64-unknown-linux-musl.tar.gz",
          "sha256": "4cf9f2741e6c465ffdb7c26f38056a59e2a2544b51f7cc128ef28337eeae4d8e",
          "size": 2411264,
          "bins": ["rg"]
        }
      ]
    }
  ]
}
```

**Manifest fields**

| Field | Meaning |
| --- | --- |
| `name`, `description`, `homepage`, `license`, `repository` | Package metadata. |
| `assetUrlTemplate` | URL template for assets. Placeholders: `{version}`, `{name}`, `{baseUrl}`. |
| `defaultVersion` | Version used when the user does not ask for one. |
| `versions[]` | The published versions. |
| `versions[].version` | Version string (semver). |
| `versions[].published` | Optional publication timestamp. |
| `versions[].notes` | Optional release notes. |
| `versions[].assets[]` | The downloadable files for this version. |

**Asset fields**

| Field | Meaning |
| --- | --- |
| `name` | File name. Also used for platform detection. |
| `url` | Explicit download URL. Overrides `assetUrlTemplate`. |
| `sha256` | Expected checksum. The download is rejected on mismatch. |
| `size` | Expected size in bytes (informational). |
| `platforms` | Explicit platform ids (`linux-x64`, `osx-arm64`, `win-x86`). Overrides name detection. |
| `bins` | Paths of the binaries to shim, relative to the extracted package root. |
| `executable` | Marks a self-contained executable asset. |

### How the right asset is picked

1. An asset with an explicit `platforms` entry that matches the current platform wins.
2. Otherwise the asset name is matched against known OS and CPU tokens
   (`x86_64`, `amd64`, `aarch64`, `arm64`, `x64`, `win64`, `i686`, `armv7`, `linux`, `windows`,
   `darwin`, `osx`, ...). Names that spell out the platform id exactly score higher.
3. Checksum, signature and metadata sidecars (`*.sha256`, `*.asc`, `*.sig`, ...) are never installed.
4. OS packages BinPm cannot unpack (`*.deb`, `*.rpm`, `*.apk`, `*.msi`, `*.pkg`, `*.snap`) are skipped
   in favour of the tarball or zip of the same release.
5. When several assets match the same platform (some projects publish one archive per binary, e.g.
   `atuin`, `atuin-client`, `atuin-server`), the archive whose name has the fewest segments before
   the platform decoration is used — that is the main binary.
6. If a version has a single asset that mentions no platform at all, it is used as-is
   (a universal binary or a single-file script).

### Binaries and shims

If `bins` is not specified, BinPm looks inside the extracted package:

* exactly one executable in the package root → that one is used;
* the root contains a single directory → the lookup continues inside it (the common
  "archive with a top-level folder" layout);
* otherwise the package is searched recursively, which finds binaries nested in
  `pkg-1.0.0_linux_amd64/bin/pkg`;
* several binaries are found → the one named after the package wins (age ships `age`,
  `age-keygen`, `age-plugin-*`); if there is still no obvious candidate BinPm stops and asks you
  to pick one with `--bin <path>`.

Files extracted from a zip lose their Unix permissions, so BinPm marks anything that looks like an
ELF binary or a script as executable again.

Published file names often carry platform decoration. The command name is cleaned up, so
`jq-linux-amd64` becomes `jq`, `yq_linux_amd64` becomes `yq` and `sops-v3.13.3.linux.amd64` becomes
`sops`. Only trailing decoration is removed, so a tool genuinely called `base64-tool` keeps its name.

---

## Configuration

`~/.config/binpm/config.json` on Linux, `~/Library/Application Support/binpm/config.json` on
macOS, `%APPDATA%\binpm\config.json` on Windows. Use `--config <path>` for a different file.

```json
{
  "defaultRegistry": "example",
  "githubToken": "",
  "registries": [
    { "name": "example", "type": "http", "url": "https://packages.example.com" },
    { "name": "internal", "type": "http", "url": "https://binaries.internal.corp" }
  ]
}
```

| Key | Meaning |
| --- | --- |
| `defaultRegistry` | Registry used for references without a prefix. |
| `registries[]` | The configured registries. |
| `registries[].name` | Name used as `name:package`. |
| `registries[].type` | `http` or `github`. |
| `registries[].url` | Base URL for HTTP registries. |
| `githubToken` | Token for the GitHub API. |
| `root`, `binDir`, `installDir` | Override the default directories. |

---

## Using BinPm.Core as a library

```csharp
using BinPm.Core.Configuration;
using BinPm.Core.Install;
using BinPm.Core.Registry;

var config = BinPmConfig.Load();
var paths = Paths.Create(config);
paths.EnsureDirectories();

using var http = new HttpClient();
var registry = new GitHubRegistryClient(http, config.GitHubToken);

var manifest = await registry.GetPackageAsync("BurntSushi/ripgrep");
var resolved = new PackageResolver().Resolve(manifest, "^14.0.0", "github:BurntSushi/ripgrep");

var installer = new BinaryInstaller(paths, http);
var result = await installer.InstallAsync(resolved);

Console.WriteLine($"Installed {result.Record.Name} {result.Record.Version}");
```

The main types:

| Type | Purpose |
| --- | --- |
| `PackageManifest`, `PackageVersion`, `Asset` | The manifest model. |
| `VersionRange`, `SemVersion` | Version parsing and selection. |
| `Platform` | OS/CPU detection and asset matching. |
| `IRegistryClient`, `HttpRegistryClient`, `GitHubRegistryClient` | Package sources. |
| `RegistryResolver` | Turns `owner/repo` or `mirror:tool` into a client plus package name. |
| `PackageResolver` | Picks a version and an asset for the current platform. |
| `BinaryInstaller` | Downloads, verifies, extracts, shims and records packages. |
| `ShimManager` | Creates and removes shims. |
| `InstalledPackageStore` | The local package database. |
| `ArchiveExtraction` | Magic-byte based zip/tar/gz extraction. |

---

## Installing BinPm itself

BinPm publishes self-contained binaries for Linux, macOS and Windows on every release:

```bash
binpm install atlis-svg/binpm
```

On Gentoo there is an ebuild in this repository (see below). Otherwise, download the archive for
your platform from the [releases page](https://github.com/atlis-svg/binpm/releases) — the
`linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` and `win-x64` builds are single files with
the .NET runtime bundled in.

---

## Gentoo

The `gentoo/` directory contains a Portage ebuild for BinPm:

```
gentoo/app-misc/binpm/
├── binpm-0.1.0.ebuild
├── metadata.xml
└── files/binpm.wrapper
```

To use it, point Portage at the repository as a local overlay:

```bash
mkdir -p /var/db/repos/binpm
git clone https://github.com/atlis-svg/binpm /var/db/repos/binpm
```

```bash
# /etc/portage/repos.conf/binpm.conf
[binpm]
location = /var/db/repos/binpm
priority = 1
```

```bash
emerge --ask binpm
```

The ebuild depends on `virtual/dotnet-sdk:8.0` (the SDK to build, the runtime to run) and
installs `/usr/bin/binpm` as a wrapper around the published assembly in `/usr/lib/binpm`.
The test suite needs network access to restore xunit from NuGet, so it is skipped unless
`FEATURES="test"` is set.

Because BinPm manages binaries in its own store (`~/.local/share/binpm` on Linux) rather than in
`/usr/bin`, it does not clash with Portage's own files. `binpm doctor` prints the directories it
uses and tells you whether the shim directory is on your `PATH`.

---

## Building and testing

```bash
./build.sh          # build + run the test suite
./run.sh --help     # run the CLI from source
```

Or with the SDK directly:

```bash
dotnet build BinPm.sln -c Release
dotnet test tests/BinPm.Core.Tests/BinPm.Core.Tests.csproj
```

The test suite covers version ranges, platform matching, archive extraction (including
path-traversal attempts), shim management, the package database, and a full install/update/
uninstall cycle against a stubbed HTTP registry.

---

## Layout

```
binpm/
├── src/BinPm.Core/          the library
│   ├── Models/              manifest, platform, installed package models
│   ├── Registry/            HTTP and GitHub registry clients
│   ├── Install/             paths, installer, shims, archive extraction
│   ├── Semver/              SemVersion and VersionRange
│   └── Configuration/       config file model
├── src/BinPm.Cli/           the binpm command-line tool
├── tests/BinPm.Core.Tests/  xunit test suite
├── examples/registry/       an example HTTP registry you can serve locally
└── build.sh, run.sh         convenience scripts
```

---

## Limitations

* `xz` and `bzip2` archives are not supported; use zip, tar, tar.gz or a raw executable.
* Shims are not created for Windows PowerShell profiles; `.cmd` wrappers are used instead.
* Uninstalling removes the package files and its shims; the download cache is kept until
  `binpm cache clean`.
* The GitHub registry uses the releases API, so it is subject to GitHub's rate limits unless a
  token is configured.
