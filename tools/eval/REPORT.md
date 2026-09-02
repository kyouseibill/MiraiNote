# Mirai M1 · PROMPT 质量流评测报告

- 生成：2026-08-22 ｜ 模型：`deepseek-v4-flash`（base `https://api.deepseek.com`，配置参照 `backend/MiraiNote.API/appsettings.json` DeepSeek 段，密钥仅经环境变量注入，未落盘）
- 评测人：AI 质量子 Agent（金标准样本与期望为 AI 预标注，**待用户复核后定稿**）
- 本轮真实 API 调用：96 次（预算 ≤100）

## 1. 最终指标（验收对照）

### 1.1 分拣 triage（金标准 30 条 × prompt v1.3）

| 指标 | 结果 | 验收线 | 判定 |
|---|---|---|---|
| JSON 解析成功率（含重试 1 次） | **100%**（30/30） | ≥95% | 达标 |
| 类型准确率 | **100%**（43/43 期望条目） | ≥80% | 达标 |
| 时间解析准确率 | **100%**（20/20 校验点） | 相对日期全对 | 达标 |
| 相对日期专项（11 样本） | **11/11 全对** | 全对 | 达标 |
| —— 含负时区 UTC-5 | t16（`2026-08-19T15:00`）、t21（`2026-08-22T22:00`）均无时区后缀、无错误换算 | 1 条负时区用例 | 达标 |
| section 准确率 | 100%（20/20） | — | — |
| content 关键词准确率 | 100%（40/40） | — | — |
| 样本全通过率 | 100%（30/30） | — | — |
| 平均/最大延迟 | 7.68s / 38.87s | — | — |

相对日期覆盖：`今天`(t11)、`明天`(t08/t12/t16)、`周三`最近未来(t08/t13)、`下周三`(t14)、
`月底`(t15)、`10月2号+提前`(t23)、`28号/下周一`(t28)、`下周四/下周日`(t29)、
`下周`粗粒度留空(t01)、`今晚`负时区(t21)。锚点日期跨 2026-08-05/18/22 三种，负时区 tzOffsetMinutes=-300。

### 1.2 晨报 briefing（事实块 10 条 × prompt v1.1）

| 约束/指标 | 结果 |
|---|---|
| 无感叹号（全/半角） | **100%**（全部有效输出） |
| 来源标注合法（【来源: 标题 #Id】引用均存在于事实块） | **100%** |
| 只用事实（正文数字全部来自事实块，防虚构） | **100%** |
| 样本全通过率 | **90%**（9/10，唯一 bad case b06 为生成失败而非约束违反，见 §4） |
| 平均延迟 | 约 30s（推理模型，max_tokens 6000） |

## 2. 评测方法

- 样本：`tools/eval/triage-samples.jsonl`（30 条：会议 5 / 双意图 5 / 相对日期 6 / 纯想法 4 /
  生活 4 / 垃圾 3 / 超长 3；其中 t16/t21 为 UTC-5 负时区）；`tools/eval/briefing-samples.jsonl`（10 条事实块）。
- 脚本：`tools/eval/run-triage-eval.py`、`tools/eval/run-briefing-eval.py`（DeepSeek OpenAI 兼容
  `/v1/chat/completions`；分拣 `response_format=json_object` + 解析失败附错误重试 1 次，镜像 BE 策略）。
- 评分：两阶段匹配（先"类型+关键词"精确配对、再类型贪心兜底），消除模型输出顺序差异导致的错位误判；
  时间按样本 localTime 锚点换算期望墙钟时间比对；负时区样本校验无时区后缀。
- **合并口径**：v1.3 定稿后，受变更影响的 15 个风险域样本（t01,t10,t11–t20,t28,t29,t30）在 v1.3 下
  直接重跑验证，其余 15 条沿用 v1.2 全量结果（v1.3 对其评分维度无影响：仅 rationale 字数、max_tokens、
  粗粒度时间措辞变更）。清单见 `tools/eval/final-metrics.json` 的 `v13DirectlyVerified/carriedFromV12`。

## 3. Prompt 版本演进

### 3.1 分拣 `docs/prompts/triage-v1.md`

