# 文档导航

运行与构建方法见 [项目说明](../README.md)。文档按用途分类，历史版本、日期、测试结果及其适用范围保留在原记录中。

## 使用说明（guides）

| 文档 | 内容 |
| --- | --- |
| [按用途可视化组帧](guides/purpose-composer.md) | 三规约的用途入口、参数填写及使用限制 |
| [字段解码范围](guides/decoding-scope.md) | 已支持的字段解释、构造样例和未覆盖边界 |

## 技术参考（reference）

| 文档 | 内容 |
| --- | --- |
| [功能与技术设计](reference/technical-design.md) | 功能分期、程序结构和交付方案；保留0.6.0设计记录 |
| [规约XML与业务解码接入](reference/protocol-catalogs.md) | XML的职责、程序接入方式及维护范围 |
| [DL/T 645规范对齐](reference/dlt645-alignment.md) | 本机规范依据、字典生成规则、参考差异和已知边界 |
| [开源项目调研](reference/open-source-research.md) | 第三方项目、许可证及采用范围；检索日期见正文 |

## 版本说明（releases）

| 版本 | 主要变更 |
| --- | --- |
| [0.7.3](releases/v0.7.3.md) | 优化列宽拖动，移除解析底部重复信息框 |
| [0.7.2](releases/v0.7.2.md) | 实时调整整列，精简解析底部信息 |
| [0.7.1](releases/v0.7.1.md) | 修正规范复查发现的解析、标识和复合时标问题 |
| [0.7.0](releases/v0.7.0.md) | DL/T 645字典统一维护及业务接入 |
| [0.6.0](releases/v0.6.0.md) | 三规约按用途组帧及可视化参数填写 |

## 审查记录（reviews）

| 文档 | 范围 |
| --- | --- |
| [2026-11-02标准对应复查](reviews/standards-2026-11-02.md) | 0.7.0基线的软件逻辑与本机规范核对，修正交付0.7.1 |
| [0.7.0代码审查](reviews/code-v0.7.0.md) | 645字典、解释器、用途读取及构建资源 |
| [2026-10-30代码审查](reviews/code-2026-10-30.md) | XML字典和三规约按用途组帧 |
| [0.5.0代码审查](reviews/code-v0.5.0.md) | 规约回显与组帧状态保持 |
| [0.4.1代码审查](reviews/code-v0.4.1.md) | 主题、滚动条及原文定位 |
| [0.4.0代码审查](reviews/code-v0.4.0.md) | 单帧、多帧分流及界面交互 |
| [0.2.1代码审查](reviews/code-v0.2.1.md) | 帧边界识别及输入长度问题 |

## 验证记录（verification）

| 文档 | 内容 |
| --- | --- |
| [构建与验证历史](verification/history.md) | 各版本的构建、回归、性能、界面核对及验证限制 |
| [0.3.0启动和界面记录](verification/startup-v0.3.0.md) | 历史启动测量和界面调整 |

## 配图（images）

| 目录 | 内容 |
| --- | --- |
| [parser](images/parser/) | 单帧、多帧解析截图，文件名保留版本及亮暗主题 |
| [composer](images/composer/) | 原始HEX与按用途组帧截图 |
| [dlt645](images/dlt645/) | 数据项选择、需量、安全异常及掉电事件截图 |

## 命名约定

- 目录入口使用 `README.md`；其他通用文档使用小写英文和连字符，按主题命名。
- 版本说明使用 `releases/vX.Y.Z.md`；代码审查使用 `reviews/code-vX.Y.Z.md` 或 `reviews/code-YYYY-MM-DD.md`；标准审查使用 `reviews/standards-YYYY-MM-DD.md`。
- 截图按功能分类；已区分版本和主题的截图保留这些信息，其余按功能命名。
