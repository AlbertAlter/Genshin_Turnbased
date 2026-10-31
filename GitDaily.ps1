param(
    [ValidateSet("Menu", "Start", "Save", "Status")]
    [string]$Action = "Save",

    [string]$ScriptDirectory = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ScriptDirectory)) {
    throw "无法确定脚本目录。请通过 GitDaily.cmd 启动。"
}

$repoRoot = (Resolve-Path $ScriptDirectory).Path
Set-Location $repoRoot


function Assert-GitRepository {
    & git rev-parse --show-toplevel *> $null

    if ($LASTEXITCODE -ne 0) {
        throw "当前目录不是 Git 仓库：$repoRoot。请确认 GitDaily.cmd 和 GitDaily.ps1 位于仓库根目录。"
    }
}


function Pause-Menu {
    [void](Read-Host "按回车键继续")
}


function Show-GitStatus {
    Write-Host "当前修改：" -ForegroundColor Cyan
    Write-Host "----------------------------------------------"

    & git status --short

    if ($LASTEXITCODE -ne 0) {
        throw "git status 执行失败。"
    }

    Write-Host "----------------------------------------------"
    Write-Host "如果上方没有内容，表示工作区干净。"
}


function Assert-PsdSyncConfiguration {
    $psdRoot = Join-Path $repoRoot "PSDfiles"

    & git lfs version *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "未检测到 Git LFS，无法安全同步 PSDfiles。请先安装 Git LFS 并执行 git lfs install。"
    }

    if (-not (Test-Path -LiteralPath $psdRoot -PathType Container)) {
        throw "找不到 PSDfiles 目录：$psdRoot"
    }

    $psdFiles = @(Get-ChildItem -LiteralPath $psdRoot -Recurse -File -Filter "*.psd")
    foreach ($file in $psdFiles) {
        $relativePath = (Resolve-Path -LiteralPath $file.FullName -Relative) -replace '^\.\\', ''
        $attribute = & git check-attr filter -- $relativePath

        if ($LASTEXITCODE -ne 0 -or $attribute -notmatch ': filter: lfs$') {
            throw "PSD 文件未配置 Git LFS：$relativePath。请确认 .gitattributes 包含 *.psd filter=lfs diff=lfs merge=lfs -text。"
        }
    }

    $trackedArtAssets = @(& git ls-files -- art_assets)
    if ($LASTEXITCODE -ne 0) {
        throw "无法检查 art_assets 的 Git 跟踪状态。"
    }
    if ($trackedArtAssets.Count -gt 0) {
        throw "art_assets 仍有 $($trackedArtAssets.Count) 个文件被 Git 跟踪，请先执行 git rm -r --cached -- art_assets。"
    }
}


function Start-Work {
    Write-Host "[1/2] 检查当前修改..." -ForegroundColor Cyan

    $changes = @(& git status --porcelain)

    if ($LASTEXITCODE -ne 0) {
        throw "git status 执行失败。"
    }

    if ($changes.Count -gt 0) {
        Write-Host ""
        Write-Host "[停止] 当前存在尚未提交的修改，暂不拉取，以免发生冲突。" -ForegroundColor Yellow

        & git status --short

        Write-Host "请先使用菜单 [2] 保存上传，或确认这些修改该如何处理。"
        return
    }

    Write-Host "[2/2] 从 GitHub 拉取 main 分支..." -ForegroundColor Cyan

    & git pull --ff-only

    if ($LASTEXITCODE -ne 0) {
        Write-Host "[失败] 拉取没有完成。请保留上方错误，不要强制操作。" -ForegroundColor Red
        return
    }

    Write-Host "[完成] 本地已经与 GitHub 同步，可以开始工作。" -ForegroundColor Green
}


function Get-PushFailureReason {
    param([string]$Output)

    if ($Output -match '(?i)GH006|GH013|protected branch|repository rule violations|cannot force.?push|force.?push.*not allowed') {
        return "远程分支保护或仓库规则禁止本次强推，不是网络故障。请检查 GitHub 分支保护设置。"
    }
    if ($Output -match '(?i)authentication failed|permission.*denied|access denied|could not read Username|terminal prompts disabled|SEC_E_NO_CREDENTIALS|repository not found|\b(401|403)\b') {
        return "GitHub 身份认证或仓库权限失败。请检查登录、凭据和仓库写入权限。"
    }
    if ($Output -match '(?i)GH001|exceeds GitHub.*limit|exceeds.*file size limit|pack exceeds|file.*larger than.*MB') {
        return "文件或推送数据超过远程限制。请检查上方 Git/LFS 错误及大文件配置。"
    }
    if ($Output -match '(?i)fetch first|non-fast-forward|stale info|\[rejected\]|\[remote rejected\]|hook declined') {
        return "远程拒绝分支更新，不是网络故障。本脚本已请求强推，请检查上方具体拒绝原因。"
    }
    if ($Output -match '(?i)could not resolve|failed to connect|couldn.t connect|connection.*(reset|refused|closed)|timed? out|timeout|SSL connect error|SSL certificate problem|TLS.*(failed|error)|HTTP (502|503|504)|remote end hung up|unexpected disconnect') {
        return "网络、代理或 TLS 连接异常。请根据上方原始错误检查连接后重试。"
    }
    return "Git 或 LFS 返回失败，请以上方原始错误为准；不能仅凭推送失败认定为网络问题。"
}


