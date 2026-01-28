Param(
    [string]$Version,
    [switch]$NoPortable
)

$ErrorActionPreference = 'Stop'

$portableMode = -not $NoPortable

function Get-VersionFromProject {
    param($ProjectPath)
    $xml = Get-Content $ProjectPath -Raw
    if ($xml -match '<Version>(?<ver>[^<]+)') {
        return $Matches['ver']
    }
    return $null
}

function Get-PreferredLlamaVariantFromProject {
    param($ProjectPath)
    $xml = Get-Content $ProjectPath -Raw
    if ($xml -match '<PreferredLlamaCpuVariant>(?<v>[^<]+)') {
        return $Matches['v']
    }
    return $null
}

function Get-LocalizationCultures {
    param($LocalizationDir)

    if (-not (Test-Path $LocalizationDir)) {
        return @()
    }

    $cultures = @()
    $files = Get-ChildItem $LocalizationDir -Filter 'Strings*.xaml' -File -ErrorAction SilentlyContinue
    foreach ($file in $files) {
        if ($file.BaseName -notmatch '^Strings\.(?<culture>.+)$') {
            continue
        }

        $culture = $Matches['culture']
        if ([string]::IsNullOrWhiteSpace($culture)) {
            continue
        }

        $cultures += $culture
        $neutral = $culture.Split('-')[0]
        if ($neutral -and $neutral -ne $culture) {
            $cultures += $neutral
        }
    }

    return ($cultures | Sort-Object -Unique)
}

function Get-CscPath {
    $paths = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
    )
    foreach ($candidate in $paths) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }
    throw "csc.exe konnte nicht gefunden werden."
}

