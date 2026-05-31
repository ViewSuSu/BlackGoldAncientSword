---
name: git-commit
description: 在当前分支分析 git diff 差异，用中文撰写详细的 commit message，然后 git commit 并 push。当用户只说"推送"、"提交"、"commit"、"push"、"提交并推送"时仅执行 commit+push；当用户说"发布"、"发版"、"上线"、"合并到release"、"release"时，则执行完整的发版流程：commit+push 当前分支 → 合并到 release → push release → 切回原分支。
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
git config --local http.proxy http://127.0.0.1:9098
git push origin <current-branch>
git config --local --unset http.proxy
```

## 原则

- 必须先完整分析 diff 再写 message，不能跳过分析直接提交
- 不要主动切换分支，在当前分支操作
- 不要 `git add` 特定文件后分批提交，一次性 `git add -A` 全部提交
- Push 前必须检查 `git-proxy` 技能，确保代理已配置

## 完整发版流程（当用户说"发布"、"发版"、"上线"、"合并到release"、"release"时）

执行上述 1-4 步（分析差异 → 撰写 commit message → commit → push 当前分支）后，继续以下步骤：

### 5. 合并到 release 分支并推送

```powershell
git checkout release
git merge <source-branch>
git config --local http.proxy http://127.0.0.1:9098
git push origin release
git config --local --unset http.proxy
git checkout <source-branch>
```

### 6. 确认结果

- 确认 `git merge` 成功，无冲突
- 确认 `git push origin release` 成功
- 确认已切回原分支

### 发版原则

- 合并使用 `git merge`（非 fast-forward 时自动生成 merge commit）
- 合并完成后必须切回原分支
- 如遇冲突，停止并报告，不做自动解决