Add-Type -AssemblyName System.Windows.Forms

function Invoke-Dotnet {
    [CmdletBinding()]
    Param (
        [Parameter(Mandatory = $true)]
        [System.String]
        $Command,

        [Parameter(Mandatory = $true)]
        [System.String]
        $Arguments,

        [Parameter(Mandatory = $false)]
        [System.String]
        $WorkingDirectory
    )
    
    $originalLocation = Get-Location

    # Set the working directory if provided
    if ($WorkingDirectory) {
        Set-Location -Path $WorkingDirectory
    }

    $DotnetArgs = @()
    $DotnetArgs += $Command
    $DotnetArgs += ($Arguments -split "\s+")

    & dotnet $DotnetArgs | Tee-Object -Variable Output

    # Should throw if the last command failed.
    if ($LASTEXITCODE -ne 0) {
        Write-Warning -Message ($Output -join "; ")
        throw "There was an issue running the specified dotnet command."
    }

    # Return to the original location
    Set-Location -Path $originalLocation
}

# Function to update or add entries in the .env file if values differ
function Update-EnvVariable {
    param (
        [string]$key,
        [string]$newValue
    )

    # Read the entire .env file once
    $envContent = Get-Content .env

    # Check for existing value
    $existingLine = $envContent | Select-String -Pattern "$key="

    if ($existingLine) {
        # Extract the existing value
        $existingValue = $existingLine -replace "$key=", "" -replace "\s+", ""

        # Compare and update if necessary
        if ($existingValue -ne $newValue) {
            $envContent = $envContent -replace "$key=.*", "$key=$newValue"
            Write-Host "$key updated to $newValue." -ForegroundColor Green
            # Write back the modified content to the file
            Set-Content .env $envContent
        } else {
            Write-Host "No change for $key, keeping existing value." -ForegroundColor Yellow
            return # Skip the update to the file
        }
    } else {
        # If the key does not exist, add it
        $envContent += "$key=$newValue"
        Write-Host "$key added with value $newValue." -ForegroundColor Green
        # Write back the modified content to the file
        Set-Content .env $envContent
    }
}

# Function to prompt for executable and set up data directory
function Get-ClientExecutable {
    # File browser dialog to get the path to the client
    $FileBrowser = New-Object System.Windows.Forms.OpenFileDialog -Property @{
        InitialDirectory = [Environment]::GetFolderPath('Desktop')
        Filter = "MapleStory2.exe|MapleStory2.exe"
        Title = "Maplestory2 Executable"
    }
    $null = $FileBrowser.ShowDialog()

    $exePath = $FileBrowser.FileName
    if ($exePath) {
        Write-Host "Using client path: $exePath" -ForegroundColor Green
        return $exePath
    } else {
        Write-Warning -Message "No client specified, exiting."
        exit
    }
}

Write-Host "====================================" -ForegroundColor Cyan
Write-Host "======= Maple2 Setup Script ========" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan

$dotnetVersion = (Get-Command dotnet -ErrorAction SilentlyContinue).FileVersionInfo.ProductVersion

if ($dotnetVersion -lt "8.0") {
    Write-Host "Please install .Net 8.0 and run this script again." -ForegroundColor Red
    Start-Process "https://dotnet.microsoft.com/en-us/download/dotnet/8.0"
    exit
}

# Create a copy of .env.example and rename it to .env
if (Test-Path .env) {
    Write-Host ".env file already exists. Skipping." -ForegroundColor Blue
} else {
    Write-Host "Creating .env file" -ForegroundColor Green
    Copy-Item .env.example .env
}

# Extract the current MS2_DATA_FOLDER value from the .env file, ignoring comments
$currentDataPath = (Get-Content .env | Select-String -Pattern "MS2_DATA_FOLDER=").Line -replace 'MS2_DATA_FOLDER=', ''

# Get the parent directory of the current data path
$parentPath = Split-Path $currentDataPath -Parent

# Define the path to the MapleStory2 executable and the data directory
$exeFileName = "MapleStory2.exe"
$exeFullPath = Join-Path $parentPath $exeFileName
$dataPath = Join-Path $parentPath "Data"

