---
name: git-commit
description: 在当前分支分析 git diff 差异，用中文撰写详细的 commit message，然后 git commit 并 push。当用户只说"推送"、"提交"、"commit"、"push"、"提交并推送"时仅执行 commit+push；当用户说"发布"、"发版"、"上线"、"合并到release"、"release"时，则执行完整的发版流程：commit+push 当前分支 → 用 gh CLI 创建 PR 到 release 并合并（release 受分支保护禁直推）。
---

# Git Commit 工作流

对当前分支执行完整的 commit + push 流程，commit message 使用中文且必须详细。

## 流程

### 1. 分析差异

运行 `git diff` 和 `git diff --cached`（如有暂存变更），逐文件分析：

- 每个文件改了什么
- 为什么这么改（从代码逻辑推断意图）
- 变更之间的关联性（是否属于同一批修改）
- 对于所有提交场景（普通 commit 和发版 release），分析过程和要求完全相同
- 禁止"修复问题"、"优化代码"等笼统描述

### 2. 撰写 Commit Message

- **语言**：中文
- **格式**：首行为简短摘要，空一行后分段详细说明
- **内容要求**：
  - 清楚说明每个文件的改动内容和原因
  - 涉及多个文件时按功能分组描述
  - 技术细节（如 API 变更、接口修改）必须注明
  - 不写"修复了一些问题"、"优化代码"等笼统描述

### 3. 确认后提交

展示 commit message 给用户确认，确认后执行：

```powershell
git add -A
git commit -m "<message>"
```

### 4. Push（必须走代理）

Push 前必须设置代理，push 后清理。详见 `git-proxy` 技能：

```powershell
# 设置代理（http.proxy + https.proxy + 环境变量，与 GitHub Desktop 共用系统代理配置）
git config --local http.proxy http://127.0.0.1:9098
git config --local https.proxy http://127.0.0.1:9098
$env:HTTP_PROXY = "http://127.0.0.1:9098"
$env:HTTPS_PROXY = "http://127.0.0.1:9098"

# 推送
git push origin <current-branch>

# 清理代理
git config --local --unset http.proxy
git config --local --unset https.proxy
Remove-Item Env:HTTP_PROXY, Env:HTTPS_PROXY -ErrorAction SilentlyContinue
```

## 原则

- 必须先完整分析 diff 再写 message，不能跳过分析直接提交
- 不要主动切换分支，在当前分支操作
- 不要 `git add` 特定文件后分批提交，一次性 `git add -A` 全部提交
- Push 前必须检查 `git-proxy` 技能，确保代理已配置

## 分支推送策略

- `main`：允许本地直推
- `release`：**受 GitHub 分支保护**，禁直推 / 禁 force push / 禁删除；必须经 Pull Request 合并
- 其它功能/特性分支：允许直推

## 完整发版流程（当用户说"发布"、"发版"、"上线"、"合并到release"、"release"时）

执行上述 1-4 步（分析差异 → 撰写 commit message → commit → push 当前分支）后，继续以下步骤：

### 5. 创建 PR 并合并到 release

`release` 受分支保护，本地 `git push origin release` 会被远端拒绝，必须用 `gh` CLI 走 PR 流程：

```powershell
# 确认 gh 已登录（必要时 gh auth login -h github.com -p https -w）
gh auth status

# 设置代理（gh 走 HTTPS_PROXY）
$env:HTTPS_PROXY = "http://127.0.0.1:9098"
$env:HTTP_PROXY = "http://127.0.0.1:9098"

# 创建 PR：base=release, head=当前源分支；--fill 用 commit 信息自动填 title/body
gh pr create --base release --head <source-branch> --fill

# 合并 PR：--merge 保留 merge commit，与原 git merge 行为一致
gh pr merge <source-branch> --merge

# 清理代理
Remove-Item Env:HTTP_PROXY, Env:HTTPS_PROXY -ErrorAction SilentlyContinue
```

### 6. 确认结果

- `gh pr merge` 成功，PR 状态变为 `MERGED`
- 无需切到 `release` 本地分支（本地 release 不再需要同步；下次发版前 `git fetch` 即可）
- 仍停留在源分支

### 发版原则

- `release` 受 GitHub 分支保护，**禁本地直推**，必须经 PR 合并
- 合并模式使用 `--merge`（非 fast-forward，保留 merge commit）
- 若 `gh` 未登录：先 `gh auth login -h github.com -p https -w`
- 若 PR 合并因冲突失败，停止并报告，不做自动解决
- 若 gh CLI 不在 PATH，用绝对路径 `"C:/Program Files/GitHub CLI/gh.exe"`
