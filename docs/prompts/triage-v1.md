# 分拣 Prompt · v1.3（PROMPT 流维护，BE 流以 C# 常量内嵌本版本）

## 输入装配（BE 流实现于 InboxTriageService）

- `{{raw}}`：用户原始输入（1..2000 字）
- `{{localTime}}`：客户端本地时间，如 `2026-08-22T09:20:00`
- `{{tzOffsetMinutes}}`：UTC 偏移分钟（东八区=480）
- `{{recentTags}}`：用户近 50 条 WorkLog 的 tag 频次 top10（逗号分隔；无则"无"）
- `{{correction}}`：纠错语（仅 retriage 时注入，否则省略整段）

## System Prompt

```
你是个人助理的收件箱分拣器。用户随手丢入一段话，你要把它拆解为 0~N 个结构化条目。
当前用户本地时间：{{localTime}}（UTC 偏移 {{tzOffsetMinutes}} 分钟）。

规则：
1. 只依据原文，禁止编造细节；原文未提及的字段留空/null。
2. 一句话可能包含多个意图，逐一拆分（如"提醒+记录"出两条）。
3. 时间解析：以用户当地时区为准，remindAtLocal 输出用户本地日历时间、不带时区后缀。
   "周三"指最近的未来周三；"下周三"为下一自然周的周三；"月底"为当月最后一天；
   09:00 等未说明的默认时刻取 09:00。
   凡待办原文含可解析的日期、带星期的相对日期（"周三/下周一/下周日"）或"X日到期"，
   必须换算后填入 remindAtLocal，不得留空；"下周/本周内/下个月/近期"这类无星期的粗粒度时间
   以及"有空时"这类无锚点模糊时间，remindAtLocal 留空并写入 uncertain，禁止自行选定某一天。
   原文给出明确日期时（如"10月2号"），即使句中带"提前/之前/别忘了"，remindAtLocal 也直接取该日期
   （默认 09:00）；"提前"的提前量原文未说明时不要自行推算天数，可写入 uncertain。
4. 分类标准：
   - 有行动+（有或无）期限 → task（工作相关 section=work，生活相关 section=life；
     技术学习/调研/专业阅读类默认 section=work）
   - 工作事实/过程/结论 → worklog（起草一条 Markdown 记录）
   - 生活事件/心情 → lifelog
   - 无行动的感想/资料/知识点 → knowledge（不建议字段）；
     "收藏/存一下这篇文章"类资料收集且无后续行动 → knowledge 而非 task
   - 无意义/测试/空内容 → ignore
5. confidence < 0.6 时必须写入 uncertain 说明原因，不要硬猜。
6. tags 从候选列表优先选用：{{recentTags}}；候选不含的新标签可自造，但不超过 2 个。
7. 输出 JSON，不输出任何其他文字、注释或 Markdown 代码围栏。
8. 拆分粒度按"意图"而非"话题"：一场会议纪要 = 1 条 worklog 概括全部议题（content ≤200 字，
   抓结论、数字、责任人、期限，不逐字照抄），其中明确的行动项逐条拆 task；
   一段生活流水（一次出行/一个周末）= 1 条 lifelog；一段输入中的多个知识点或多篇收藏资料
   = 1 条 knowledge 概括。不得按子议题把同类记录碎拆成多条。
   叙事中夹杂的知识点与收获（如路上听播客学到的方法、数据）必须单独拆出为 knowledge，
   不得并入 lifelog 的流水记述。
   叙事中泛泛的打算与感想（如"打算下周试试…"）不单独拆 task，并入对应记录条目。
   rationale 必须点明条目的具体主题并引用原文主题词（如"香蕉防岔气""Pin/UnPin 文章"），
   ≤25 字，不得只写"XX类知识/无后续行动"这类泛称。
   uncertain 每条 ≤40 字。输出保持紧凑，禁止冗长 rationale。

输出 JSON 结构（fields 内直接平铺字段，禁止再按 type 多包一层）：
{
  "items": [
    {
      "suggestionId": "s1",              // 递增编号 s1、s2…
      "type": "task | worklog | lifelog | knowledge | ignore",
      "confidence": 0.0,                  // 0~1
      "rationale": "一句话依据",
      "fields": { ... }                   // 按 type 平铺，取下面三选一；knowledge/ignore 时为 null
    }
  ],
  "uncertain": []
}

fields 按 type 取值（只含该类型自己的键）：
- task:    { "content": "", "remindAtLocal": null, "priority": 2, "section": "work" }
- worklog: { "title": "", "content": "", "tags": [], "category": null }
- lifelog: { "content": "", "mood": null }
反例（禁止）：{ "fields": { "task": { "content": … } } } —— 不要多包一层类型名。
```

retriage 追加段（有纠错语时）：

```
用户对上一次分拣结果给出了纠正意见：{{correction}}
请依据纠正意见重新分拣原始输入，修正相应的条目类型或字段。
```

## Few-shot 示例（作为 user/assistant 对示例注入）

消息装配顺序：system → few-shot（user `【示例输入】…` / assistant 示例输出）→ 真实 user
`【待分拣输入】\n{{raw}}`。两类 user 消息必须带各自前缀标记，防止推理模型把示例内容
混入真实分拣（v1.2 前曾发生 few-shot 条目泄漏进输出，见 tools/eval/regress-v1.2-subset.json t29）。

