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

# 上传范围只在这里设置；目录包含全部子目录，文件必须精确匹配。
$syncDirectories = @('Genshin_Project', 'Docs', 'Charts', 'PSDfiles')
$syncFiles = @('.gitignore', '.gitattributes', 'GitDaily.cmd', 'GitDaily.ps1')
$excludedPaths = @(
    ':(top,icase,literal)Genshin_Project/Assets/StreamingAssets',
    ':(top,icase,literal)Genshin_Project/Assets/StreamingAssets.meta'
)
foreach ($directory in @('Library','Temp','Obj','Build','Builds','Logs','UserSettings','MemoryCaptures','.vs','.idea','.svn')) {
    $excludedPaths += ":(top,icase,literal)Genshin_Project/$directory"
}
foreach ($extension in @('csproj','sln','suo','user','userprefs')) {
    $excludedPaths += ":(top,icase,glob)Genshin_Project/*.$extension"
}
$excludedPaths += @(':(top,glob)**/~$*', ':(top,glob)**/.DS_Store', ':(top,glob)**/Thumbs.db')
$allowedPaths = @($syncDirectories + $syncFiles | ForEach-Object { ":(top,literal)$_" })
$savePaths = $allowedPaths + @($excludedPaths -replace '^:\(', ':(exclude,')
$directorySavePaths = @($syncDirectories | ForEach-Object { ":(top,literal)$_" }) + @($excludedPaths -replace '^:\(', ':(exclude,')
$outsidePaths = @('.') + @($allowedPaths -replace '^:\(', ':(exclude,')

function Add-SyncChanges {
    # Git 枚举实际文件，避免 add 的目录遍历再次触发忽略目录提示。
    # NUL 字节流保留中文、空格、换行等文件名，literal 模式防止文件名被当成通配符。
    $listing = Invoke-GitBytes -Arguments (@('ls-files', '-z', '--cached', '--others', '--exclude-standard', '--') + $savePaths)
    if ($listing.ExitCode -ne 0) { throw "枚举暂存文件失败：$($listing.ErrorText)" }
    $paths = New-Object System.IO.MemoryStream
    try {
        $paths.Write($listing.Bytes, 0, $listing.Bytes.Length)
        # 仅这些明确配置的根文件允许绕过 .gitignore。
        foreach ($file in $syncFiles) {
            if (Test-Path -LiteralPath $file -PathType Leaf) {
                $bytes = [Text.Encoding]::UTF8.GetBytes($file + [char]0)
                $paths.Write($bytes, 0, $bytes.Length)
            }
        }
        if ($paths.Length -gt 0) {
            $result = Invoke-GitBytes -Arguments @('--literal-pathspecs', 'add', '-A', '-f', '--pathspec-from-file=-', '--pathspec-file-nul') -InputBytes $paths.ToArray()
            if ($result.ErrorText) { Write-Host $result.ErrorText }
            if ($result.ExitCode -ne 0) { throw "暂存文件失败（Git 退出码 $($result.ExitCode)）：`n$($result.ErrorText)" }
        }
    }
    finally { $paths.Dispose() }
    Remove-OutsideSyncIndex
}

