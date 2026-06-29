# CLAUDE.md — NarakaBladepoint-Stats-Assistant

> 本文件为 Claude Code 在本项目中的工作准则。与 [AGENTS.md](AGENTS.md) 内容对齐，供 Claude Code（`.claude/` 体系）使用。

## C# MVVM / 属性变更通知规则

1. **禁止使用 SetProperty**：ViewModel 基类中属性变更通知必须调用 RaisePropertyChanged，不得使用 SetProperty 或其他封装方法。

2. **禁止硬编码属性名字符串**：调用 RaisePropertyChanged 时，不允许传入 "XXX" 这样的字符串字面量。必须使用以下方式之一：
   - 使用 nameof(PropertyName) 显式传入属性名。
   - 若方法签名支持 [CallerMemberName] 特性，则不要主动传入属性名参数，由编译器自动填充。

3. **ViewModel 禁止引用 WPF**：ViewModel 中不允许出现任何 WPF 命名空间或类型的使用（例如 `System.Windows.*`、`System.Windows.Controls.*`、`System.Windows.Media.*`、`Visibility`、`Brush`、`Color`、`DependencyObject`、`UIElement`、`FrameworkElement` 等）。
   - **替代方案**：可见性用 `bool` 表达（通过 Converter 转换为 `Visibility`），颜色/样式用 `string` 或枚举/自定义类型表达，命令使用 `Prism.Commands.DelegateCommand`。
   - **例外**：`System.Windows.Input.ICommand` 接口本身允许使用（命令绑定的标准接口）。

## Claude Code 工作规则

1. **禁止 git 回滚**：不允许使用 git revert、git reset、git checkout 等回滚代码的操作，除非用户明确要求。

2. **文件编码统一 UTF-8**：生成或修改的任何文件都必须使用 UTF-8 编码（无 BOM）。

3. **Git Commit 规范**：
   - commit message 使用**中文**撰写。
   - 内容必须**详细**，仔细分析 git diff 的文件差异后再编写。
   - 清楚说明改了什么、为什么改，不写笼统的描述。

4. **防止中文乱码**：修改或生成文件时，确保文件中的中文字符正常显示，不得出现乱码。写入文件时必须使用 UTF-8 编码（无 BOM）。

5. **每次修改后必须 Build**：对代码做任何修改（新增、编辑、删除文件）后，必须运行以下命令确保项目编译通过，不得跳过：
   ```powershell
   dotnet build src/BlackGoldAncientSword.slnx
   ```
   若编译失败（exit code ≠ 0 或有 error），必须修复所有编译错误后再次 build，直到 0 error 为止才可认为修改完成。

## 项目级 Skills

本项目在 `.claude/skills/` 下提供以下 skill，Claude Code 在对应场景下应自动使用：

| Skill | 适用场景 |
|---|---|
| [naraka-stats-assistant](.claude/skills/naraka-stats-assistant/SKILL.md) | 项目架构、模块组织、编码约定、发版流程 |
| [git-commit](.claude/skills/git-commit/SKILL.md) | 分析 diff、中文详细 commit、push、发版到 release |
| [rebuild-guard](.claude/skills/rebuild-guard/SKILL.md) | 对话收尾时强制 kill + dotnet build，确保 0 error |
| [gitee-token](.claude/skills/gitee-token/SKILL.md) | Gitee API 调用令牌（清理 release/tag、上传附件等） |

> 同等内容在 `.codex/skills/` 下也存在一份，供 codex 使用；两者保持同步。
