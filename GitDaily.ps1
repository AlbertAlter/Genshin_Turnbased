param(
    [ValidateSet("Menu", "Start", "Save", "Status", "Setup")]
    [string]$Action = "Menu",

    [string]$ScriptDirectory = $PSScriptRoot,

    [string]$CommitMessage = ""
)

$ErrorActionPreference = "Stop"
$expectedRemote = "https://github.com/AlbertAlter/Genshin_Turnbased.git"
$gitUserName = "AlbertAlter"
$gitUserEmail = "1392402650@qq.com"
$managedPaths = @(
    "Genshin_Project/Assets",
    "Docs",
    "Charts",
    "art_assets"
)
$scriptPaths = @("GitDaily.cmd", "GitDaily.ps1")
$configPaths = @(".gitignore", ".gitattributes")
$commitPaths = @($managedPaths + $configPaths + $scriptPaths)

function Invoke-Git {
    & git @args
    if ($LASTEXITCODE -ne 0) {
        throw "Git 命令执行失败：git $($args -join ' ')"
    }
}

function Add-PathWithProgress {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][int]$Current,
        [Parameter(Mandatory = $true)][int]$Total
    )

    $gitExecutable = (Get-Command git -ErrorAction Stop).Source
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $gitExecutable
    $startInfo.Arguments = "add -A -- `"$RelativePath`""
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "无法启动 Git 暂存进程：$RelativePath"
    }

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $spinner = @("|", "/", "-", "\")
    $frame = 0

    while (-not $process.WaitForExit(250)) {
        $symbol = $spinner[$frame % $spinner.Count]
        Write-Host ("`r[{0}/{1}] 正在暂存 {2}  {3}  已用 {4:N1} 秒" -f $Current, $Total, $RelativePath, $symbol, $stopwatch.Elapsed.TotalSeconds) -NoNewline
        $frame++
    }

    $process.WaitForExit()
    $exitCode = $process.ExitCode
    $stopwatch.Stop()

    if ($exitCode -ne 0) {
        Write-Host ""
        throw "暂存失败：$RelativePath（git 退出码：$exitCode）"
    }

    Write-Host ("`r[{0}/{1}] 已暂存 {2}，耗时 {3:N1} 秒                    " -f $Current, $Total, $RelativePath, $stopwatch.Elapsed.TotalSeconds) -ForegroundColor Green
}

function Initialize-RepositoryContext {
    if ([string]::IsNullOrWhiteSpace($ScriptDirectory)) {
        throw "无法确定脚本目录。"
    }

    $startDirectory = (Resolve-Path -LiteralPath $ScriptDirectory).Path
    $rootOutput = & git -C $startDirectory rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($rootOutput)) {
        throw "脚本不在 Git 仓库中：$startDirectory"
    }

    $script:repoRoot = (Resolve-Path -LiteralPath $rootOutput.Trim()).Path
    Set-Location -LiteralPath $script:repoRoot

    foreach ($relativePath in $managedPaths) {
        $fullPath = Join-Path $script:repoRoot ($relativePath -replace '/', [IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) {
            throw "缺少需要管理的目录：$relativePath`n当前仓库：$script:repoRoot"
        }
    }
}

function Set-RepositoryConfiguration {
    Invoke-Git config --local user.name $gitUserName
    Invoke-Git config --local user.email $gitUserEmail

    $origin = & git remote get-url origin 2>$null
    if ($LASTEXITCODE -ne 0) {
        Invoke-Git remote add origin $expectedRemote
    }
    elseif ($origin.Trim() -ne $expectedRemote) {
        throw "origin 当前为：$($origin.Trim())`n预期为：$expectedRemote`n为避免推送到错误仓库，脚本已停止。"
    }
}

function Assert-MainBranch {
    $branch = (& git branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0 -or $branch -ne "main") {
        throw "当前分支不是 main（当前：$branch），脚本已停止。"
    }
}

function Pause-Menu {
    [void](Read-Host "按回车键继续")
}

function Show-GitStatus {
    Write-Host "当前修改（整个仓库）：" -ForegroundColor Cyan
    Write-Host "----------------------------------------------"
    Invoke-Git status --short
    Write-Host "----------------------------------------------"
    Write-Host "脚本管理路径：$($managedPaths -join ', ')"
}