function Invoke-GitBytes {
    param([string[]]$Arguments, [byte[]]$InputBytes)
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = 'git'
    $info.WorkingDirectory = (Get-Location).Path
    $info.Arguments = ($Arguments | ForEach-Object { '"' + ($_ -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"' }) -join ' '
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardErrorEncoding = [Text.Encoding]::UTF8
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $info
    $buffer = New-Object System.IO.MemoryStream
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.BaseStream.CopyToAsync($buffer)
        $stderr = $process.StandardError.ReadToEndAsync()
        if ($InputBytes) { $process.StandardInput.BaseStream.Write($InputBytes, 0, $InputBytes.Length) }
        $process.StandardInput.Close()
        $process.WaitForExit()
        $stdout.GetAwaiter().GetResult()
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Bytes = $buffer.ToArray(); ErrorText = $stderr.GetAwaiter().GetResult() }
    }
    finally { $buffer.Dispose(); $process.Dispose() }
}

function Remove-OutsideSyncIndex {
    # --cached 只清理 Git 索引，保留清单外的本地文件。
    & git rm -q -r -f --cached --ignore-unmatch -- @outsidePaths
    if ($LASTEXITCODE -ne 0) { throw '移除上传范围外的跟踪失败。' }
    & git rm -q -r -f --cached --ignore-unmatch -- @excludedPaths
    if ($LASTEXITCODE -ne 0) { throw '移除排除目录的跟踪失败。' }
}

function Get-OutsideSyncHistory {
    param([string]$Revision = 'main')
    # 排除型 pathspec 会影响同次调用内的所有正向匹配，必须分开检查。
    & git log --full-history -m --format= --name-only $Revision -- @outsidePaths
    if ($LASTEXITCODE -ne 0) { throw '无法检查范围外历史。' }
    & git log --full-history -m --format= --name-only $Revision -- @excludedPaths
    if ($LASTEXITCODE -ne 0) { throw '无法检查排除路径历史。' }
}

function Clear-OutsideSyncHistory {
    $outside = @(Get-OutsideSyncHistory)
    if (-not ($outside | Where-Object { $_ })) { return }

    # filter-branch 的 index-filter 不检出历史素材；当前版本已先取消其跟踪。
    $quote = { param($value) "'" + $value.Replace("'", "'\''") + "'" }
    $removeOutside = ($outsidePaths | ForEach-Object { & $quote $_ }) -join ' '
    $removeExcluded = ($excludedPaths | ForEach-Object { & $quote $_ }) -join ' '
    $filter = "git rm -q -r -f --cached --ignore-unmatch -- $removeOutside && git rm -q -r -f --cached --ignore-unmatch -- $removeExcluded"
    Write-Host '清理 main 全部历史中上传范围外的文件（保留本地文件）...' -ForegroundColor Yellow
    $previousWarning = $env:FILTER_BRANCH_SQUELCH_WARNING
    try {
        $env:FILTER_BRANCH_SQUELCH_WARNING = '1'
        & git filter-branch --index-filter $filter -- main
        if ($LASTEXITCODE -ne 0) { throw '历史清理失败，未上传。请保留错误输出。' }
    }
    finally { $env:FILTER_BRANCH_SQUELCH_WARNING = $previousWarning }
    $remaining = @(Get-OutsideSyncHistory)
    if ($remaining | Where-Object { $_ }) { throw '历史清理验证失败，未上传。' }
    & git update-ref -d refs/original/refs/heads/main
    if ($LASTEXITCODE -ne 0) { throw '清理旧历史备份引用失败，未上传。' }
}


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

    & git status --short -- @savePaths

    if ($LASTEXITCODE -ne 0) {
        throw "git status 执行失败。"
    }

    Write-Host "----------------------------------------------"
    Write-Host "如果上方没有内容，表示同步范围内没有修改（已排除 StreamingAssets）。"
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

}


