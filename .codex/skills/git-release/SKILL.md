---
name: git-release
description: 在当前分支分析 git diff、中文详细 commit、push，然后合并到 release 分支并 push。当用户说"发版"、"发布"、"release"、"合并到release"、"上线"时使用此技能。
---

# Git Release 工作流

完整的发版流程：分析差异 → commit → push → 合并到 release → push release → 切回原分支。

## 关键词触发

- **中文**：发版、发布、上线、合并到 release
- **英文**：release、merge to release、ship

## 流程

### 1. 分析差异

运行 `git diff` 和 `git diff --cached`，逐文件分析改动内容和意图。

### 2. 撰写中文 Commit Message

- 首行简短摘要，空一行后分段详述
- 每个文件的改动 + 原因，按功能分组
- 禁止"修复问题"、"优化代码"等笼统描述

### 3. Commit + Push 当前分支

```powershell
git add -A
git commit -m "<message>"
git push origin <current-branch>
```

### 4. 合并到 release

```powershell
git checkout release
git merge <source-branch>
git push origin release
git checkout <source-branch>
```

## 原则

- 合并使用 `git merge`（非 fast-forward 时自动生成 merge commit）
- 合并完成后必须切回原分支
- 如遇冲突，停止并报告，不做自动解决