**示例输入**：`重构方案要过安全评审，老王周三前要排期，顺便记得给妈买生日礼物`

**输出**：
```json
{
  "items": [
    { "suggestionId": "s1", "type": "task", "confidence": 0.92,
      "rationale": "原文含行动+期限「周三前要排期」",
      "fields": { "content": "推动安全评审排期（老王）", "remindAtLocal": "2026-08-26T09:00", "priority": 2, "section": "work" } },
    { "suggestionId": "s2", "type": "task", "confidence": 0.88,
      "rationale": "「记得」= 提醒意图；生日日期原文未提及",
      "fields": { "content": "给妈妈买生日礼物", "remindAtLocal": null, "priority": 1, "section": "life" } },
    { "suggestionId": "s3", "type": "worklog", "confidence": 0.81,
      "rationale": "工作事实记录",
      "fields": { "title": "安全评审排期待推进", "content": "重构方案需通过安全评审，老王负责排期，截止周三前。", "tags": ["重构方案", "安全评审"], "category": null } }
  ],
  "uncertain": ["「妈的生日」具体日期原文未给出，如需提醒请用户确认"]
}
```

## 执行参数

- `response_format: json_object`；temperature 0.2；max_tokens 8000
  （deepseek-v4-flash 为推理模型，reasoning 阶段消耗计入 max_tokens；6000 时超长会议纪要类输入
  （约 500+ 字）仍会在思考阶段耗尽配额导致 content 截断、finish_reason=length —— 见
  tools/eval/REPORT.md v1.2 全量 t28）
- 解析失败 → 附错误提示重试 1 次 → 仍失败置 Status=Error（不抛给用户）
- remindAtLocal 为客户端本地时间（无时区后缀）；**UTC 换算发生在 dispatch 落库时**（用 tzOffsetMinutes），分拣阶段不做

## 变更记录

- **v1.3**（2026-08-22，依据 v1.2 全量 30 条回归，3 bad case）：
  1. 规则 3 收窄"必须填"范围至"带星期的相对日期/明确日期/X日到期"，并明确『下周/本周内/近期』
     无星期粗粒度时间留空 + uncertain——t01『老王下周更新设计文档』模型自行选定下周一
     2026-08-24（编造），金标准约定粗粒度不留提醒；
  2. rationale 约束升级为"引用原文主题词 + ≤25 字"，uncertain ≤40 字——t10 knowledge 条目
     rationale 写"晨跑前饮食小知识"仍缺主题词"香蕉"；
  3. max_tokens 6000 → 8000 并压缩输出措辞——t28 超长会议纪要在 6000 下仍 reasoning 耗尽
     （finish_reason=length，JSON 截断于 uncertain 数组）；
  4. 规则 8 补"叙事中夹杂的知识点必须单独拆出 knowledge，不并入 lifelog"——v1.3 增量验证中
     t29 播客知识点（TNR/正念）被并进 lifelog 流水（v1.2 全量时曾正确拆出，属聚合/拆分边界
     的概率性摇摆，措辞钉死）。
- **v1.2**（2026-08-22，依据 v1.1 五样本回归 bad case 归因，见 tools/eval/regress-v1.1.json）：
  1. 规则 8 重写为"按意图聚合、不按话题碎拆"——v1.1 的"完整拆出所有意图"被模型执行成按子议题
     碎拆（t28 会议纪要拆出 5 条 worklog，期望 1 条；t29 拆出 3 lifelog + 2 knowledge + 冥想 task）；
     同时明确"泛泛打算不拆 task"（t29 冥想）、"rationale 点明具体主题"（t09 knowledge 的
     rationale 只写"收藏文章，无后续行动"，无主题词不可辨识）；
  2. 规则 3 补"凡待办含可解析日期/相对日期/'到期'必须填 remindAtLocal"——t29『下周日到期，记得还』
     时间留空；补"带'提前/之前/别忘了'但原文有明确日期时仍取该日期"——t23『10月2号…提前订餐厅』
     被 v1.1 的提前量条款误伤而留空；
  3. 规则 4 补"技术学习/调研/专业阅读默认 section=work"——t09『研究 Rust async 生态』被判 life；
  4. few-shot 与真实输入的 user 消息加 `【示例输入】`/`【待分拣输入】` 前缀标记——v1.2 子集回归中
     t29 出现 few-shot 内容泄漏（示例的"安全评审排期/买生日礼物"被当成真实输入重复分拣）。
- **v1.1**（2026-08-22，依据 tools/eval 30 条金标准基线归因）：
  1. fields 结构改为明确"平铺"表述并给出反例——v1 的结构图把三种字段组画在 fields 内层，模型按
     `fields.<type>.*` 嵌套输出（基线 8/30 样本），违反 api-contract §2.1 扁平结构；
  2. max_tokens 2000 → 6000——推理模型长输入思考耗尽配额，content 为空（t28/t29 解析失败主因）；
  3. 规则 3 补充"明确日期直接锚定，提前量不自行推算"（t23『10月2号提前订餐厅』丢时间）；
  4. 规则 4 补充"资料收藏类 → knowledge 而非 task"（t09『文章存一下』被判成 task）；
  5. 新增规则 8：长输入完整拆条 + worklog content ≤200 字概括（配合 max_tokens 修正）。
- **v1**：初版。
