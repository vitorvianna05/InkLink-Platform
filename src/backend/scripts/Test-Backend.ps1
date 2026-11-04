param([switch]$NoBuild,[string[]]$Project=@(),[string]$ResultsDirectory='artifacts/test-results')
$ErrorActionPreference='Stop'
$repoRoot=(Resolve-Path "$PSScriptRoot/../../..").Path
Push-Location $repoRoot
try {
    if(!$NoBuild){
        dotnet restore InkLink.Platform.slnx --locked-mode
        if($LASTEXITCODE -ne 0){throw 'Locked restore failed.'}
        dotnet build InkLink.Platform.slnx --no-restore --configuration Release --warnaserror
        if($LASTEXITCODE -ne 0){throw 'Release build failed.'}
    }
    $projects=@(Get-ChildItem src/backend -Recurse -Filter '*Tests.csproj'|Sort-Object FullName)
    if($Project.Count){
        $projects=@($projects|Where-Object {$_.BaseName -in $Project})
        if($projects.Count -ne $Project.Count){throw 'Unknown or duplicate test project selection.'}
    }
    $failed=[Collections.Generic.List[string]]::new()
    foreach($item in $projects){
        # Each project has a distinct prefix, even when TRX files share a timestamp.
        dotnet test $item.FullName --no-build --configuration Release --logger "trx;LogFilePrefix=$($item.BaseName)" --results-directory $ResultsDirectory
        if($LASTEXITCODE -ne 0){$failed.Add($item.BaseName)}
    }
    if($failed.Count){throw ('Test projects failed: '+($failed -join ', '))}
    Write-Host "$($projects.Count) test projects completed successfully."
} finally {Pop-Location}
