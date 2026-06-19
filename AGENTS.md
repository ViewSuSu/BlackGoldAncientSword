永远用简体中文回答

# PowerShell 7+
遇到 powershell 命令时，使用 PowerShell 7 以上版本（`pwsh`）。

# 全局规则
始终加载并遵循以下全局 skill：
- [karpathy-guidelines](skills/karpathy-guidelines/SKILL.md) — Karpathy 编码行为准则：Think Before Coding, Simplicity First, Surgical Changes, Goal-Driven Execution

# nameof / typeof 规则
**遇到需要拼接已知信息的字符串时，例如类名、属性名、方法名、参数名等，必须优先使用 nameof 而非硬编码字面量。**

正确：
```csharp
var key = $"{nameof(MyClass)}.{nameof(MyClass.MyProperty)}";
// 等价于 "MyClass.MyProperty"，但具备编译时安全性
```

错误：
```csharp
var key = "MyClass.MyProperty"; // 硬编码字符串，重命名后不会报错
```

此规则适用于 C# 代码中所有已知标识符的字符串引用场景：属性变更通知、日志键名、序列化字段名、配置文件键路径、反射字符串参数等。

## Namespace 获取规则

**遇到需要获取 namespace 的字符串时，必须使用 typeof(类).Namespace 获取，禁止硬编码 namespace 字符串字面量。**

正确：
```csharp
var ns = typeof(MyClass).Namespace;
```

错误：
```csharp
var ns = "MyCompany.MyProject.MyNamespace"; // 硬编码字符串，命名空间变更后不会报错
```

--- project-doc ---

# AGENTS.md — NarakaBladepoint-Stats-Assistant

## C# MVVM / 属性变更通知规则

1. **禁止使用 SetProperty**：ViewModel 基类中属性变更通知必须调用 RaisePropertyChanged，不得使用 SetProperty 或其他封装方法。

2. **禁止硬编码属性名字符串**：调用 RaisePropertyChanged 时，不允许传入 "XXX" 这样的字符串字面量。必须使用以下方式之一：
   - 使用 nameof(PropertyName) 显式传入属性名。
   - 若方法签名支持 [CallerMemberName] 特性，则不要主动传入属性名参数，由编译器自动填充。

3. **ViewModel 禁止引用 WPF**：ViewModel 中不允许出现任何 WPF 命名空间或类型的使用（例如 `System.Windows.*`、`System.Windows.Controls.*`、`System.Windows.Media.*`、`Visibility`、`Brush`、`Color`、`DependencyObject`、`UIElement`、`FrameworkElement` 等）。
   - **替代方案**：可见性用 `bool` 表达（通过 Converter 转换为 `Visibility`），颜色/样式用 `string` 或枚举/自定义类型表达，命令使用 `Prism.Commands.DelegateCommand`。
   - **例外**：`System.Windows.Input.ICommand` 接口本身允许使用（命令绑定的标准接口）。

此规则与 `.codex/skills/wpf-mvvm-visibility/SKILL.md` 协同生效，确保 ViewModel 层保持纯净，不引用任何与 `System.Windows` 相关的命名空间。

## Codex 规则

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
