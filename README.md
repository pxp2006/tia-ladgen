# TIA LAD Generator

用 **西门子 TIA Portal Openness** 自动生成梯形图（LAD）程序块并导入 TIA Portal V21 的工具链。

> 起因：装袋机需要一套 11 步顺序控制的梯形图主程序，手画费时且要反复改时间。
> 现在：改一份 Python 生成脚本 → 生成 SimaticML XML → Openness 导入 → 编译 0 错。

---

## 能做什么

- 读 TIA 项目里的设备信息（CPU 型号、固件、模块、模块 MLFB）
- 生成 LAD 程序块（SimaticML XML）并导入 TIA Portal V21
- 自动创建 PLC 变量表、实例 DB（FB/DB 编号可配）
- 自动编译并把编译结果（错误/警告/位置）回读到控制台
- 生成 11 步纯时间顺序控制主程序（装袋机案例）

## 已验证的 LAD/SimaticML 元素（V21）

| 元素 | 写法 | 状态 |
|---|---|---|
| 常开触点 | `Part Name="Contact"` | ✅ |
| 常闭触点 | `Part` 内加 `<Negated Name="operand"/>` | ✅ |
| 线圈 | `Part Name="Coil"` | ✅ |
| 置位/复位线圈 | `Part Name="SCoil"/"RCoil"` | ✅ |
| TON 定时器 | `Part Name="TON"` + `Instance`(TON_TIME) + `TemplateValue time_type` | ✅ |
| 并行分支（OR） | `Openbranch` | ⚠️ 未验证，本项目用双线圈/S-R 规避 |
| 字/整数运算、MOVE、比较 | — | ⚠️ 未验证 |

## Web HMI（零运行时）

`hmi/` 下是一个不依赖 WinCC 运行时的网页 HMI：

- 后端 `server.py`：OPC UA 连 Kepware（asyncua），带 DEMO 回退
- 前端 `index.html`：启动/急停/复位按钮、11 步进度、10 路使能灯、6 路位置灯、心跳
- 启动：`python hmi\server.py`，浏览器打开 http://127.0.0.1:8080
- 连不上 Kepware 时自动进入 DEMO 模式（页面会显示）

## 目录

```
src/        C# 探针（Openness 操作：读设备/建变量/导入块/编译/导出）
tools/      Python 生成器（SimaticML XML 生成、工步表、点位表模板）
docs/       使用说明、时间参数表、开发记录（含踩坑清单）
examples/   生成的 XML 示例、Excel 模板
scripts/    编译与运行脚本
```

## 快速开始

```powershell
# 1. 编译 C# 探针（无需 .NET SDK，用系统自带的 csc.exe）
pwsh scripts\build.ps1

# 2. 生成主程序 XML（Python 3 + 无第三方依赖除 openpyxl）
python tools\gen_main_v3.py

# 3. 在 TIA 里导入 + 编译（需要 TIA Portal V21 已安装且项目已打开）
#    注意：exe 必须放在 TIA 所在的用户会话里运行，见 docs/使用说明.md
.\bin\TiaProbe.exe "D:\TiaLadGen\out" --import
```

## 关键前提

- TIA Portal **V21** + Openness（`PublicAPI\V21\net48`）
- 用户在 Windows 本地组 **Siemens TIA Openness** 中
- 首次连接会弹 `Openness 访问` 授权框，需点“全部选是”（`src/ClickFix.cs` 可自动点）
- **TIA 同时只能打开一个项目**；导出/修改块时需离线
- 实例 DB 的初值不会自动从 FB 拷贝：需要用 `tools/fix_idb.py` 生成带 `StartValue` 的 DB XML 再导入

## 已知限制

- 没有到位反馈时只能靠时间推进（本项目即如此）
- 安全回路必须硬件实现；软件急停只是“使能=0”
- 生成的块是“整块覆盖”，不能只改单个网络

## License

MIT