# Check for the executable and data directory
if (Test-Path $exeFullPath) {
    Write-Host "Found existing client at: $exeFullPath" -ForegroundColor Green
} else {
    Write-Host -Message "No client executable found in the MS2_DATA_FOLDER parent directory." -ForegroundColor Red
    $exePath = Get-ClientExecutable
    $parentPath = Split-Path $exePath -Parent
    $dataPath = Join-Path $parentPath "Data"
}

# Verify if the data directory exists
if (-not (Test-Path $dataPath)) {
    Write-Warning -Message "'Data' directory not found. Please ensure the selected executable is correct." -ForegroundColor Yellow
    $exePath = Get-ClientExecutable
    $parentPath = Split-Path $exePath -Parent
    $dataPath = Join-Path $parentPath "Data"
}

# Confirm and save the data directory
Write-Host "MapleStory2 data directory: $dataPath" -ForegroundColor Green
Write-Host "====================================" -ForegroundColor Cyan

# Check if the new data path is different from the current value before updating
if ($currentDataPath -ne $dataPath) {
    Write-Host "Updating MS2_DATA_FOLDER in .env file." -ForegroundColor Green
    Update-EnvVariable "MS2_DATA_FOLDER" $dataPath
} else {
    Write-Host "No change for MS2_DATA_FOLDER, keeping existing value." -ForegroundColor Yellow
}

# Check if MySQL service is installed
$mysqlService = Get-Service -Name "MySQL80" -ErrorAction SilentlyContinue

if ($mysqlService) {
    # Check if the MySQL service is running
    if ($mysqlService.Status -eq 'Running') {
        Write-Host "MySQL is installed and running." -ForegroundColor Green
    } else {
        Write-Host "MySQL is installed but the service is not running." -ForegroundColor Yellow
        exit
    }
} else {
    Write-Host "MySQL is not installed. Please download and install it." -ForegroundColor Red
    Start-Process "https://dev.mysql.com/downloads/installer/"
    exit
}

# Database setup, prompt if they want to change MySQL settings
$changeSettings = Read-Host "Do you want to change the MySQL settings? (y/n)"

# If user chooses to update MySQL settings
if ($changeSettings -eq "y") {
    # Prompt for connection details
    $ip = Read-Host "MySQL host (leave blank for localhost)"
    $port = Read-Host "MySQL port (leave blank for 3306)"
    $user = Read-Host "MySQL user (leave blank for root)"
    $pass = Read-Host "MySQL password"

    if ($ip -eq "") {
        $ip = "localhost"
    }

    if ($port -eq "") {
        $port = "3306"
    }

    if ($user -eq "") {
        $user = "root"
    }

    # Update or add DB connection info in the .env file
    Update-EnvVariable "DB_IP" $ip
    Update-EnvVariable "DB_PORT" $port
    Update-EnvVariable "DB_USER" $user
    Update-EnvVariable "DB_PASSWORD" $pass

    Write-Host "MySQL settings updated." -ForegroundColor Green
} else {
    Write-Host "Keeping the existing MySQL settings." -ForegroundColor Yellow
}

Write-Host "====================================" -ForegroundColor Cyan
Write-Host "MySQL setup complete."
Write-Host "====================================" -ForegroundColor Cyan
Write-Host "Initializing project..." -ForegroundColor Blue

# Ask if they want to skip navmesh generation
Write-Host "You need navmeshes to run the server, you can either generate them or download them from the discord server. Link in README.md." -ForegroundColor Yellow
Write-Host "Navmesh generation can take up to an hour depending on your system." -ForegroundColor Yellow

$answer = Read-Host "Do you want to skip navmesh generation? (y/n)"

if ($answer -eq "y") {
    $dotnetArguments = "--skip-navmesh"
} else {
    $dotnetArguments = "--init"
}

Invoke-Dotnet -Command "run" -Arguments $dotnetArguments -WorkingDirectory "Maple2.File.Ingest"

Write-Host "====================================" -ForegroundColor Cyan
Write-Host "Done! Happy Mapling!" -ForegroundColor Green