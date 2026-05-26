# -*- coding: utf-8 -*-
import re

filepath = r"E:\desk1\NarakaBladepoint-Stats-Assistant\src\NarakaBladepoint.StatsAssistant.Modules\UI\Stats\ViewModels\StatsPageViewModel.cs"

with open(filepath, "r", encoding="utf-8-sig") as f:
    content = f.read()

# Map of garbled -> correct Chinese strings
mapping = {
    "\u6f9e\u5d97\u5f17\u9384\u6200\u59aa": "\u590d\u5236\u6210\u529f",  # 复制成功
    "\u9365\u68a8\u6d93\u4e36": "\u52a0\u8f7d\u4e2d...",  # 加载中...
    "\u59dd\u8403\u7483\u636e...": "\u6b63\u5728\u641c\u7d22...",  # 正在搜索...
    "\u59dd\u8403\u7e84\u5cf0\u4fdd\u4ff1\u6148...": "\u6b63\u5728\u83b7\u53d6\u4fe1\u606f...",  # 正在获取信息...
    "\u59dd\u8403\u9365\u68a8\u6d93\u6a3a\u5628...": "\u6b63\u5728\u52a0\u8f7d\u6218\u7ee9...",  # 正在加载战绩...
    "\u7490\u7465\u5b58": "\u751f\u5b58",  # 生存
    "\u701f\u5dc0\u5dac": "\u5bf9\u5c40",  # 对局
    "\u9366\u6d93": "\u573a\u6b21",  # 场次
    "\u9360\u72a5\u5546": "\u51a0\u519b",  # 冠军
    "\u9362\u7a2e\u6d6e": "\u5403\u9e21",  # 吃鸡
    "\u9364\u5dc4\u7c72": "\u524d\u4e94",  # 前五
    "\u9366\u6d93\u6f80": "\u573a\u5747",  # 场均
    "\u9366\u6d93\u5877\u6f84": "\u573a\u5747\u4f24\u5bb3",  # 场均伤害
    "\u6f84\u6d93\u614a": "\u4f24\u5bb3",  # 伤害
    "\u6f80\u5929\u82ac\u20ac\u5927\u5d1f\u9365\u934a?": "\u5929\u9009\u5355\u6392",  # 天选单排
    "\u6f80\u5929\u82ac\u20ac\u5927\u5f3b\u9365\u934a?": "\u5929\u9009\u53cc\u6392",  # 天选双排
    "\u6f80\u5929\u82ac\u20ac\u5927\u5909\u7b01\u9365\u934a?": "\u5929\u9009\u4e09\u6392",  # 天选三排
    "\u9365\u8c9b\u5ec4\u9365\u5c64\u5e1b\u5e19": "\u5339\u914d\u5355\u6392",  # 匹配单排
    "\u9365\u8c9b\u5ec4\u9365\u5c4f\u5e1b\u5e19": "\u5339\u914d\u53cc\u6392",  # 匹配双排
    "\u9365\u8c9b\u5ec4\u6f84\u672a\u5e1b\u5e19": "\u5339\u914d\u4e09\u6392",  # 匹配三排
    "\u6f80\u592a\u4eba\u9365\u5c64\u5e1b\u5e19": "\u5929\u4eba\u5355\u6392",  # 天人单排
    "\u6f80\u592a\u4eba\u9365\u5c4f\u5e1b\u5e19": "\u5929\u4eba\u53cc\u6392",  # 天人双排
    "\u6f80\u592a\u4eba\u6f84\u672a\u5e1b\u5e19": "\u5929\u4eba\u4e09\u6392",  # 天人三排
    "\u9472\u7909\u7926": "\u672a\u77e5",  # 未知
}

# The mapping above is complex. Let me take a different approach.
# I'll reconstruct the entire file from the known correct structure.

print("Reading file done, applying fixes...")

# Instead of complex mapping, let me use a more robust approach:
# Read the file and search for specific code patterns, then fix the strings
# based on their context.

# Fix FormatSurvivalTime
old_fst = 'return $\"{minutes}\u9365\u9446\u559fremainSeconds:D2}\u7d89?;'
new_fst = 'return $\"{minutes}\u5206{remainSeconds:D2}\u79d2\";'
content = content.replace('\u9365\u9446\u559f', '\u5206{')
content = content.replace('\u7d89?;', '\u79d2\";')

print("Applied FormatSurvivalTime fix")