function Push-LocalMain {
    $messages = New-Object 'System.Collections.Generic.List[string]'
    $pushExitCode = $null
    $previousErrorAction = $ErrorActionPreference
    try {
        # Windows PowerShell 会将重定向的原生命令 stderr 包装为 ErrorRecord。
        # Git 进度也写在 stderr；必须显示完整输出并按退出码判断，不能提前当异常中断。
        $ErrorActionPreference = "Continue"
        & git push --force --progress origin refs/heads/main:refs/heads/main 2>&1 | ForEach-Object {
            $line = $_.ToString()
            [void]$messages.Add($line)
            Write-Host $line
        }
        $pushExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }

    if ($pushExitCode -ne 0) {
        $reason = Get-PushFailureReason -Output ($messages -join "`n")
        throw "本地提交已保存，但强推失败（Git 退出码：$pushExitCode）。$reason"
    }
}


function Save-Work {
    $branch = & git branch --show-current
    if ($LASTEXITCODE -ne 0 -or $branch -ne "main") {
        throw "上传脚本只允许在本地 main 分支运行；请切回 main，避免覆盖错误的远程分支。"
    }

    Write-Host "[1/4] 检查 PSDfiles 与 Git LFS 配置..." -ForegroundColor Cyan

    Assert-PsdSyncConfiguration

    Write-Host "[2/4] 自动暂存项目修改（包含 PSDfiles 的完整子目录结构）..." -ForegroundColor Cyan

    # .gitignore 会排除 art_assets 和本地缓存；PSDfiles 及其内部相对路径会递归暂存。
    & git add -A -- .

    if ($LASTEXITCODE -ne 0) {
        throw "暂存文件失败，请查看上方错误。"
    }


    # 根目录默认可能被 .gitignore 的 /* 规则忽略。
    # 只强制加入这两个 Git 日常脚本，不放行其他根目录文件。
    & git add -f -- GitDaily.cmd GitDaily.ps1

    if ($LASTEXITCODE -ne 0) {
        throw "暂存 Git 日常脚本失败，请确认两个文件都在仓库根目录。"
    }


    # 检查暂存区是否真的有变化。
    & git diff --cached --quiet
    $diffExitCode = $LASTEXITCODE

    if ($diffExitCode -ne 0 -and $diffExitCode -ne 1) {
        throw "无法检查暂存内容。"
    }

    if ($diffExitCode -eq 1) {
        $commitMessage = "自动同步 {0}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
        Write-Host "[3/4] 自动提交：$commitMessage" -ForegroundColor Cyan

        & git commit -m $commitMessage

        if ($LASTEXITCODE -ne 0) {
            throw "提交没有完成，请查看上方错误。"
        }
    }
    else {
        Write-Host "[3/4] 没有新的本地修改，继续检查待上传提交..." -ForegroundColor Cyan
    }

    Write-Host "[4/4] 以本地 main 为准强推 GitHub（覆盖远程 main；PSD 由 Git LFS 上传）..." -ForegroundColor Cyan

    Push-LocalMain

    Write-Host "[完成] 本地存档已保存，GitHub main 已更新为本地 main。" -ForegroundColor Green
}


Assert-GitRepository


if ($Action -eq "Status") {
    Show-GitStatus
    exit 0
}

if ($Action -eq "Start") {
    Start-Work
    exit 0
}

if ($Action -eq "Save") {
    Save-Work
    exit 0
}


$exitRequested = $false

while (-not $exitRequested) {
    Clear-Host

    Write-Host "=============================================="
    Write-Host "      Genshin TurnBased - Git 日常同步"
    Write-Host "=============================================="
    Write-Host "当前仓库：$repoRoot"
    Write-Host ""

    Write-Host "[1] 开始工作：检查干净后从 GitHub 拉取"
    Write-Host "[2] 完成工作：检查、提交并以本地 main 强推 GitHub"
    Write-Host "[3] 只查看当前修改"
    Write-Host "[4] 退出"

    Write-Host ""

    switch (Read-Host "请选择 1-4") {
        "1" {
            Start-Work
            Pause-Menu
        }

        "2" {
            Save-Work
            Pause-Menu
        }

        "3" {
            Show-GitStatus
            Pause-Menu
        }

        "4" {
            $exitRequested = $true
        }

        default {
            Write-Host "请输入 1、2、3 或 4。" -ForegroundColor Yellow
            Pause-Menu
        }
    }
}
