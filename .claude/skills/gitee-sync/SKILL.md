---
name: gitee-sync
description: 从 GitHub 同步指定版本的 release 到 Gitee（SususuChang/BlackGoldAncientSword）。当用户说"同步到 gitee"、"gitee 同步"、"sync to gitee"、"上传到 gitee" 并附带版本号时触发。
---

# Gitee 同步 Skill

将 GitHub 上指定版本的 release 附件下载后重新发布到 Gitee。

## 触发条件

用户说以下任意短语 **且附带版本号**（如 v1.0.0.1）时触发：

- "同步到 gitee"
- "gitee 同步"
- "sync to gitee"
- "上传到 gitee"

## 前置检查

执行任何步骤前，先确认以下条件：

1. **版本号**：用户已提供（如 `v1.0.0.1`），否则先询问。
2. **gh CLI**：`gh auth status` 可正常通过（用于查询 GitHub release）。
3. **CDP 就绪**：运行以下命令确认浏览器自动化依赖已就绪：
   ```powershell
   node "C:/Users/16147/.claude/skills/web-access/scripts/check-deps.mjs"
   ```
4. **Gitee Token**：从 `gitee-token` skill 读取，无需向用户索取。
5. **Python 依赖**：`requests`、`requests-toolbelt`、`tqdm` 已安装（上传脚本需要）。

---

## 完整步骤

### Step 1：查询 GitHub Release，筛选附件

```powershell
$version = "v1.0.0.1"   # 替换为实际版本号

gh api repos/ViewSuSu/BlackGoldAncientSword/releases/tags/$version `
  --jq '.assets[] | select(.size < 104857600) | "\(.name) \(.size) \(.browser_download_url)"'
```

**筛选规则**：只保留 **小于 100 MB** 的文件，通常包括：

| 类型 | 文件名示例 |
|---|---|
| 分卷安装包 | `Setup-Split.exe`、`Setup-Split-2.bin`、… |
| 分卷压缩包 | `BlackGoldAncientSword.zip.001`、`.002`、… |

超过 100 MB 的文件（Gitee 单文件限制）自动忽略。

记录所有筛选出文件的 `browser_download_url`，供下一步使用。

---

### Step 2：浏览器触发下载

> 不使用 curl/gh CLI 直连 GitHub 下载——GFW 限速严重，速度极慢。改用浏览器（走系统代理）触发下载。

对每个筛选出的 URL，执行：

```powershell
curl -s -X POST --data-raw "<browser_download_url>" http://localhost:3456/new
```

逐个打开，每次调用都会在 Edge 中打开一个新 Tab 并触发浏览器自动下载。所有 Tab 打开完毕后，可关闭这些空 Tab。

**等待用户确认**：提示用户所有文件将下载到默认下载目录（通常为 `C:\Users\16147\Downloads`），等用户确认下载完成后再进入下一步。

---

### Step 3：整理文件到临时目录

浏览器重复下载同名文件时会产生 `文件名 (1).ext` 副本，上传前需统一整理。

```powershell
$version = "v1.0.0.1"
$downloads = "C:\Users\16147\Downloads"
$tmpDir = "$env:TEMP\gitee-sync-$version"
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null

# 将文件复制到临时目录，去掉 (1) 后缀，使用正确文件名
# 示例（根据实际筛选出的文件名逐一处理）：
Copy-Item "$downloads\Setup-Split.exe" "$tmpDir\Setup-Split.exe" -Force
# ... 其余文件类推

# 可选：删除 Downloads 中的 (1) 副本
Remove-Item "$downloads\*`(1`)*" -ErrorAction SilentlyContinue
```

确认 `$tmpDir` 中的文件名与 GitHub release 附件名**完全一致**。

---

### Step 4：清理 Gitee 旧 Release 和 Tag

**Token** 从 `gitee-token` skill 读取：`ed885a61d4f2640787007fb21a365d74`（变量化使用，勿硬编码到脚本输出中）。

```powershell
$TOKEN = "ed885a61d4f2640787007fb21a365d74"
$REPO  = "SususuChang/BlackGoldAncientSword"
$BASE  = "https://gitee.com/api/v5/repos/$REPO"
$TAG   = "v1.0.0.1"   # 替换为实际版本号

# 4-1. 删旧 Tag（用 git push --delete 比 REST API 更可靠）
git push gitee --delete $TAG 2>$null
# 兜底：REST API 盲删，忽略响应
curl.exe -s -X DELETE "$BASE/tags/$TAG`?access_token=$TOKEN" | Out-Null

