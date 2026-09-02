# 任务卡 · PROMPT 质量流

> 你是 Mirai M1 开发的 AI 质量子 Agent。本卡自包含。

## 背景

M1 核心闭环是"捕获→AI 分拣→确认分发"，分拣质量直接决定产品信任。你的任务：建立分拣/晨报 prompt 的**金标准评测集与回归脚本**，并把准确率调到验收线。业务代码归 BE/UI 流。

## 必读

1. `docs/prompts/triage-v1.md`、`docs/prompts/briefing-v1.md` — 当前 prompt 与执行参数
2. `docs/contracts/api-contract.md` §2.1（AiParse JSON Schema）
3. `docs/m1-detailed-design.md` §9.1（验收标准：类型准确率 ≥80%、时间解析零时区错误、JSON 解析成功率 ≥95%）

## 文件边界

- **允许**：`docs/prompts/**`、`tools/eval/**`（新建：评测脚本与数据）
- **禁止**：`backend/`、`frontend/`、`desktop/`、`docs/contracts/`

## 任务分解

1. **金标准集 30 条**（`tools/eval/triage-samples.jsonl`）：覆盖会议记录、双意图混排、相对日期（今天/明天/周三/下周三/月底）、纯想法、生活事件、垃圾输入、边界长度。先从 `backend` 开发库现有 WorkLog/Memo 风格生成候选（只读查询），标注字段（期望 items/类型/时间），**标注结果交用户复核后定稿**
2. **评测脚本**（`tools/eval/run-triage-eval.py` 或 .NET console）：逐条真实调用 DeepSeek（读取环境变量 `DEEPSEEK_API_KEY`，**密钥不得写入任何文件**），输出：JSON 解析成功率、类型准确率、时间字段与期望差异、平均延迟
3. **迭代 prompt**：不准确样本逐条归因（prompt 措辞/few-shot 不足/规则缺失），改 `triage-v1.md`（版本号递增 v1.1、v1.2…，保留变更记录），重跑评测
4. **晨报评测集**（10 条）：构造事实块样例，校验"只用事实、带来源标注、无感叹号"约束遵守率
5. 产出报告 `tools/eval/REPORT.md`：最终各项指标、bad case 清单与归因、prompt 版本演进

## 注意

- DeepSeek API：OpenAI 兼容 `/v1/chat/completions`，base URL 与模型名参照 `backend/MiraiNote.API/appsettings.json` 的 AI 配置段（只读参照，勿改）
- 评测有真实 API 成本：单轮全量 ≤100 次调用，迭代时先跑 bad case 子集

## 验收

1. 类型准确率 ≥80%（金标准定稿版）
2. 时间解析：全部相对日期样本换算正确（含 UTC+8 与一个负时区用例）
3. JSON 解析成功率 ≥95%（含重试）
4. REPORT.md 完整，prompt 变更有据可查

## 完成方式

输出：改动文件清单、最终指标、bad case 归因。**不要 git commit**。定稿的 prompt 版本由主 Agent 同步进 BE 的 C# 常量。
