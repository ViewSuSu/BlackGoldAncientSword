---
name: git-commit
description: 在当前分支分析 git diff 差异，用中文撰写详细的 commit message，然后 git commit 并 push。当用户只说"推送"、"提交"、"commit"、"push"、"提交并推送"时仅执行 commit+push；当用户说"发布"、"发版"、"上线"、"合并到release"、"release"时，执行完整发版流程：commit + push 当前分支 →（在 GitHub 上）创建 main → release 的 PR 并合并（release 分支保护已重启，禁止本地直推，只能走 PR）。
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

### 4. Push

网络出口、代理选路、自带 git fallback 等**统一由全局 `git-proxy` skill** 处理，本 skill 不再保留任何端口/代理配置。

```powershell
git push origin <current-branch>
```

如果遇到 `Failed to connect` / `Connection was reset` / `Could not resolve host` 等网络错误，按全局 `git-proxy` skill 的步骤：探测可用通道 → `git -c http.proxy=$proxy push` 或回落 GitHub Desktop 自带 git.exe。

## 原则

- 必须先完整分析 diff 再写 message，不能跳过分析直接提交
- 不要主动切换分支，在当前分支操作
- 不要 `git add` 特定文件后分批提交，一次性 `git add -A` 全部提交
- Push 走全局 `git-proxy` skill，**不要**在本仓库 / 项目 skill 里硬编码代理端口

## 分支推送策略

- `main`：允许本地直推
- `release`：**已开启分支保护（含 `enforce_admins`）**，禁止本地直推 / force push / 删除，所有变更必须通过 PR 合并进来
- 其它功能/特性分支：允许直推

## 完整发版流程（当用户说"发布"、"发版"、"上线"、"合并到release"、"release"时）

执行上述 1-4 步（分析差异 → 撰写 commit message → commit → push 当前源分支，通常是 `main`）后，继续以下步骤：

### 5. 创建 main → release 的 PR

`release` 分支保护已重启，**不再允许本地直推**。发版必须通过 GitHub 上的 PR 完成合并：

- 推荐做法：在浏览器打开
  `https://github.com/ViewSuSu/BlackGoldAncientSword/compare/release...main?expand=1`
  填写 PR 标题（例如 `Release vX.Y.Z.W`），提交 PR，再点击 **Merge pull request**（保留 merge commit）触发 `dotnet-desktop.yml`。
- 如果本机已安装 GitHub CLI，可改用：

  ```powershell
  gh pr create --base release --head main --title "Release" --body "发版合并"
  gh pr merge --merge --auto
  ```

- 如果只有 GitHub Personal Access Token（无 `gh`），可用 GitHub REST API。**网络出口走全局 `git-proxy` skill**：先按其探测流程拿到可用 `$proxy`，再注入到 curl：

  ```powershell
  # 创建 PR
  curl.exe -x $proxy -X POST `
    -H "Authorization: Bearer $env:GITHUB_TOKEN" `
    -H "Accept: application/vnd.github+json" `
    https://api.github.com/repos/ViewSuSu/BlackGoldAncientSword/pulls `
    -d '{"title":"Release","head":"main","base":"release","body":"发版合并"}'

  # 合并 PR（用上一步返回的 number 替换 <PR_NUMBER>）
  curl.exe -x $proxy -X PUT `
    -H "Authorization: Bearer $env:GITHUB_TOKEN" `
    -H "Accept: application/vnd.github+json" `
    https://api.github.com/repos/ViewSuSu/BlackGoldAncientSword/pulls/<PR_NUMBER>/merge `
    -d '{"merge_method":"merge"}'
  ```

### 6. 确认结果

- `main` 已 push 到远端
- `main` → `release` 的 PR 已创建并合并，`dotnet-desktop.yml` 已被触发

### 发版原则

- **`release` 分支保护已开启，不允许本地直推**：禁止 `git push origin release`、禁止 force push、禁止删除，对管理员同样生效（`enforce_admins`）
- 合并模式使用 `--merge`（保留 merge commit），不要用 squash/rebase
- 若 PR 出现冲突，停止并报告，由人工解决后再触发合并
- 不在本地执行任何修改 `release` 分支历史的命令
