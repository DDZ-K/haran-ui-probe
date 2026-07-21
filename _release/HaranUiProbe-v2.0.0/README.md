# HARAN UI 探测 v2 · 状态栏截图

Viscom/HARAN「Semi-automatic Repair Station Display」**就绪信号探针**。

## 结论背景

- 供应商**无**官方就绪信号  
- UIA 读不到 `Waiting for Input` / `Currently no Repair Data`（状态栏自绘）  
- **v2 主方案**：截取窗口**底栏** → 与空闲/待判**模板**比对  

## 使用步骤（产线电脑）

1. 解压，双击 `HaranUiProbe.exe`（HARAN 若管理员运行，本工具也管理员）  
2. 打开 HARAN  
3. **空闲**（底栏 `Currently no Repair Data`、全蓝）  
   → 点 **截取底栏** → **保存为空闲模板**  
4. **待判**（红框 + 底栏 `Waiting for Input`）  
   → 点 **截取底栏** → **保存为待判模板**  
5. 之后点 **立即匹配** 或勾选 **自动轮询**  
6. 看顶部状态：  
   - 绿色 **Waiting for Input（可判定）**  
   - 蓝色 **no Repair Data（空闲）**  
   - 橙色 **未知**（调阈值/底栏像素，或重录模板）  

模板目录：`exe 同级\templates\`  
  - `status_idle.png`  
  - `status_waiting.png`  

## 参数

| 项 | 建议 |
|----|------|
| 底栏像素 | 36～48（把 State 那条蓝底栏包全） |
| 阈值 | 0.85～0.92（误报多就升高） |
| 轮询 | 400～800 ms |

## 构建

```bat
cd HaranUiProbe
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -o ..\publish
```

## 许可

MIT