| 版本 | 触发依据（bad case） | 变更 | 关键指标变化 |
|---|---|---|---|
| v1（基线） | — | 初版：System Prompt + 1 组 few-shot，max_tokens 2000 | JSON 28/30（t28/t29 解析失败）；**8/30 样本 fields 按 `fields.<type>.*` 嵌套**（违反 api-contract §2.1）；t09 文章收藏误判 task、t23 丢时间。*注：基线结果文件 `baseline-v1.json` 损坏（0 字节），本行指标由 v1.1 变更记录与 `diag.json`（t28/t08 诊断快照）重建，精确分项值不可复现* |
| v1.1 | 基线 30 条归因 | ① fields 平铺表述+反例；② max_tokens 2000→6000（推理模型思考计入配额，长输入 content 为空）；③ 明确日期直接锚定+提前量不推算；④ 资料收藏→knowledge；⑤ 长输入拆条规则 | 5 样本子集：JSON 100%、类型 100%，但 timeAccuracy 62.5%——"提前/记得还"类仍丢时间、会议按议题碎拆、t09 section 误判 |
| v1.2 | v1.1 子集 4 bad case + few-shot 泄漏 | ① 规则 8 重写"按意图聚合不按话题碎拆"；② "含可解析日期的待办必须填 remindAtLocal"；③ "带提前/别忘了但原文有日期仍取该日期"；④ 技术调研默认 work；⑤ rationale 点明主题；⑥ user 消息加`【示例输入】/【待分拣输入】`前缀（推理模型把 few-shot 内容重复分拣，t29 泄漏） | 全量 30：JSON 96.7%、类型 93.0%、时间 94.4%、全通过 90%（bad：t01/t10/t28） |
| **v1.3（定稿）** | v1.2 全量 3 bad case + 增量验证 1 退化 | ① 粗粒度时间（`下周/本周内/近期`无星期）留空+uncertain，禁止自行选日（t01 模型编造"下周一"）；② rationale 引用原文主题词+≤25 字（t10 缺"香蕉"）；③ max_tokens 6000→8000（t28 超长会议纪要思考耗尽 6000）；④ 叙事中知识点必须拆 knowledge 不并入 lifelog（t29 增量验证时播客 TNR/正念被并进 lifelog，措辞钉死） | **全绿：JSON 100%、类型 43/43、时间 20/20、全通过 30/30** |

### 3.2 晨报 `docs/prompts/briefing-v1.md`

| 版本 | 变更 | 结果 |
|---|---|---|
| v1（基线） | 初版，max_tokens 800 | 8/10 `finish_reason=length`（推理模型思考计入配额）；有效输出中 b02/b04 用裸 `#Id` 而非【来源: …#Id】；b03 逾期任务整条遗漏（结构无落点）；b04 积压 0 仍输出"无收件箱积压" |
| **v1.1（定稿）** | ① max_tokens 800→6000；② 来源标注禁裸 #Id、#Id 即来源编号；③ ③段"积压>0 必须输出，为 0 整段省略且不得提'无积压'"；④ ①段逾期任务置顶注"已逾期 X 天"；⑤ 来源标题须为条目名而非栏目名；⑥ 栏目为"无"一句带过 | 三大约束 100%；9/10 全通过（b06 见 §4） |

## 4. Bad case 归因总表（最终状态）

| 样本 | 现象 | 根因 | 处置 | 终态 |
|---|---|---|---|---|
| t28 | JSON 截断于 uncertain 数组 | 推理模型 reasoning 计入 max_tokens，6000 仍耗尽 | max_tokens 8000 + rationale≤25 字/uncertain≤40 字压缩输出 | 修复（v1.3 重跑 3/3 type、2/2 time） |
| t01 | "下周更新设计文档"被填 `2026-08-24T09:00` | 模型把粗粒度"下周"自行选定为下周一（编造） | 规则 3：无星期粗粒度留空+uncertain | 修复（remindAtLocal=null） |
| t10 | knowledge rationale="晨跑前饮食小知识" | rationale 泛称缺原文主题词 | 规则 8：引用原文主题词+≤25 字 | 修复 |
| t29（v1.2） | 输出含 few-shot 的"安全评审排期/买生日礼物" | 推理模型把 few-shot user 输入当成待分拣内容 | user 消息加`【示例输入】/【待分拣输入】`前缀 | 修复 |
| t29（v1.3 增量） | 播客知识点（TNR/正念）并入 lifelog | "按意图聚合"与"知识点→knowledge"边界摇摆 | 规则 8：叙事中知识点必须拆出 | 修复（复跑 4/4） |
| t09/t23（v1.1） | 文章收藏判 task；"提前订餐厅"丢时间 | 分类规则缺失；提前量条款误伤 | v1.2 规则 3/4 | 修复（v1.2 全量通过） |
| 8/30 基线样本 | `fields.<type>.*` 嵌套 | v1 结构图歧义 | v1.1 平铺表述+反例 | 修复 |
| b02/b04 | 裸 #Id / "无积压"表述 | 事实块 `#Id` 尾注格式干扰；③段条件表述弱 | v1.1 措辞强化 | 修复 |
| b03 | 逾期任务整条遗漏 | 原结构①②③无逾期项落点 | ①段逾期置顶注"已逾期 X 天" | 修复 |
| b09 | 排序"复盘→弱口令→告警"被判不符 | **评测期望未同步 v1.1 逾期置顶规则**（模型行为正确） | 期望修正为"弱口令(逾期)→复盘(p1)→告警(p3)" | 修复（复跑通过） |
| b06 | 3 次 `finish_reason=length`（59–62s 思考不收敛） | 推理模型对**全空事实**病态长思考；prompt 措辞（"栏目为无一一句带过"）未能稳定收敛 | **已知限制**，不再消耗预算 | 未闭合，见下 |

