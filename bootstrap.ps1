$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$minimumRelease = 528040
$downloadUrl = "https://go.microsoft.com/fwlink/?linkid=2085155"

function Show-Message([string]$Text, [string]$Title, [string]$Buttons, [string]$Icon) {
    Add-Type -AssemblyName System.Windows.Forms
    $buttonType = [System.Enum]::Parse([System.Windows.Forms.MessageBoxButtons], $Buttons)
    $iconType = [System.Enum]::Parse([System.Windows.Forms.MessageBoxIcon], $Icon)
    [System.Windows.Forms.MessageBox]::Show(
        $Text,
        $Title,
        $buttonType,
        $iconType
    )
}

function Get-NetFrameworkRelease {
    $keyPath = "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"
    $key = Get-ItemProperty -Path $keyPath -Name Release -ErrorAction SilentlyContinue
    if ($null -eq $key) {
        return 0
    }
    return [int]$key.Release
}

try {
    if ((Get-NetFrameworkRelease) -lt $minimumRelease) {
        $choice = Show-Message `
            "Для работы Моглокера требуется .NET Framework 4.8. Скачать официальный установщик Microsoft и запустить его? Может появиться запрос контроля учётных записей." `
            "Требуется компонент Windows" "YesNo" "Information"
        if ($choice -ne [System.Windows.Forms.DialogResult]::Yes) {
            return
        }

        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $installerPath = Join-Path $env:TEMP ("MogLocker-Net48-" + [Guid]::NewGuid().ToString("N") + ".exe")
        try {
            Invoke-WebRequest -Uri $downloadUrl -OutFile $installerPath -UseBasicParsing

            $signature = Get-AuthenticodeSignature -LiteralPath $installerPath
            if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
                $signature.SignerCertificate.Subject -notmatch "CN=Microsoft Corporation") {
                throw "Подпись загруженного установщика Microsoft не прошла проверку."
            }

            $installProcess = Start-Process -FilePath $installerPath `
                -ArgumentList "/passive", "/norestart" -Verb RunAs -Wait -PassThru
        }
        finally {
            Remove-Item -LiteralPath $installerPath -Force -ErrorAction SilentlyContinue
        }

        if ($installProcess.ExitCode -notin @(0, 1641, 3010) -or
            (Get-NetFrameworkRelease) -lt $minimumRelease) {
            throw "Не удалось установить .NET Framework 4.8. Код установщика: $($installProcess.ExitCode)."
        }
    }

    $appHash = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot "MogLockerApp.exe") -Algorithm SHA256).Hash
    $installDirectory = Join-Path $env:LOCALAPPDATA ("Programs\MogLocker\" + $appHash.Substring(0, 16))
    New-Item -ItemType Directory -Force -Path $installDirectory | Out-Null
    $installedApp = Join-Path $installDirectory "MogLocker.exe"
    if (Test-Path -LiteralPath $installedApp) {
        $installedHash = (Get-FileHash -LiteralPath $installedApp -Algorithm SHA256).Hash
        if ($installedHash -ne $appHash) {
            throw "Файл установленной версии не прошёл проверку целостности: $installedApp"
        }
    }
    else {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot "MogLockerApp.exe") `
            -Destination $installedApp
    }

    $extensionArchive = Join-Path $PSScriptRoot "browser-extension.zip"
    if (-not (Test-Path -LiteralPath $extensionArchive)) {
        throw "Не найден встроенный пакет браузерного расширения: $extensionArchive"
    }
    $extensionDirectory = Join-Path $installDirectory "browser-extension"
    Expand-Archive -LiteralPath $extensionArchive -DestinationPath $extensionDirectory -Force

    $soundpadArchive = Join-Path $PSScriptRoot "soundpad.zip"
    if (-not (Test-Path -LiteralPath $soundpadArchive)) {
        throw "Не найден встроенный пакет саундпада: $soundpadArchive"
    }
    $soundpadDirectory = Join-Path $installDirectory "soundpad"
    if (Test-Path -LiteralPath $soundpadDirectory) {
        Remove-Item -LiteralPath $soundpadDirectory -Recurse -Force
    }
    Expand-Archive -LiteralPath $soundpadArchive -DestinationPath $installDirectory -Force

    $startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
    $shortcutPath = Join-Path $startMenu "Моглокер.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $installedApp
    $shortcut.WorkingDirectory = $installDirectory
    $shortcut.Description = "Монитор подозрительных блокировок Windows"
    $shortcut.Save()

    Start-Process -FilePath $installedApp -WorkingDirectory $installDirectory
}
catch {
    try {
        [void](Show-Message `
            ("Не удалось подготовить или запустить Моглокер:`r`n`r`n" + $_.Exception.Message) `
            "Ошибка запуска Моглокера" "OK" "Error")
    }
    catch {
        Write-Error $_
    }
    exit 1
}
