# Uploads an Android App Bundle to Google Play through the Play Developer API and releases it on a track, with the
# matching CHANGELOG.md section as its release notes. Shared by GitLab CI and GitHub Actions; needs PowerShell 7.
#
# Authenticates as the service account in PLAY_SERVICE_ACCOUNT_BASE64 (base64 of its JSON key). Skips with a warning
# when that isn't set. The service account must be invited in Play Console (Users and permissions) with release
# permissions for the app.
#
# Play rejects a versionCode it has seen before, so when the bundle's <ApplicationVersion> is already in Play this
# does nothing: re-running a pipeline is safe.
#
# The API can't create the app or its first release: upload the first AAB by hand in Play Console.
param(
    [Parameter(Mandatory)][string]$Aab,
    [string]$Project = 'VistumblerMAUI/VistumblerMAUI.csproj',
    # internal, alpha (closed testing), beta (open testing) or production
    [string]$Track = 'internal',
    # completed rolls the release out; use draft while the app has never been published (Play allows only drafts then)
    [ValidateSet('completed', 'draft')][string]$Status = 'completed',
    [string]$Changelog = 'CHANGELOG.md'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:PLAY_SERVICE_ACCOUNT_BASE64)) {
    Write-Warning 'PLAY_SERVICE_ACCOUNT_BASE64 is not set; skipping the Google Play upload.'
    return
}
if (-not (Test-Path $Aab)) { throw "AAB not found: $Aab" }

function Get-ProjectValue([string]$Element) {
    $m = [regex]::Match((Get-Content $Project -Raw), "<$Element>([^<]+)</$Element>")
    if (-not $m.Success) { throw "<$Element> not found in $Project" }
    $m.Groups[1].Value.Trim()
}
$packageName = Get-ProjectValue ApplicationId
$versionName = Get-ProjectValue ApplicationDisplayVersion
$versionCode = [long](Get-ProjectValue ApplicationVersion)

function ConvertTo-Base64Url([byte[]]$Bytes) {
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

# OAuth token from a JWT signed with the service account key (RS256)
$account = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:PLAY_SERVICE_ACCOUNT_BASE64)) | ConvertFrom-Json
$now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$header = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes((@{ alg = 'RS256'; typ = 'JWT' } | ConvertTo-Json -Compress)))
$claims = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes((@{
    iss   = $account.client_email
    scope = 'https://www.googleapis.com/auth/androidpublisher'
    aud   = $account.token_uri
    iat   = $now
    exp   = $now + 3600
} | ConvertTo-Json -Compress)))
$rsa = [Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem($account.private_key)
    $signature = $rsa.SignData([Text.Encoding]::ASCII.GetBytes("$header.$claims"),
        [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
}
finally {
    $rsa.Dispose()
}
$token = (Invoke-RestMethod -Method Post -Uri $account.token_uri -Body @{
    grant_type = 'urn:ietf:params:oauth:grant-type:jwt-bearer'
    assertion  = "$header.$claims.$(ConvertTo-Base64Url $signature)"
}).access_token
$headers = @{ Authorization = "Bearer $token" }

# Release notes: the lines under "## <version>" up to the next "## " heading. Play allows 500 characters.
$notes = @()
if (Test-Path $Changelog) {
    $inSection = $false
    foreach ($line in Get-Content $Changelog -Encoding UTF8) {
        if ($line -eq "## $versionName") { $inSection = $true; continue }
        if ($inSection -and $line -match '^## ') { break }
        if ($inSection) { $notes += $line }
    }
}
$notesText = ($notes -join "`n").Trim()
if (-not $notesText) { $notesText = "Version $versionName" }
if ($notesText.Length -gt 500) { $notesText = $notesText.Substring(0, 497).TrimEnd() + '...' }

# All changes go through an edit, which is discarded unless committed
$api = "https://androidpublisher.googleapis.com/androidpublisher/v3/applications/$packageName"
$edit = (Invoke-RestMethod -Method Post -Uri "$api/edits" -Headers $headers).id
$committed = $false
try {
    $bundles = @((Invoke-RestMethod -Uri "$api/edits/$edit/bundles" -Headers $headers).bundles)
    if ($bundles | Where-Object { $_ -and [long]$_.versionCode -eq $versionCode }) {
        Write-Host "versionCode $versionCode ($versionName) is already in Google Play; nothing to do."
        return
    }

    Write-Host "Uploading $Aab ($packageName $versionName, versionCode $versionCode)"
    $bundle = Invoke-RestMethod -Method Post -Headers $headers -ContentType 'application/octet-stream' -InFile $Aab `
        -Uri "https://androidpublisher.googleapis.com/upload/androidpublisher/v3/applications/$packageName/edits/$edit/bundles?uploadType=media" `
        -TimeoutSec 1800
    if ([long]$bundle.versionCode -ne $versionCode) {
        throw "Uploaded bundle has versionCode $($bundle.versionCode), but $Project says $versionCode"
    }

    $release = @{
        track    = $Track
        releases = @(@{
            name         = $versionName
            versionCodes = @("$versionCode")
            status       = $Status
            releaseNotes = @(@{ language = 'en-US'; text = $notesText })
        })
    } | ConvertTo-Json -Depth 5
    Invoke-RestMethod -Method Put -Uri "$api/edits/$edit/tracks/$Track" -Headers $headers `
        -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($release)) | Out-Null

    Invoke-RestMethod -Method Post -Uri "$api/edits/${edit}:commit" -Headers $headers | Out-Null
    $committed = $true
    Write-Host "Released $versionName (versionCode $versionCode) to the $Track track ($Status)."
}
finally {
    if (-not $committed) {
        try { Invoke-RestMethod -Method Delete -Uri "$api/edits/$edit" -Headers $headers | Out-Null } catch { }
    }
}
