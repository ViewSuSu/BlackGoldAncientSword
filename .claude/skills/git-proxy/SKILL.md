---
name: git-proxy
description: 确保 git push 操作通过代理 http://127.0.0.1:9098 执行。当用户要求推送、push、提交并推送、commit+push 或任何涉及 git push 的操作时使用此技能。
---

# Git Proxy

本项目 GitHub 直连不通，所有 `git push` 必须通过本地代理 `127.0.0.1:9098`（端口 9098）。

## 代理来源

GitHub Desktop 在 Windows 上通过读取系统注册表 `HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings` 自动获取系统代理配置。本 skill 的代理地址即从该系统中读取，与 GitHub Desktop 保持一致。

当前环境系统代理设置：
- `ProxyEnable = 1`
- `ProxyServer = 127.0.0.1:9098`
- `ProxyOverride = localhost;127.*;[::1]`

## 规则

### 所有 git push 前必须设置代理

执行任何 `git push` 命令之前，先设置本地 git 代理：

```powershell
git config --local http.proxy http://127.0.0.1:9098
git config --local https.proxy http://127.0.0.1:9098
```

同时设置环境变量（部分 git 操作还通过 HTTP 库直连）：

```powershell
$env:HTTP_PROXY = "http://127.0.0.1:9098"
$env:HTTPS_PROXY = "http://127.0.0.1:9098"
```

### Push 后必须清理代理

Push 成功后清理本地配置，避免影响其他 git 操作（如 fetch / clone 可能不需要代理）：

```powershell
git config --local --unset http.proxy
git config --local --unset https.proxy
Remove-Item Env:HTTP_PROXY, Env:HTTPS_PROXY -ErrorAction SilentlyContinue
```

### 超时处理

如果 `git push` 超时，按顺序排查：
1. `Test-NetConnection 127.0.0.1 -Port 9098` 检查代理是否在运行
2. `git config --local --list` 确认代理已设置
3. 检查 Windows 系统代理设置：`Get-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings" | Select-Object ProxyEnable, ProxyServer`

## 完整示例

```powershell
# 1. 设置代理
git config --local http.proxy http://127.0.0.1:9098
git config --local https.proxy http://127.0.0.1:9098
$env:HTTP_PROXY = "http://127.0.0.1:9098"
$env:HTTPS_PROXY = "http://127.0.0.1:9098"

# 2. 推送
git push origin <branch>

# 3. 清理
git config --local --unset http.proxy
git config --local --unset https.proxy
Remove-Item Env:HTTP_PROXY, Env:HTTPS_PROXY -ErrorAction SilentlyContinue
```

## 注意

- 仅设置 `--local`，不影响全局 git 配置
- 代理地址与 GitHub Desktop 一致，均从 Windows 系统代理读取
- 必须同时设置 `http.proxy` + `https.proxy` + 环境变量，三者缺一不可
- 不要在 git push 的同一行命令中设置和清理，要分步执行
- 如果 git push 不带 `origin <branch>` 参数，先确认当前分支再推送
