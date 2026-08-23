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


function Save-Work {
    Write-Host "[1/3] 自动暂存全部项目修改..." -ForegroundColor Cyan

    # .gitignore 会排除本地缓存；其余修改、新文件和删除全部暂存。
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
        Write-Host "[2/3] 自动提交：$commitMessage" -ForegroundColor Cyan

        & git commit -m $commitMessage

        if ($LASTEXITCODE -ne 0) {
            throw "提交没有完成，请查看上方错误。"
        }
    }
    else {
        Write-Host "[2/3] 没有新的本地修改，继续检查待上传提交..." -ForegroundColor Cyan
    }

    Write-Host "[3/3] 自动上传到 GitHub..." -ForegroundColor Cyan

    & git push

    if ($LASTEXITCODE -ne 0) {
        throw "本地提交已保存，但上传失败。网络恢复后请重新运行脚本。"
    }

    Write-Host "[完成] 本地存档和 GitHub 上传均已完成。" -ForegroundColor Green
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
    Write-Host "[2] 完成工作：检查、提交并上传 GitHub"
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