function New-LauncherExe {
    param(
        [string]$OutputPath
    )

    $source = @"
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
        var appDir = Path.Combine(baseDir, "app");
        var targetExe = Path.Combine(appDir, "DCM.App.exe");

        if (!File.Exists(targetExe))
        {
            MessageBox.Show("DCM.App.exe wurde nicht gefunden:\n" + targetExe, "Dabis Content Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = targetExe,
            WorkingDirectory = appDir,
            UseShellExecute = false,
            Arguments = BuildArguments(args)
        };

        try
        {
            var process = Process.Start(psi);
            if (process == null)
            {
                MessageBox.Show("Die Anwendung konnte nicht gestartet werden.", "Dabis Content Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                process.WaitForExit();
                Environment.Exit(process.ExitCode);
            }
            finally
            {
                process.Dispose();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Fehler beim Starten der Anwendung:\n" + ex, "Dabis Content Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string BuildArguments(string[] args)
    {
        if (args == null || args.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < args.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            var argument = args[i] ?? string.Empty;
            builder.Append(QuoteArgument(argument));
        }

        return builder.ToString();
    }

    private static string QuoteArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (value.IndexOfAny(new[] { ' ', '\t', '"' }) == -1)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
"@

    $tempFile = [System.IO.Path]::GetTempFileName() + '.cs'
    Set-Content -Path $tempFile -Value $source -Encoding UTF8

    $csc = Get-CscPath
    & $csc /nologo /target:winexe /optimize+ /out:$OutputPath /r:System.Windows.Forms.dll $tempFile

    Remove-Item $tempFile -Force
}

function Sync-LlamaNativesToPublish {
    param(
        [string]$BuildRidDir,
        [string]$PublishDir
    )

    if (-not (Test-Path $BuildRidDir)) {
        Write-Host "WARN: Build RID dir nicht gefunden, überspringe Llama Native Sync: $BuildRidDir" -ForegroundColor Yellow
        return
    }

    $publishLlama = Join-Path $PublishDir 'llama.dll'
    $buildLlama = Join-Path $BuildRidDir 'llama.dll'

    if (-not (Test-Path $buildLlama)) {
        Write-Host "WARN: build llama.dll nicht gefunden, überspringe: $buildLlama" -ForegroundColor Yellow
        return
    }

    $rootFiles = @(
        'llama.dll',
        'mtmd.dll',
        'ggml.dll',
        'ggml-base.dll',
        'ggml-cpu.dll',
        'ggml-vulkan.dll'
    )

    foreach ($name in $rootFiles) {
        $src = Join-Path $BuildRidDir $name
        if (Test-Path $src) {
            Copy-Item $src -Destination $PublishDir -Force
        }
    }

    $hBuild = (Get-FileHash $buildLlama -Algorithm SHA256).Hash
    if (Test-Path $publishLlama) {
        $hPub = (Get-FileHash $publishLlama -Algorithm SHA256).Hash
        if ($hBuild -eq $hPub) {
            Write-Host "OK: llama.dll im Publish entspricht Build (SHA256 match)" -ForegroundColor Green
        }
        else {
            Write-Host "WARN: llama.dll Hash im Publish weicht weiterhin ab" -ForegroundColor Yellow
            Write-Host "      Build:  $hBuild"
            Write-Host "      Pub:    $hPub"
        }
    }
}

function Sync-LlamaRuntimeFoldersToPublish {
    param(
        [string]$RepoRoot,
        [string]$PublishDir,
        [string]$PreferredVariant
    )

    $candidateRoots = @(
        (Join-Path $RepoRoot 'DCM.App\bin\Release\net9.0-windows'),
        (Join-Path $RepoRoot 'DCM.App\bin\Release\net9.0-windows\win-x64'),
        (Join-Path $RepoRoot 'DCM.App\bin\Debug\net9.0-windows'),
        (Join-Path $RepoRoot 'DCM.App\bin\Debug\net9.0-windows\win-x64')
    )

    $runtimeRoot = $null
    foreach ($root in $candidateRoots) {
        if (Test-Path (Join-Path $root 'runtimes\win-x64\native')) {
            $runtimeRoot = $root
            break
        }
    }

    if (-not $runtimeRoot) {
        Write-Host "WARN: Konnte kein Build-Output mit runtimes\win-x64\native finden. Überspringe Llama Runtime Folder Sync." -ForegroundColor Yellow
        return
    }

    $variantsToCopy = @($PreferredVariant, 'vulkan') | Select-Object -Unique
    foreach ($v in $variantsToCopy) {
        $src = Join-Path $runtimeRoot ("runtimes\win-x64\native\{0}" -f $v)
        if (-not (Test-Path $src)) { continue }

        $dst = Join-Path $PublishDir ("runtimes\win-x64\native\{0}" -f $v)
        New-Item -ItemType Directory -Force -Path $dst | Out-Null
        Copy-Item (Join-Path $src '*') -Destination $dst -Recurse -Force

        Write-Host "OK: Copied runtimes\win-x64\native\$v into publish." -ForegroundColor Green
    }
}

function Trim-RuntimesToWindowsOnly {
    param(
        [string]$PublishDir,
        [string]$PreferredVariant
    )

    $runtimesDir = Join-Path $PublishDir 'runtimes'
    if (-not (Test-Path $runtimesDir)) {
        return
    }

    # 1) Unter runtimes nur diese Top-Level Ordner behalten:
    #    - win-x64      (LLamaSharp CPU/Vulkan native variants)
    #    - vulkan       (Whisper Vulkan runtime folder)
    $keepTop = @('win-x64', 'vulkan')
    Get-ChildItem -Path $runtimesDir -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        if ($keepTop -notcontains $_.Name) {
            Remove-Item $_.FullName -Recurse -Force
        }
    }

    # 2) Unter runtimes\vulkan nur win-x64 behalten (Whisper DLLs liegen dort)
    $vulkanDir = Join-Path $runtimesDir 'vulkan'
    if (Test-Path $vulkanDir) {
        Get-ChildItem -Path $vulkanDir -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            if ($_.Name -ne 'win-x64') {
                Remove-Item $_.FullName -Recurse -Force
            }
        }
    }

    # 3) Unter runtimes\win-x64 nur "native" behalten
    $winRidDir = Join-Path $runtimesDir 'win-x64'
    if (Test-Path $winRidDir) {
        Get-ChildItem -Path $winRidDir -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            if ($_.Name -ne 'native') {
                Remove-Item $_.FullName -Recurse -Force
            }
        }

        # 4) Unter runtimes\win-x64\native nur:
        #    - deine bevorzugte CPU-Variante (z.B. avx2)
        #    - vulkan (falls LLamaSharp Vulkan native nutzt)
        $nativeDir = Join-Path $winRidDir 'native'
        if (Test-Path $nativeDir) {
            $keepNative = @($PreferredVariant, 'vulkan') | Select-Object -Unique
            Get-ChildItem -Path $nativeDir -Directory -ErrorAction SilentlyContinue | ForEach-Object {
                if ($keepNative -notcontains $_.Name) {
                    Remove-Item $_.FullName -Recurse -Force
                }
            }
        }
    }

    Write-Host "OK: Trimmed runtimes to Windows-only (kept win-x64 native + vulkan whisper runtime)." -ForegroundColor Green
}

# --------------------------------------------------------------------------------------

$projectPath = Join-Path $PSScriptRoot 'DCM.App\DCM.App.csproj'
if (-not (Test-Path $projectPath)) {
    throw "Projektdatei nicht gefunden: $projectPath"
}

if (-not $Version) {
    $Version = Get-VersionFromProject -ProjectPath $projectPath
}

if (-not $Version) {
    throw "Version konnte nicht ermittelt werden. Bitte Parameter -Version angeben."
}

$preferredVariant = Get-PreferredLlamaVariantFromProject -ProjectPath $projectPath
if (-not $preferredVariant) { $preferredVariant = 'avx2' }

$releaseTag = "v$($Version.TrimStart('v','V'))"
$releaseRoot = Join-Path $PSScriptRoot "releases\$releaseTag"
$appDir = Join-Path $releaseRoot 'app'

if (Test-Path $releaseRoot) {
    Remove-Item $releaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $appDir | Out-Null

$publishArgs = @(
    'publish', $projectPath,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    "-p:Version=$Version",
    "-p:PreferredLlamaCpuVariant=$preferredVariant",
    "-p:PortableMode=$portableMode",
    '-o', $appDir
)

dotnet @publishArgs

# 1) Root native DLLs (llama/ggml/mtmd) aus dem schnellen RID-Output in den Publish spiegeln
$buildRidDir = Join-Path $PSScriptRoot 'DCM.App\bin\Release\net9.0-windows\win-x64'
Sync-LlamaNativesToPublish -BuildRidDir $buildRidDir -PublishDir $appDir

# 2) Zusätzlich die win-x64 Runtime Variant-Folder spiegeln (avx2 + vulkan),
#    damit LLamaSharp exakt wie im bin-Output auflösen kann.
Sync-LlamaRuntimeFoldersToPublish -RepoRoot $PSScriptRoot -PublishDir $appDir -PreferredVariant $preferredVariant

# 3) Runtimes auf Windows-only reduzieren (kein Linux/OSX im Release)
Trim-RuntimesToWindowsOnly -PublishDir $appDir -PreferredVariant $preferredVariant

# Cleanup: nur runtimes + Localization-Cultures + DabisContentManager behalten (alles andere an Top-Level-Dirs weg)
$localizationCultures = Get-LocalizationCultures -LocalizationDir (Join-Path $PSScriptRoot 'DCM.App\Localization')
$directories = Get-ChildItem -Path $appDir -Directory -ErrorAction SilentlyContinue
foreach ($dir in $directories) {
    if ($dir.Name -eq 'runtimes' -or $dir.Name -eq 'DabisContentManager' -or $localizationCultures -contains $dir.Name) {
        continue
    }
    Remove-Item $dir.FullName -Recurse -Force
}

# PDBs entfernen
$pdbFiles = Get-ChildItem -Path $appDir -Recurse -Filter *.pdb -File -ErrorAction SilentlyContinue
foreach ($file in $pdbFiles) {
    Remove-Item $file.FullName -Force
}

# Launcher erstellen (Root\DCM.App.exe startet app\DCM.App.exe mit WorkingDirectory=app)
$launcherPath = Join-Path $releaseRoot 'DCM.App.exe'
New-LauncherExe -OutputPath $launcherPath

# README kopieren
$readmeSource = Join-Path $PSScriptRoot 'README.md'
if (Test-Path $readmeSource) {
    Copy-Item $readmeSource -Destination (Join-Path $releaseRoot 'README.md') -Force
}

# YouTube Client Secrets kopieren (aus lokalem Secrets-Ordner, nicht im Repo)
$secretsSource = Join-Path $env:LOCALAPPDATA 'DabisContentManager\youtube_client_secrets.json'
if (-not (Test-Path $secretsSource)) {
    # Fallback: aus dem Debug-Output
    $secretsSource = Join-Path $PSScriptRoot 'DCM.App\bin\Debug\net9.0-windows\DabisContentManager\youtube_client_secrets.json'
}
if (Test-Path $secretsSource) {
    $secretsDestDir = Join-Path $appDir 'DabisContentManager'
    New-Item -ItemType Directory -Force -Path $secretsDestDir | Out-Null
    Copy-Item $secretsSource -Destination (Join-Path $secretsDestDir 'youtube_client_secrets.json') -Force
    Write-Host "OK: YouTube Client Secrets kopiert" -ForegroundColor Green
} else {
    Write-Host "WARN: youtube_client_secrets.json nicht gefunden. Release wird ohne Secrets erstellt." -ForegroundColor Yellow
    Write-Host "      Erwartet in: $env:LOCALAPPDATA\DabisContentManager\youtube_client_secrets.json" -ForegroundColor Yellow
}

# Zip bauen
$zipPath = Join-Path $PSScriptRoot "releases\DCM-$releaseTag.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $releaseRoot '*') -DestinationPath $zipPath -Force

Write-Host "Fertig: $releaseRoot (Zip: $zipPath)" -ForegroundColor Green


