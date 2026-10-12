# USB メモリなどで持ち歩けるポータブル版を発行する。
# .NET ランタイムを同梱した単一の KeyNest.exe と、ポータブルモードの目印の portable ファイルを出力する。
#   pwsh -File tools/publish-portable.ps1            # publish/KeyNest-portable-win-x64 に出力
#   pwsh -File tools/publish-portable.ps1 -Zip       # 配布用の zip も作る
#   pwsh -File tools/publish-portable.ps1 -Runtime win-arm64
#   pwsh -File tools/publish-portable.ps1 -NoCompress  # exe を圧縮しない
#
# 既定では exe を圧縮する（約 190MB → 約 80MB）。起動のたびに展開するが、読み込みの遅い USB メモリでは
# ファイルが小さい方が有利なことが多い（試した PC では起動時間に差は無かった）。
#
# 出力先に data フォルダ（データファイル・設定）があっても消さない・上書きしない・zip に含めない。
# 使っている USB メモリのフォルダへ発行し直しても、データが失われないようにするため。
param(
    [ValidateSet("win-x64", "win-arm64", "win-x86")]
    [string]$Runtime = "win-x64",
    [string]$Output,
    [switch]$Zip,
    [switch]$NoCompress
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "src\KeyNest.App\KeyNest.App.csproj"
if (-not $Output) { $Output = Join-Path $root "publish\KeyNest-portable-$Runtime" }
$Output = [System.IO.Path]::GetFullPath($Output)

Write-Host "発行しています: $Runtime → $Output"
dotnet publish $project -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:EnableCompressionInSingleFile=$((-not $NoCompress).ToString().ToLowerInvariant()) `
    -o $Output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish が失敗しました（終了コード $LASTEXITCODE）。" }

# ポータブルモードの目印（中身は説明だけで、ファイルがあることに意味がある）
$marker = Join-Path $Output "portable"
if (-not (Test-Path $marker)) {
    Set-Content -Path $marker -Encoding utf8 -Value @(
        "このファイルが KeyNest.exe と同じフォルダにあると、ポータブルモードで動きます。"
        "データファイルと設定は data フォルダに保存されます。"
        "削除すると、データはこの PC のユーザーフォルダ（%APPDATA%\KeyNest）に保存されるようになります。"
    )
}

$exe = Get-Item (Join-Path $Output "KeyNest.exe")
Write-Host ("作成しました: {0}（{1:N1} MB）" -f $exe.FullName, ($exe.Length / 1MB))

if ($Zip) {
    $zipPath = "$Output.zip"
    # data フォルダ（利用者のデータ）は配布物に含めない
    $items = Get-ChildItem $Output | Where-Object { $_.Name -ne "data" }
    Compress-Archive -Path $items.FullName -DestinationPath $zipPath -Force
    Write-Host ("zip を作成しました: {0}（{1:N1} MB）" -f $zipPath, ((Get-Item $zipPath).Length / 1MB))
}