function Start-Work {
    Set-RepositoryConfiguration
    Assert-MainBranch

    $changes = @(& git status --porcelain)
    if ($LASTEXITCODE -ne 0) {
        throw "无法检查 Git 状态。"
    }
    if ($changes.Count -gt 0) {
        Write-Host "[停止] 仓库存在尚未提交的修改，暂不拉取，以免冲突。" -ForegroundColor Yellow
        Invoke-Git status --short
        return
    }

    Write-Host "从 GitHub 拉取 main 分支..." -ForegroundColor Cyan
    Invoke-Git pull --ff-only origin main
    Write-Host "[完成] 本地已经与 GitHub 同步。" -ForegroundColor Green
}

function Save-Work {
    Set-RepositoryConfiguration
    Assert-MainBranch

    Write-Host "开始逐项暂存指定目录..." -ForegroundColor Cyan
    for ($index = 0; $index -lt $managedPaths.Count; $index++) {
        Add-PathWithProgress -RelativePath $managedPaths[$index] -Current ($index + 1) -Total ($managedPaths.Count + 1)
    }

    Write-Host "[5/5] 正在暂存 Git 配置和日常脚本..." -ForegroundColor Cyan
    Invoke-Git add -A -- @configPaths
    Invoke-Git add -f -- @scriptPaths
    Write-Host "[5/5] Git 配置和日常脚本已暂存。" -ForegroundColor Green

    & git diff --cached --quiet -- @commitPaths
    $diffExitCode = $LASTEXITCODE
    if ($diffExitCode -eq 0) {
        Write-Host "[提示] 指定路径中没有需要提交的修改。" -ForegroundColor Yellow
        return
    }
    if ($diffExitCode -ne 1) {
        throw "无法检查暂存内容。"
    }

    Write-Host "本次只会提交以下路径中的修改：" -ForegroundColor Cyan
    Invoke-Git diff --cached --stat -- @commitPaths

    if ([string]::IsNullOrWhiteSpace($CommitMessage)) {
        $effectiveCommitMessage = "自动同步 $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    }
    else {
        $effectiveCommitMessage = $CommitMessage.Trim()
    }

    # --only 确保仓库中其他已暂存内容不会被这次提交带上。
    Write-Host "自动提交：$effectiveCommitMessage" -ForegroundColor Cyan
    Invoke-Git commit --only -m $effectiveCommitMessage -- @commitPaths
    Invoke-Git push -u origin main
    Write-Host "[完成] 指定路径已提交并上传。" -ForegroundColor Green
}

Initialize-RepositoryContext

switch ($Action) {
    "Setup"  { Set-RepositoryConfiguration; Write-Host "[完成] Git 身份和远端配置正确。" -ForegroundColor Green; exit 0 }
    "Status" { Show-GitStatus; exit 0 }
    "Start"  { Start-Work; exit 0 }
    "Save"   { Save-Work; exit 0 }
}

$exitRequested = $false
while (-not $exitRequested) {
    Clear-Host
    Write-Host "=============================================="
    Write-Host "      Genshin TurnBased - Git 日常同步"
    Write-Host "=============================================="
    Write-Host "当前仓库：$repoRoot"
    Write-Host "盘符自动识别；管理四个指定目录。"
    Write-Host ""
    Write-Host "[1] 开始工作：工作区干净时拉取 main"
    Write-Host "[2] 完成工作：自动提交指定目录并上传"
    Write-Host "[3] 查看整个仓库状态"
    Write-Host "[4] 配置/检查 Git 身份和远端"
    Write-Host "[5] 退出"
    Write-Host ""

    switch (Read-Host "请选择 1-5") {
        "1" { Start-Work; Pause-Menu }
        "2" { Save-Work; Pause-Menu }
        "3" { Show-GitStatus; Pause-Menu }
        "4" { Set-RepositoryConfiguration; Write-Host "[完成] 配置正确。" -ForegroundColor Green; Pause-Menu }
        "5" { $exitRequested = $true }
        default { Write-Host "请输入 1、2、3、4 或 5。" -ForegroundColor Yellow; Pause-Menu }
    }
}
