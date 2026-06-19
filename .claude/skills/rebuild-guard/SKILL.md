---
name: rebuild-guard
description: 每次对话结束时自动清理运行中的进程并强制 dotnet build，确保项目编译通过（0 error）。当在此项目中完成代码修改后必须使用此 skill：杀死所有运行中的 BlackGoldAncientSword.App 进程，执行完整的 dotnet build，若编译失败则修复直到 0 error。与 naraka-stats-assistant skill 协同使用。
---

# Rebuild Guard

每次对话结束时，在提交 final 回答**之前**，执行以下重建流程。

## 流程

### 1. 强制杀死运行中的应用进程

先检查并强制结束正在运行的 `BlackGoldAncientSword.App` 进程，释放文件锁：

```powershell
Get-Process -Name "BlackGoldAncientSword.App" -ErrorAction SilentlyContinue | Stop-Process -Force
```

如果还有其它衍生进程也需要一并清理，可以扩展此命令。

### 2. 执行 dotnet build

```powershell
dotnet build src/BlackGoldAncientSword.slnx
```

### 3. 验证编译结果

- 检查命令 exit code 是否为 `0`
- 检查输出中是否有 `Build succeeded` 字样
- 检查输出中 `error` 行数为零

**如果编译失败（exit code ≠ 0 或有 error 出现）：**

1. 分析错误信息，定位到具体文件和代码
2. 修复所有编译错误
3. 再次执行 `dotnet build src/BlackGoldAncientSword.slnx`
4. 重复直到 exit code 为 `0` 且无 error

**只有编译通过（0 error）后，才可提交 final 回答。**

## 与 CLAUDE.md / AGENTS.md 的关系

此 skill 是 CLAUDE.md（及对齐的 AGENTS.md）中"每次修改后必须 Build"规则的自动化增强版：

- CLAUDE.md / AGENTS.md 要求每次修改后 build（手动）
- 此 skill 额外在对话结束前自动执行 kill + rebuild 流程，兜底检查

## 注意

- 此流程必须在向用户提交 final 回答之前执行，确保用户收到的都是编译通过的代码
- 如果项目中没有任何修改（纯分析、咨询场景），可以跳过此流程
- 进程清理使用 `-ErrorAction SilentlyContinue`，如果进程不存在不会报错
