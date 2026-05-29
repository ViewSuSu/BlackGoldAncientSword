# AGENTS.md — NarakaBladepoint-Stats-Assistant

## Codex 规则

1. **禁止 git 回滚**：不允许使用 `git revert`、`git reset`、`git checkout` 等回滚代码的操作，除非用户明确要求。

2. **文件编码统一 UTF-8**：生成或修改的任何文件都必须使用 UTF-8 编码（无 BOM）。

3. **Git Commit 规范**：
   - commit message 使用**中文**撰写。
   - 内容必须**详细**，仔细分析 `git diff` 的文件差异后再编写。
   - 清楚说明改了什么、为什么改，不写笼统的描述。