---
name: release-protection-bypass
description: 发版（"发布"、"发版"、"上线"、"合并到release"、"release"）时，本地把 main 合并进 release 后执行 `git push origin release`，若被 release 分支保护阻断（报错含 "protected branch hook declined" / "Changes must be made through a pull request" / "enforce_admins" 等关键字）时使用：临时解除 release 分支保护 → 完成 `git push origin release` → 立即按备份的原配置恢复保护。**严禁解除后忘记恢复**。
---

# Release 分支保护临时绕过

## 触发场景与边界

本仓库（`ViewSuSu/BlackGoldAncientSword`）的发版流程是**本地直推 release**：

1. 所有开发动作只在 `main` 分支完成。**禁止在 `release` 及其它任何下游分支上做开发提交**。
2. `main` push 到远端后，本地执行 `git checkout release && git merge --no-ff main` 把 main 合并进 release。
3. 本地 `git push origin release` 把合并结果推到远端，触发 `dotnet-desktop.yml`。

第 3 步如果远端 `release` 分支保护开着（含 `enforce_admins`），push 会被拒。此时才使用本 skill：临时关保护 → push 成功 → 立即恢复保护。

> 触发前用户必须明确同意"临时关掉 release 保护推一下"。能等就等，能改保护规则放行就改规则，能不关就不关。
>
> 本 skill **只负责 push release 这一动作前后的保护开关**。其它发版步骤（commit / merge / push main）请按 `git-commit` skill 流程走。

## 全流程（严格按序，且必须 finally 恢复）

### 0. 前置

- 仓库：`ViewSuSu/BlackGoldAncientSword`
- 受保护分支：`release`
- GitHub API base：`https://api.github.com`
- 网络出口：由全局 `git-proxy` skill 探测可用通道后填充到 `$proxy`
- 鉴权：本机 `GITHUB_TOKEN` 环境变量（与 `git-commit` skill 调用 GitHub REST API 用的同一份 PAT），权限须含 `repo` + `administration:write`。用 `gh` 时需 `gh auth status` 已登录且 scope 含 `admin:repo`。
- 前置确认：当前 `release` 分支 HEAD 已经是合并好 main 的提交，且本地工作区干净（`git status` 无 untracked / 未提交变更），否则**不进入本 skill**。

### 1. 备份当前分支保护配置（必须做）

在做任何变更前，完整拉取一次当前保护配置落盘备份：

```powershell
$ErrorActionPreference = "Stop"
$repo   = "ViewSuSu/BlackGoldAncientSword"
$branch = "release"
# $proxy 由全局 git-proxy skill 探测后赋值
$backup = Join-Path $env:TEMP "release-protection-backup.json"

curl.exe -x $proxy -sS `
  -H "Authorization: Bearer $env:GITHUB_TOKEN" `
  -H "Accept: application/vnd.github+json" `
  "https://api.github.com/repos/$repo/branches/$branch/protection" `
  -o $backup

# 校验：必须是合法 JSON 且含 enforce_admins 字段
$json = Get-Content $backup -Raw | ConvertFrom-Json
if (-not $json.enforce_admins) { throw "保护配置备份失败，禁止继续。" }
```

> 没有有效备份**绝对**不许进入第 2 步。

### 2. 临时解除保护

二选一，**推荐 gh**：

**方式 A：gh CLI**

```powershell
gh api -X DELETE "repos/$repo/branches/$branch/protection"
```

**方式 B：curl + REST API**

```powershell
curl.exe -x $proxy -sS -X DELETE `
  -H "Authorization: Bearer $env:GITHUB_TOKEN" `
  -H "Accept: application/vnd.github+json" `
  "https://api.github.com/repos/$repo/branches/$branch/protection"
