# GitHub 开源项目调研

检索日期：2026-10-28。已使用 GitHub 连接检索并读取相关仓库元数据、README；对最接近需求的项目另检查 LICENSE、发布信息和源码文件清单。以下是调研结论，不代表已运行或完整审计第三方软件。

## 候选对比

| 项目 | 已核实的公开信息 | 许可证信息 | 与需求的关系 |
| --- | --- | --- | --- |
| [Liruitao1993/South_Network_Protocol_Analysis_Tool](https://github.com/Liruitao1993/South_Network_Protocol_Analysis_Tool) | Python / PySide6；README 列出 645、南网协议和单列的南网上行；解析、组帧、收发等；存在独立上行和 645 解析 / 组帧源码文件；有 Windows EXE 发布 | 已读取 [MIT LICENSE](https://github.com/Liruitao1993/South_Network_Protocol_Analysis_Tool/blob/main/LICENSE) | 最接近完整桌面工具，可优先试用和对照；规约版本需要特别核对 |
| [ZerojackShi/Assistent](https://github.com/ZerojackShi/Assistent) | Python；README 声明支持 645 和南网规约，可通过 XML 配置数据项、位域、模板和嵌套报文 | 已读取 [LGPL-3.0 LICENSE](https://github.com/ZerojackShi/Assistent/blob/main/LICENSE) | 值得参考数据字典、字段树和嵌套解析的交互；未证明覆盖本机 2024版V2.0 |
| [bobyuxinyang/dlt645_2007](https://github.com/bobyuxinyang/dlt645_2007) | C；README 描述 645-2007 数据类型定义及封装、解析 API | GitHub 元数据未返回明确 SPDX 许可证 | 适合研究 645 协议核心；不是三协议 Windows 桌面工具，直接复制代码前需要核实授权 |
| [WuSong0/ProtocolAnalysls](https://github.com/WuSong0/ProtocolAnalysls) | Java；README 描述 645-2007 和 USB 转 485，使用 RXTX DLL | GitHub 元数据未返回明确 SPDX 许可证 | 可参考旧式串口抄表流程；不作为当前 Windows EXE 的基础 |

“源码公开”不能自动推导为可任意复用；表中未识别许可证的项目没有被认定为已取得复用授权。当前 FrameTools 没有复制这些项目的代码。

## 最接近项目的核查

[South_Network_Protocol_Analysis_Tool 的 README](https://github.com/Liruitao1993/South_Network_Protocol_Analysis_Tool/blob/main/README.md) 声明有单帧 / 批量解析、组帧、字段与字节联动、串口 / BLE、报文对比等功能。读取的文件清单中可见 `csg_uplink_parser.py`、`csg_uplink_wire.py`、`csg_uplink_frame_gen.py`、`dlt645_parser.py`、`dlt645_frame_gen.py` 等，支持存在对应实现模块的判断；本次没有验证每个模块的运行正确性。

已核实 [1.24.2 发布页](https://github.com/Liruitao1993/South_Network_Protocol_Analysis_Tool/releases/tag/1.24.2)，发布于 2026-10-26，包含 Windows EXE `v1.24.2_20261026185938.exe`，大小 105,129,124 字节。README 说明打包包含 Python 运行时。发布存在不等于本次已经下载、运行或验证兼容性。

版本差异是主要评估点：该 README 的协议表将“南网上行通信规约”标为 **Q/CSG 11109004-2013**，而本机 Word 封面明确是 **2024版V2.0、2026年8月**；其名为“南网协议”的另一项标为 **Q/CSG1209021-2019**。这些名称不能直接等同于本机的本地通信模块接口试行文本。需要对照具体长度、地址、SEQ、DI、数据结构和认证处理，而不能只看支持列表。

## Assistent 的参考价值

[README](https://github.com/ZerojackShi/Assistent/blob/main/README.md) 展示了 XML 中数据项长度、BCD / BIN 类型、单位、小数位、位域和嵌套帧的描述方式。它说明业务解释可以从代码中的固定判断逐渐过渡到数据项配置。

该 README 自述“南网2003”，同时给出的配置名和类型中出现 `CSG13`；本次不据此擅自确认其对应标准版本。应实际核对源码、配置和真实报文。LGPL-3.0 已通过 LICENSE 核实，不等于本次完成了全部依赖的许可证审计。

## 采用建议

如果目标是尽快使用成熟界面，优先试用 South_Network_Protocol_Analysis_Tool，并用本机三份协议的已知请求 / 应答对做兼容性检查。只有确认符合实际规约和设备行为后，才决定是否以它为基础修改。

如果目标是维护一个范围明确、与 Windows 集成、运行依赖少的自有工具，可以保留当前 C# 实现，从帧结构逐步补齐业务数据字典。代价是目前功能覆盖明显少于上述完整工具，需要持续增加有标准和样本支撑的数据项。

本次选择后者，交付独立的帧级首版及可重现测试；开源项目用于产品功能和实现方式的比较，没有把旧版协议代码直接并入当前工具。
