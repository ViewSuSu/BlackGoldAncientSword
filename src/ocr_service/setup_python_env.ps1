<#
.SYNOPSIS
    在项目中构建自包含的 Python 环境（嵌入版 + PaddleOCR）。
    运行一次即可，后续构建会自动将 python/ 目录复制到输出。

.DESCRIPTION
    本脚本会按顺序执行以下操作：
    1. 下载 Python 嵌入版压缩包（如未缓存）
    2. 解压到 src/ocr_service/python/
    3. 在嵌入版 Python 中启用 pip 支持
    4. 安装 PaddleOCR 及全部依赖（paddlepaddle-cpu、opencv 等）
    5. 创建 .pydeps_ok 标记文件（防止重复构建）

    重复执行会自动跳过（检测到 .pydeps_ok 标记文件后直接退出）。
    如需重新构建，删除 src/ocr_service/python/.pydeps_ok 后再运行。

.NOTES
    所需磁盘空间: ~350 MB
      Python 3.12.8 嵌入版:  ~85 MB
      PaddlePaddle-CPU:     ~150 MB
      PaddleOCR + 模型:     ~100 MB

    构建耗时: 5 ~ 15 分钟（取决于网络速度和 CPU）
#>

param(
    # Python 版本号（需与 python.org ftp 上的版本一致）
    [string]$PythonVersion = "3.12.8",

    # CPU 架构（amd64 或 win32）
    [string]$Arch = "amd64",

    # 项目根目录（默认脚本所在目录即 src/ocr_service/）
    [string]$ProjectRoot = $PSScriptRoot
)

$ErrorActionPreference = "Stop"
Set-Location $ProjectRoot

# ── 路径定义 ──
$pythonDir = Join-Path $ProjectRoot "python"
$pythonExe = Join-Path $pythonDir "python.exe"
$markerFile = Join-Path $pythonDir ".pydeps_ok"          # 标记文件：存在表示构建已完成
$pythonZipName = "python-$PythonVersion-embed-$Arch.zip"
$pythonZipPath = Join-Path $ProjectRoot $pythonZipName
$pythonUrl = "https://www.python.org/ftp/python/$PythonVersion/$pythonZipName"
$getPipUrl = "https://bootstrap.pypa.io/get-pip.py"
$getPipPath = Join-Path $pythonDir "get-pip.py"
$pthFile = Join-Path $pythonDir "python312._pth"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  PaddleOCR Python 环境构建" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ═══════════════════════════════════════════════
#  检查是否已构建
# ═══════════════════════════════════════════════
if (Test-Path $markerFile) {
    Write-Host "[OK] Python 环境已构建完成，路径: $pythonDir" -ForegroundColor Green
    Write-Host "     如需重建，请删除 $markerFile 后重新运行。" -ForegroundColor Yellow
    exit 0
}

# ═══════════════════════════════════════════════
#  第 1 步：下载 Python 嵌入版
# ═══════════════════════════════════════════════
if (-not (Test-Path $pythonZipPath)) {
    Write-Host "[1/5] 下载 Python $PythonVersion 嵌入版 ($Arch) ..." -ForegroundColor Yellow
    Write-Host "      $pythonUrl"
    try {
        Invoke-WebRequest -Uri $pythonUrl -OutFile $pythonZipPath -UseBasicParsing
    } catch {
        Write-Host "[ERROR] 下载失败: $_" -ForegroundColor Red
        Write-Host "       请检查网络连接，或确认 python.org 上存在版本 $PythonVersion。" -ForegroundColor Red
        exit 1
    }
    Write-Host "      已下载: $pythonZipPath" -ForegroundColor Green
} else {
    Write-Host "[1/5] Python 嵌入版压缩包已缓存: $pythonZipPath" -ForegroundColor Gray
}

# ═══════════════════════════════════════════════
#  第 2 步：解压到 python/ 目录
# ═══════════════════════════════════════════════
Write-Host "[2/5] 解压 Python 到: $pythonDir" -ForegroundColor Yellow
if (Test-Path $pythonDir) {
    Remove-Item -Recurse -Force $pythonDir
}
Expand-Archive -Path $pythonZipPath -DestinationPath $pythonDir -Force
Write-Host "      解压完成。" -ForegroundColor Green

# ═══════════════════════════════════════════════
#  第 3 步：启用 pip（修改 ._pth 文件）
# ═══════════════════════════════════════════════
Write-Host "[3/5] 在嵌入版 Python 中启用 pip 支持..." -ForegroundColor Yellow
# 嵌入版默认禁用 pip；需要取消注释 "import site" 行
$pthContent = Get-Content $pthFile -Raw
$pthContent = $pthContent -replace '#import site', 'import site'
$pthContent = $pthContent -replace '# Lib\\site-packages', 'Lib\site-packages'
Set-Content -Path $pthFile -Value $pthContent -NoNewline
Write-Host "      已修改: $pthFile" -ForegroundColor Green

# ═══════════════════════════════════════════════
#  第 4 步：安装 pip
# ═══════════════════════════════════════════════
Write-Host "[4/5] 下载并安装 pip..." -ForegroundColor Yellow
try {
    Invoke-WebRequest -Uri $getPipUrl -OutFile $getPipPath -UseBasicParsing
} catch {
    Write-Host "[ERROR] 下载 get-pip.py 失败: $_" -ForegroundColor Red
    exit 1
}

$pipResult = & $pythonExe $getPipPath 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] pip 安装失败:" -ForegroundColor Red
    Write-Host $pipResult
    exit 1
}
Write-Host "      pip 安装成功。" -ForegroundColor Green

# ═══════════════════════════════════════════════
#  第 5 步：安装 PaddleOCR 及全部依赖
# ═══════════════════════════════════════════════
Write-Host "[5/5] 安装 PaddleOCR 及依赖（预计 5~15 分钟）..." -ForegroundColor Yellow
Write-Host "      包含: paddlepaddle-cpu, paddleocr, opencv-python-headless"

# 先升级 pip 自身（静默）
& $pythonExe -m pip install --upgrade pip --quiet 2>&1 | Out-Null

# 按顺序安装，避免依赖冲突
$packages = @(
    "paddlepaddle==3.1.1",              # PaddlePaddle CPU 版（3.x 系列）
    "paddleocr>=2.9.0",                 # PaddleOCR 文字识别库
    "opencv-python-headless>=4.8.0"     # OpenCV 无 GUI 版（用于图像预处理）
)

foreach ($pkg in $packages) {
    Write-Host "      正在安装 $pkg ..."
    $result = & $pythonExe -m pip install $pkg --quiet 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] 安装失败: $pkg" -ForegroundColor Red
        Write-Host $result
        exit 1
    }
}

Write-Host "      全部依赖安装完成。" -ForegroundColor Green

# ═══════════════════════════════════════════════
#  标记构建完成
# ═══════════════════════════════════════════════
"" | Set-Content $markerFile

# 计算总大小
$totalSize = (Get-ChildItem -Recurse $pythonDir | Measure-Object -Property Length -Sum).Sum / 1MB

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  构建完成！" -ForegroundColor Green
Write-Host "  Python 路径 : $pythonExe" -ForegroundColor White
Write-Host "  pip  路径    : $(Join-Path $pythonDir 'Scripts\pip.exe')" -ForegroundColor White
Write-Host "  占用空间     : $([math]::Round($totalSize, 1)) MB" -ForegroundColor White
Write-Host "========================================" -ForegroundColor Cyan