```

成功后 `release` 临时无保护。从这一刻起进入"保护窗口"，**只许做 push release 这一件事**。

### 3. 执行 push release

在当前已切到 `release` 分支、且 HEAD 已含 main 合并的前提下：

```powershell
# 必须走全局 git-proxy skill 的通道选择，不直接硬编码端口
git push origin release
```

push 失败（网络抖动、保护残留缓存等）允许重试；但**严禁** force push / 删除 release / 重写 release 历史。

push 成功后**立即**进入第 4 步。

> 保护窗口内严禁顺手做：在 release 上 commit、在 release 上 cherry-pick、把别的分支 push 到 release、tag 操作等。仅 push 这一个动作。

### 4. 恢复分支保护（最关键，必须执行；即使第 3 步失败也要执行）

`PUT branch protection` 接口要求的 body 与 GET 返回结构不完全一致，需做一次字段转换：

```powershell
$g = Get-Content $backup -Raw | ConvertFrom-Json

$putBody = @{
  required_status_checks = $(if ($g.required_status_checks) {
      @{
        strict   = [bool]$g.required_status_checks.strict
        contexts = @($g.required_status_checks.contexts)
      }
    } else { $null })
  enforce_admins = [bool]$g.enforce_admins.enabled
  required_pull_request_reviews = $(if ($g.required_pull_request_reviews) {
      @{
        dismiss_stale_reviews           = [bool]$g.required_pull_request_reviews.dismiss_stale_reviews
        require_code_owner_reviews      = [bool]$g.required_pull_request_reviews.require_code_owner_reviews
        required_approving_review_count = [int]$g.required_pull_request_reviews.required_approving_review_count
      }
    } else { $null })
  restrictions       = $null
  allow_force_pushes = [bool]$g.allow_force_pushes.enabled
  allow_deletions    = [bool]$g.allow_deletions.enabled
} | ConvertTo-Json -Depth 10 -Compress

curl.exe -x $proxy -sS -X PUT `
  -H "Authorization: Bearer $env:GITHUB_TOKEN" `
  -H "Accept: application/vnd.github+json" `
  "https://api.github.com/repos/$repo/branches/$branch/protection" `
  --data $putBody
```

备份 JSON 里如果出现 `restrictions`（限制可推送 user/team/app）、`required_signatures`、`block_creations`、`required_linear_history` 等额外字段，按实际值逐项映射回 PUT body，**禁止省略**。

### 5. 验证恢复成功

```powershell
curl.exe -x $proxy -sS `
  -H "Authorization: Bearer $env:GITHUB_TOKEN" `
  -H "Accept: application/vnd.github+json" `
  "https://api.github.com/repos/$repo/branches/$branch/protection" | ConvertFrom-Json |
  Select-Object enforce_admins, required_pull_request_reviews, required_status_checks, allow_force_pushes, allow_deletions
```

确认：

- `enforce_admins.enabled == true`
- 其它字段与备份一致

未恢复成功必须**立刻反复重试**，直到状态与备份一致才能结束任务。

### 6. 清理与切回

恢复并验证通过后：

```powershell
Remove-Item $backup -ErrorAction SilentlyContinue
git checkout main
```

切回 `main` 是为了让后续开发动作天然落在 main 上，避免误在 release 上写代码。

## 强制原则（任何情况下都不得违反）

1. **开发仅限 main**：release 永远只接合并、只 push，不接受任何在它身上写的提交。
2. **必须备份、必须恢复**：未备份不许 DELETE；未恢复保护不许结束任务。
3. **try / finally 思维**：第 3 步无论成功还是失败、抛错还是被打断，都必须执行第 4 + 5 步。
4. **窗口最短化**：保护解除到恢复之间只做 `git push origin release` 这一件事。
5. **绝不 force push / 删除 / 重写 release 历史**：解除保护≠允许破坏历史。
6. **告知用户**：解除前、恢复后各给用户一条明确说明（含时间点），便于人工复核。
7. **与 `git-commit`、全局 `git-proxy` skill 联动**：commit / merge / 网络代理仍走那两个 skill 规则，本 skill 只负责保护开关。