# 4-2. 查旧 Release 并删除
$rel = curl.exe -s "$BASE/releases/tags/$TAG`?access_token=$TOKEN" | ConvertFrom-Json 2>$null
if ($rel.id) {
    curl.exe -s -X DELETE "$BASE/releases/$($rel.id)`?access_token=$TOKEN" | Out-Null
    Write-Host "已删除旧 release id=$($rel.id)"
}
```

> **注意**：`git push gitee --delete` 要求本地已配置 `gitee` remote 指向 `https://gitee.com/SususuChang/BlackGoldAncientSword.git`。如未配置：`git remote add gitee https://gitee.com/SususuChang/BlackGoldAncientSword.git`

---

### Step 5：创建新 Gitee Release

**必须用 form data（`-F`），不要用 JSON body**；`body` 字段只写 ASCII（中文会报 `invalid byte sequence`）。

```powershell
$TAG = "v1.0.0.1"   # 替换为实际版本号

$resp = curl.exe -s -X POST "$BASE/releases" `
  -F "access_token=$TOKEN" `
  -F "tag_name=$TAG" `
  -F "name=Release $TAG" `
  -F "body=$TAG" `
  -F "target_commitish=main" | ConvertFrom-Json

$RELEASE_ID = $resp.id
Write-Host "创建 release 成功 id=$RELEASE_ID"
```

**若创建失败**：重试最多 3 次，间隔 5s：

```powershell
$maxRetry = 3
for ($i = 1; $i -le $maxRetry; $i++) {
    $resp = curl.exe -s -X POST "$BASE/releases" `
      -F "access_token=$TOKEN" -F "tag_name=$TAG" `
      -F "name=Release $TAG" -F "body=$TAG" `
      -F "target_commitish=main" | ConvertFrom-Json
    if ($resp.id) { $RELEASE_ID = $resp.id; break }
    Write-Warning "创建 release 失败（$i/$maxRetry），5s 后重试..."
    Start-Sleep 5
}
if (-not $RELEASE_ID) { throw "创建 Gitee release 失败，已重试 $maxRetry 次" }
```

---

### Step 6：上传附件

设置必要环境变量后调用上传脚本：

```powershell
$env:GITEE_REPO       = "SususuChang/BlackGoldAncientSword"
$env:GITEE_TOKEN      = $TOKEN
$env:GITEE_RELEASE_ID = $RELEASE_ID
$env:GITEE_ASSETS_DIR = $tmpDir

python scripts/gitee-upload-assets.py
```

脚本行为：
- 并行 3 路上传，每文件最多重试 3 次，指数退避
- 超 100 MB 的文件自动跳过（已在 Step 1 筛过，此处为二次防护）
- 任一文件失败 → `exit 1`，需人工排查后重新执行本步骤

---

### Step 7：清理临时文件

上传成功后清理：

```powershell
# 删除临时目录
Remove-Item -Recurse -Force $tmpDir

# 删除 Downloads 中已下载的原始文件（按版本号命名的文件）
# 根据实际文件名逐一清理，例如：
Remove-Item "$downloads\Setup-Split.exe" -ErrorAction SilentlyContinue
Remove-Item "$downloads\BlackGoldAncientSword.zip.*" -ErrorAction SilentlyContinue
```

---

## 常见问题

| 现象 | 原因 | 处理 |
|---|---|---|
| `git push --delete` 报 `remote: repository not found` | 未配置 gitee remote | `git remote add gitee https://gitee.com/SususuChang/BlackGoldAncientSword.git` |
| 创建 release 返回 `invalid byte sequence` | body 字段含中文 | body 只写版本号（如 `v1.0.0.1`），不写中文 |
| 上传超时 | 网络波动 | 脚本自动重试，超过 3 次后手动重跑 Step 6 |
| 浏览器下载出现 `(1)` 副本 | 重复点击 | Step 3 整理时统一修正文件名 |
| CDP 端口 3456 无响应 | web-access 服务未启动 | 先运行 `check-deps.mjs` 确认服务状态 |

## 注意事项

- Gitee Token 不要出现在 commit message、日志输出或截图中。
- 整个流程中 GitHub 侧的 release **不做任何修改**，只读查询。
- 如需重新同步同一版本，从 Step 4 重新执行即可（清理旧 release/tag → 重建）。
