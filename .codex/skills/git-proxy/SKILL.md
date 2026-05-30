---
name: git-proxy
description: 确保 git push 操作通过代理 http://127.0.0.1:9098 执行。当用户要求推送、push、提交并推送、commit+push 或任何涉及 git push 的操作时使用此技能。
---

# Git Proxy

本项目 GitHub 直连不通，所有 `git push` 必须通过本地代理 `http://127.0.0.1:9098`。

## 规则

### Push 前必须设置代理

执行任何 `git push` 命令之前，先设置代理：

```powershell
git config --local http.proxy http://127.0.0.1:9098
```

### Push 后清理代理

Push 成功后清理，避免影响其他 git 操作（如 fetch）：

```powershell
git config --local --unset http.proxy
```

### 示例

完整 push 流程：

```powershell
git config --local http.proxy http://127.0.0.1:9098
git push origin <branch>
git config --local --unset http.proxy
```

## 注意

- 仅设置 `--local`，不影响全局 git 配置
- 代理地址固定为 `http://127.0.0.1:9098`
- 如果 push 超时，先检查代理是否已设置
