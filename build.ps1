# Builds Dynamic Key Prompts: the loader (C++, as dinput8.dll and as xinput1_3.dll) and the core
# (DynamicKeyPrompts.dll, C# NativeAOT), laid out in .\out\dist exactly as they go into the game folder.
#
#   pwsh .\build.ps1                               build
#   pwsh .\build.ps1 -Install                      build and copy into the game folder
#   pwsh .\build.ps1 -Install -Loader xinput1_3    same, with the alternative loader
#   pwsh .\build.ps1 -Package                      build and create the release zips in .\out
#
# The game folder is found through Steam; override with -GameDir or the DS2_GAME_DIR variable.
param(
    [switch]$Install,
    [switch]$Package,
    [ValidateSet("dinput8", "xinput1_3")]
    [string]$Loader = "dinput8",
    [string]$Configuration = "Release",
    [string]$GameDir = $env:DS2_GAME_DIR
)
$loaders = "dinput8", "xinput1_3"
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$modName = "DynamicKeyPrompts"

# ---------------------------------------------------------------- dependencies

# dearxan (Arxan neutering) is a prebuilt static library, downloaded instead of committed.
$dearxanVersion = "0.5.6"
$dearxanSha256 = "12697a497be8ef43927cf2671152443c281195fd0176164a3ab184f6159ac5e3"
$dearxanDir = Join-Path $root "third_party\dearxan"
if (-not (Test-Path "$dearxanDir\lib\dearxan.lib")) {
    $zip = Join-Path $root "third_party\dearxan-$dearxanVersion.zip"
    if (-not (Test-Path $zip)) {
        $url = "https://github.com/tremwil/dearxan/releases/download/v$dearxanVersion/dearxan-$dearxanVersion.zip"
        Write-Host "Downloading $url"
        Invoke-WebRequest $url -OutFile $zip
    }
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $dearxanSha256) { Remove-Item $zip; throw "dearxan-$dearxanVersion.zip: SHA-256 mismatch ($hash)" }
    Expand-Archive $zip -DestinationPath $dearxanDir -Force
}

# ---------------------------------------------------------------- build

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
# The NativeAOT linker step locates MSVC through vswhere.exe on PATH.
$env:PATH = "$(Split-Path $vswhere);$env:PATH"
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\amd64\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild) { throw "MSBuild not found (install Visual Studio with the C++ desktop workload)" }

foreach ($proxy in $loaders) {
    & $msbuild "$root\src\Loader\Loader.vcxproj" -p:Configuration=$Configuration -p:Platform=x64 -p:Proxy=$proxy -nologo -v:m
    if ($LASTEXITCODE) { throw "loader build failed ($proxy)" }
}

$corePublish = "$root\obj\Core\publish"
& dotnet publish "$root\src\Core\Core.csproj" -c $Configuration -o $corePublish --nologo
if ($LASTEXITCODE) { throw "core build failed" }

$dist = "$root\out\dist"
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force "$dist\$modName" | Out-Null
Copy-Item "$root\out\$Configuration\dinput8.dll" $dist
Copy-Item "$corePublish\$modName.dll" "$dist\$modName\"
Copy-Item "$root\src\Core\$modName.ini" "$dist\$modName\"
# The alternative loader is shipped separately (a player installs one of the two).
$distAlt = "$root\out\dist-xinput1_3"
if (Test-Path $distAlt) { Remove-Item $distAlt -Recurse -Force }
New-Item -ItemType Directory -Force $distAlt | Out-Null
Copy-Item "$root\out\$Configuration\xinput1_3.dll" $distAlt
Write-Host "Built into $dist (alternative loader: $distAlt)"

# ---------------------------------------------------------------- package

if ($Package) {
    $version = ([xml](Get-Content "$root\src\Core\Core.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    # Player-facing readme (install / settings) at the top of the archive, where it is seen first;
    # the repository README is for developers. Legal files go into the mod folder.
    Copy-Item "$root\package\$modName-README.txt" $dist
    Copy-Item "$root\LICENSE", "$root\THIRD-PARTY-NOTICES.md" "$dist\$modName\"
    Copy-Item "$root\package\$modName-xinput1_3-loader-README.txt" $distAlt
    $zips = @{ "$root\out\$modName-$version.zip" = "$dist\*"; "$root\out\$modName-$version-xinput1_3-loader.zip" = "$distAlt\*" }
    foreach ($zip in $zips.Keys) {
        if (Test-Path $zip) { Remove-Item $zip }
        Compress-Archive -Path $zips[$zip] -DestinationPath $zip
        Write-Host "Packaged $zip"
    }
}

# ---------------------------------------------------------------- install

function Find-GameDir {
    $steam = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { return $null }
    $libraries = @($steam)
    $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
        $libraries += Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' | ForEach-Object { $_.Matches[0].Groups[1].Value -replace '\\\\', '\' }
    }
    foreach ($lib in $libraries | Select-Object -Unique) {
        $dir = Join-Path $lib "steamapps\common\Dark Souls II Scholar of the First Sin\Game"
        if (Test-Path (Join-Path $dir "DarkSoulsII.exe")) { return $dir }
    }
    return $null
}

if ($Install) {
    if (-not $GameDir) { $GameDir = Find-GameDir }
    if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir "DarkSoulsII.exe"))) {
        throw "Game folder not found. Pass -GameDir '<...>\Dark Souls II Scholar of the First Sin\Game' or set DS2_GAME_DIR."
    }
    Copy-Item "$root\out\$Configuration\$Loader.dll" $GameDir -Force
    New-Item -ItemType Directory -Force "$GameDir\$modName" | Out-Null
    Copy-Item "$dist\$modName\$modName.dll" "$GameDir\$modName\" -Force
    # Keep the player's settings.
    if (-not (Test-Path "$GameDir\$modName\$modName.ini")) { Copy-Item "$dist\$modName\$modName.ini" "$GameDir\$modName\" }
    Write-Host "Installed into $GameDir with $Loader.dll"
    $other = $loaders | Where-Object { $_ -ne $Loader }
    if (Test-Path "$GameDir\$other.dll") {
        Write-Warning "$other.dll is also in the game folder. If it is this mod's other loader, delete it (only one is used)."
    }
}
