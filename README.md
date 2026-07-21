# HARAN UI 控件探测（HaranUiProbe）

小工具：用 **UI Automation (FlaUI UIA3)** 扫描 HARAN / Viscom「Semi-automatic Repair Station Display」窗口树，检查能否读到：

- `Waiting for Input`（待判定 / 可按键）
- `Currently no Repair Data`（空闲）

供评估「工位自动放行」是否可用**控件文字**做就绪门闩（相对截图 OCR 更稳）。

## 运行（Win10 x64）

1. 解压发布包  
2. 先打开 HARAN 复判界面  
3. 双击 `HaranUiProbe.exe`  
4. 点 **扫描一次**（可选：自动轮询）  
5. 看顶部摘要 + 下方日志  
6. **保存结果到文件**，把 `probe-logs\*.txt` 发回分析  

若 HARAN 以**管理员**运行，本工具也请**管理员**打开。

## 结果怎么看

| 摘要 | 含义 |
|------|------|
| 检测到 Waiting for Input | 控件方案大概率可用 |
| 检测到 no Repair Data | 空闲态可读；请再在待判时扫一次 |
| 找到窗但无目标字 | 状态栏可能自绘 → 需截图模板/OCR |
| 未找到窗口 | 调整标题过滤或确认 HARAN 已开 |

默认标题过滤：`HARAN;Repair Station;Semi-automatic`

## 源码构建

```bat
cd HaranUiProbe
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o ..\publish
```

## 注意

- 仅探测，不发送按键、不改产线逻辑  
- 遍历深度/控件数有上限，避免卡死  
- 与 NgStationTool 工位工具独立  

## 许可

MIT