function Start-Work {
    Write-Host "[1/2] 检查当前修改..." -ForegroundColor Cyan

    $changes = @(& git status --porcelain -- @savePaths)

    if ($LASTEXITCODE -ne 0) {
        throw "git status 执行失败。"
    }

    if ($changes.Count -gt 0) {
        Write-Host ""
        Write-Host "[停止] 当前存在尚未提交的修改，暂不拉取，以免发生冲突。" -ForegroundColor Yellow

        & git status --short -- @savePaths

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
    $query = Invoke-GitBytes -Arguments @('ls-remote', '--refs', '--heads', '--tags', 'origin')
    if ($query.ExitCode -ne 0) { throw "查询远端失败（Git $($query.ExitCode)）：`n$($query.ErrorText)" }
    $remoteRefs = @{}
    foreach ($line in ([Text.Encoding]::UTF8.GetString($query.Bytes) -split '\r?\n')) {
        if (-not $line.Trim()) { continue }
        if ($line -notmatch '^([0-9a-f]{40,64})\s+(refs/(?:heads|tags)/\S+)$') { throw "无法识别远端引用：$line" }
        $remoteRefs[$Matches[2]] = $Matches[1]
    }
    $remoteTip = if ($remoteRefs.ContainsKey('refs/heads/main')) { $remoteRefs['refs/heads/main'] } else { '' }
    $localTip = & git rev-parse --verify refs/heads/main
    if ($LASTEXITCODE -ne 0) { throw '无法确认本地 main，未推送。' }
    $unsafeHistory = @(Get-OutsideSyncHistory -Revision $localTip)
    if ($unsafeHistory | Where-Object { $_ }) { throw '待上传历史仍有范围外文件，未推送。' }

    $leases = @("--force-with-lease=refs/heads/main:${remoteTip}")
    $updates = @("${localTip}:refs/heads/main")
    $temporaryRefs = @()
    $cleanupRoot = $null
    $otherRefs = @($remoteRefs.Keys | Where-Object { $_ -ne 'refs/heads/main' })
    $previousErrorAction = $ErrorActionPreference
    try {
    if ($otherRefs.Count) {
        $urlResult = Invoke-GitBytes -Arguments @('remote', 'get-url', 'origin')
        if ($urlResult.ExitCode -ne 0) { throw $urlResult.ErrorText }
        $remoteUrl = [Text.Encoding]::UTF8.GetString($urlResult.Bytes).Trim()
        $objectsResult = Invoke-GitBytes -Arguments @('rev-parse', '--git-path', 'objects')
        if ($objectsResult.ExitCode -ne 0) { throw $objectsResult.ErrorText }
        $objects = [IO.Path]::GetFullPath((Join-Path (Get-Location).Path ([Text.Encoding]::UTF8.GetString($objectsResult.Bytes).Trim())))
        $cleanupRoot = Join-Path $repoRoot ('codex_worktable/git-remote-clean-' + [guid]::NewGuid().ToString('N'))
        $token = [guid]::NewGuid().ToString('N')
        $init = Invoke-GitBytes -Arguments @('init', '--bare', $cleanupRoot)
        if ($init.ExitCode -ne 0) { throw $init.ErrorText }
        # 复用本地只读对象，避免为清理额外复制整份素材历史。
        [IO.File]::WriteAllText((Join-Path $cleanupRoot 'objects/info/alternates'), ($objects -replace '\\','/') + "`n", (New-Object Text.UTF8Encoding($false)))
        Push-Location $cleanupRoot
        try {
            foreach ($ref in $otherRefs) {
                Write-Host "按路径清单检查远端引用：$ref" -ForegroundColor Cyan
                $fetch = Invoke-GitBytes -Arguments @('fetch', '--no-tags', $remoteUrl, "${ref}:${ref}")
                if ($fetch.ExitCode -ne 0) { throw "读取远端引用 $ref 失败：$($fetch.ErrorText)" }
                $tip = Invoke-GitBytes -Arguments @('rev-parse', '--verify', $ref)
                if ($tip.ExitCode -ne 0 -or [Text.Encoding]::UTF8.GetString($tip.Bytes).Trim() -ne $remoteRefs[$ref]) {
                    throw "远端 $ref 已变化，请重新运行脚本。"
                }
            }
            # filter-branch 需要一个有效 HEAD，临时仓库用已抓取的分支或标签指向提交。
            $head = Invoke-GitBytes -Arguments @('rev-parse', "$($otherRefs[0])^{commit}")
            if ($head.ExitCode -ne 0) { throw '远端标签未指向提交，无法自动清理。' }
            $setHead = Invoke-GitBytes -Arguments @('update-ref', 'refs/heads/gitdaily-filter-head', [Text.Encoding]::UTF8.GetString($head.Bytes).Trim())
            if ($setHead.ExitCode -ne 0) { throw $setHead.ErrorText }
            $setHead = Invoke-GitBytes -Arguments @('symbolic-ref', 'HEAD', 'refs/heads/gitdaily-filter-head')
            if ($setHead.ExitCode -ne 0) { throw $setHead.ErrorText }
            $needsCleanup = $false
            foreach ($ref in $otherRefs) {
                if (@(Get-OutsideSyncHistory -Revision $ref | Where-Object { $_ }).Count) { $needsCleanup = $true }
            }
            if ($needsCleanup) {
                $quote = { param($value) "'" + $value.Replace("'", "'\''") + "'" }
                $outside = ($outsidePaths | ForEach-Object { & $quote $_ }) -join ' '
                $excluded = ($excludedPaths | ForEach-Object { & $quote $_ }) -join ' '
                $filter = "git rm -q -r -f --cached --ignore-unmatch -- $outside && git rm -q -r -f --cached --ignore-unmatch -- $excluded"
                $oldWarning = $env:FILTER_BRANCH_SQUELCH_WARNING
                try {
                    $env:FILTER_BRANCH_SQUELCH_WARNING = '1'
                    $filtered = Invoke-GitBytes -Arguments @('filter-branch', '--tag-name-filter', 'cat', '--index-filter', $filter, '--', '--branches', '--tags')
                    if ($filtered.ExitCode -ne 0) { throw "其他分支/标签清理失败：$($filtered.ErrorText)" }
                }
                finally { $env:FILTER_BRANCH_SQUELCH_WARNING = $oldWarning }
            }
            foreach ($ref in $otherRefs) {
                if (@(Get-OutsideSyncHistory -Revision $ref | Where-Object { $_ }).Count) { throw "$ref 历史清理验证失败。" }
            }
        }
        finally { Pop-Location }
        foreach ($ref in $otherRefs) {
            $tempRef = 'refs/gitdaily-publish/' + $token + '/' + $ref.Substring(5)
            $temporaryRefs += $tempRef
            $fetch = Invoke-GitBytes -Arguments @('fetch', '--no-tags', $cleanupRoot, "${ref}:${tempRef}")
            if ($fetch.ExitCode -ne 0) { throw $fetch.ErrorText }
            $leases += "--force-with-lease=${ref}:$($remoteRefs[$ref])"
            $updates += "${tempRef}:${ref}"
        }
    }

    $messages = New-Object 'System.Collections.Generic.List[string]'
    $pushExitCode = $null
        # Windows PowerShell 会将重定向的原生命令 stderr 包装为 ErrorRecord。
        # Git 进度也写在 stderr；必须显示完整输出并按退出码判断，不能提前当异常中断。
        $ErrorActionPreference = "Continue"
        & git push --atomic --progress @leases origin @updates 2>&1 | ForEach-Object {
            $line = $_.ToString()
            [void]$messages.Add($line)
            Write-Host $line
        }
        $pushExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
        foreach ($ref in $temporaryRefs) { & git update-ref -d $ref }
        if ($cleanupRoot -and (Test-Path -LiteralPath $cleanupRoot)) {
            $resolved = (Resolve-Path -LiteralPath $cleanupRoot).Path
            $expectedParent = [IO.Path]::GetFullPath((Join-Path $repoRoot 'codex_worktable'))
            if ([IO.Path]::GetDirectoryName($resolved) -ne $expectedParent) { throw '临时仓库清理路径异常。' }
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
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

    Add-SyncChanges


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

    Clear-OutsideSyncHistory
    Write-Host "[4/4] 上传符合路径设置的 main（含清理后的历史；PSD 由 Git LFS 上传）..." -ForegroundColor Cyan

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
