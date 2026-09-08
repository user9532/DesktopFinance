# DesktopFinance Installer

Builds an MSI (`DesktopFinanceSetup.msi`) that installs a self-contained,
x64 Release build of DesktopFinance — including the .NET 8 runtime and the
native SQLite driver — with Start Menu and Desktop shortcuts.

## Prerequisites

- Visual Studio 2026 with the **HeatWave** extension (Extensions → Manage
  Extensions → search "HeatWave", from FireGiant) — gives WiX v5/v6 project
  templates, IntelliSense, and F5/Build integration for `.wixproj` files.
  HeatWave itself is an authoring aid; the actual build is driven by the
  `WixToolset.Sdk` NuGet package referenced in `DesktopFinanceSetup.wixproj`,
  so `dotnet build` works identically with or without the extension installed.
- .NET 8 SDK (already required to build DesktopFinance itself).

## Building

### From Visual Studio

Open `DesktopFinanceSetup.wixproj` directly (File → Open → Project/Solution),
or open the repo folder — VS2026 recognizes SDK-style `.wixproj` files and
will offer to build them like any other project. Build the project; the MSI
is written to `Installer\bin\x64\Release\DesktopFinanceSetup.msi`.

This does **not** publish DesktopFinance for you first — run the publish
step below (or the all-in-one script) before building in Visual Studio,
since the installer packages whatever is currently in `..\publish\`.

### From the command line (recommended — does both steps)

```powershell
cd Installer
.\build-installer.ps1
```

This publishes DesktopFinance as a self-contained win-x64 Release build to
`..\publish\`, then builds the MSI from that output. Pass `-ProductVersion`
to stamp a specific version:

```powershell
.\build-installer.ps1 -ProductVersion 1.1.0
```

### Manually, step by step

```powershell
# 1. Publish the app (self-contained → bundles the .NET 8 runtime + SQLite driver)
dotnet publish ..\Finance.csproj -c Release -r win-x64 --self-contained true -p:PublishDir=..\publish\

# 2. Build the installer
dotnet build DesktopFinanceSetup.wixproj -c Release -p:PublishDir=..\publish\
```

## How it works

- **`DesktopFinanceSetup.wixproj`** — SDK-style WiX v6 project
  (`Sdk="WixToolset.Sdk/6.0.2"`). `ProductVersion` here is the MSI's version
  number; bump it for every release. Pinned to v6 rather than v7 because WiX
  Toolset v7 requires accepting FireGiant's paid Open Source Maintenance Fee
  (OSMF) EULA before it will build (`WIX7015`); v6 remains free. See
  https://docs.firegiant.com/wix/osmf/ if you want to move to v7 later.
- **`Package.wxs`** — the installer definition:
  - `<MajorUpgrade>` makes each new installer automatically remove the
    previous version before installing, keyed on the constant `UpgradeCode`
    in the `Package` element. **Never change `UpgradeCode`** once you've
    shipped a release, or upgrade detection breaks and installs will
    conflict instead of upgrading.
  - `<Files Include="$(var.PublishDir)**">` packages the *entire* publish
    output tree automatically — every DLL, the runtime files, and
    `e_sqlite3.dll` — so nothing here needs to be hand-maintained as
    DesktopFinance's dependencies change. `*.pdb` files and the per-locale
    resource-satellite subfolders (`cs\`, `de\`, `fr\`, ...) are excluded —
    the latter only carry non-English framework UI strings, and several
    locales ship identically-named files that WiX's `<Files>` glob cannot
    auto-dedupe.
  - Start Menu and Desktop shortcuts point at the installed `Finance.exe`.
  - `WixUI_InstallDir` gives the user a standard license/install-folder/
    progress/finish wizard. **`License.rtf` is a placeholder — replace its
    text with real license terms before distributing this installer.**
- **`build-installer.ps1`** — publishes DesktopFinance and builds the MSI
  in one step; see `-ProductVersion` above.

## Versioning / updates

MSI upgrades are handled entirely by `MajorUpgrade` + the constant
`UpgradeCode`: running a newer `DesktopFinanceSetup.msi` on a machine that
already has DesktopFinance installed silently removes the old version and
installs the new one, preserving the install location. The application's
SQLite database lives under `%LOCALAPPDATA%\DesktopFinance\Database\` (see
`Data/DatabaseManager.cs`), outside the install folder, so it is untouched
by upgrades or uninstalls.

There is no automatic "check for updates" mechanism — that's a separate,
optional feature (e.g. an in-app version check against a hosted JSON file
or GitHub Releases) and isn't implemented here.
