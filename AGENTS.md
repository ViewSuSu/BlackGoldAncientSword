# AGENTS.md — NarakaBladepoint-Stats-Assistant

## C# MVVM / 属性变更通知规则

1. **禁止使用 SetProperty**：ViewModel 基类中属性变更通知必须调用 RaisePropertyChanged，不得使用 SetProperty 或其他封装方法。

2. **禁止硬编码属性名字符串**：调用 RaisePropertyChanged 时，不允许传入 "XXX" 这样的字符串字面量。必须使用以下方式之一：
   - 使用 nameof(PropertyName) 显式传入属性名。
   - 若方法签名支持 [CallerMemberName] 特性，则不要主动传入属性名参数，由编译器自动填充。

## Codex 规则

1. **禁止 git 回滚**：不允许使用 git revert、git reset、git checkout 等回滚代码的操作，除非用户明确要求。

2. **文件编码统一 UTF-8**：生成或修改的任何文件都必须使用 UTF-8 编码（无 BOM）。

3. **Git Commit 规范**：
   - commit message 使用**中文**撰写。
   - 内容必须**详细**，仔细分析 git diff 的文件差异后再编写。
   - 清楚说明改了什么、为什么改，不写笼统的描述。

4. **防止中文乱码**：修改或生成文件时，确保文件中的中文字符正常显示，不得出现乱码。写入文件时必须使用 UTF-8 编码（无 BOM）。