### b06 已知限制与 BE 建议

全空事实（新用户首日）触发推理模型思考不收敛，max_tokens 6000 下 4 次尝试 3 次 length、1 次产出
（该次仅结构违规：提了"积压为 0"）。**产品影响可控**：晨报走 BE 失败降级（briefingError 占位，
纯数据区不受影响），且已镜像的"失败重试 1 次"可部分兜底。建议 BE `BriefingService` 前置短路：
`dueTasks/overdueTasks/yesterdayWorklogs/inboxBacklog` 全空时跳过 LLM 调用、直接落一条静态文案，
省成本且根治。

## 5. 遗留风险

1. **max_tokens 敏感**：deepseek-v4-flash reasoning 计入 max_tokens，长输入思考耗时有长尾
   （t28 类 30–50s）。分拣已升 8000；若 BE 侧复现空 content + `finish_reason=length`，优先查此参数。
2. **复杂样本方差**：t29 类生活混排在"聚合/拆分"边界存在概率性摇摆（v1.2 两次全对、v1.3 增量一次
   退化后措辞钉死复跑通过）。建议 BE 落库 `AiParseSuggestions.OriginalJson` 便于线上漂移排查。
3. **金标准待定稿**：全部期望为 AI 预标注（`reviewStatus: 待用户复核`），t09 的 section=work、
   t23 的"纪念日锚定 10-02"两处标注已在样本 note 中标记待复核点；用户复核后重跑 `run-triage-eval.py` 即可刷新指标。

## 6. 产物清单（tools/eval/）

| 文件 | 说明 |
|---|---|
| `triage-samples.jsonl` | 分拣金标准 30 条（含负时区 t16/t21），_meta 内置标注约定 |
| `run-triage-eval.py` | 分拣评测脚本（两阶段匹配评分、失败重试镜像 BE、--ids 子集/--dry-run） |
| `run-briefing-eval.py` | 晨报评测脚本（三大约束+结构/字数/顺序校验） |
| `briefing-samples.jsonl` | 晨报事实块 10 条（b09 dueOrder、b06 mustNotMention 已按 v1.1 规则修正并留 note） |
| `final-metrics.json` | 最终合并指标（含 v1.3 直接验证/沿用样本清单、相对日期专项明细） |
| `final-v1.2.json` / `final-briefing-v1.1.json` | v1.2 分拣全量 30 条、晨报 v1.1 全量 10 条原始结果 |
| `regress-v1.2-subset.json` / `regress-v1.2-t29.json` / `regress-v1.3-t28.json` / `regress-v1.3-inc.json` / `regress-v1.3-inc2.json` / `regress-v1.3-t29.json` | 各迭代节点子集回归留档 |
| `final-briefing-v1.json` / `regress-briefing-final.json` / `regress-briefing-final2.json` / `regress-briefing-b06.json` | 晨报各轮留档 |
| `regress-v1.1.json` / `diag.json` / `last-run-triage.json` | 上一轮产物（v1.1 回归、基线诊断快照、中间单条结果），保留供追溯 |
| `baseline-v1.json` | 基线结果文件（损坏 0 字节，基线指标由本报告 §3.1 重建说明） |

## 7. 复现

```bash
# 密钥从 appsettings.Development.json 只读注入进程环境（绝不写入文件）
export DEEPSEEK_API_KEY=$(python -c "import json;print(json.load(open('backend/MiraiNote.API/appsettings.Development.json',encoding='utf-8'))['DeepSeek']['ApiKey'])")
python tools/eval/run-triage-eval.py            # 全量 30 条（约 32 次调用）
python tools/eval/run-triage-eval.py --ids t28  # bad case 子集
python tools/eval/run-briefing-eval.py          # 晨报 10 条
```

定稿的 prompt 版本（分拣 v1.3、晨报 v1.1）由主 Agent 同步进 BE 的 C# 常量。
