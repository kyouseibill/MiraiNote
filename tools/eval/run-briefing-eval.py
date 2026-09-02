#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Mirai M1 晨报 prompt 评测脚本（PROMPT 流）

校验 briefing-v1.md 三大约束的遵守率：
  1. 只用事实 —— 正文数字必须全部来自【给定事实】（防虚构数字）
  2. 带来源标注 —— 【来源: 标题 #Id】引用的 (标题, Id) 必须存在于事实块
  3. 无感叹号 —— 全/半角感叹号均禁止
另校验：必提/禁提词、到期任务优先级降序（dueOrder）、积压段出现与否、正文≤200字。

用法:
  DEEPSEEK_API_KEY=xxx python run-briefing-eval.py                # 全量 10 条
  DEEPSEEK_API_KEY=xxx python run-briefing-eval.py --ids b04,b06  # 子集
  python run-briefing-eval.py --dry-run --ids b01                 # 只看装配后消息

密钥只从环境变量 DEEPSEEK_API_KEY 读取，绝不写入任何文件。
"""
import argparse
import json
import os
import re
import sys
import time

import requests

DEFAULT_BASE_URL = "https://api.deepseek.com"
DEFAULT_MODEL = "deepseek-v4-flash"
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))


# ---------------------------------------------------------------- prompt 装配
def load_briefing_prompt(path):
    md = open(path, encoding="utf-8").read()
    ms = re.search(r"## System Prompt\s*\n(.*?)(?=\n## |\Z)", md, re.S)
    if not ms:
        sys.exit("[prompt] 未找到 ## System Prompt 段")
    sys_block = re.findall(r"```\s*\n(.*?)```", ms.group(1), re.S)
    if not sys_block:
        sys.exit("[prompt] System Prompt 段内无代码块")
    mu = re.search(r"## User Prompt 模板\s*\n(.*?)(?=\n## |\Z)", md, re.S)
    if not mu:
        sys.exit("[prompt] 未找到 ## User Prompt 模板 段")
    usr_block = re.findall(r"```\s*\n(.*?)```", mu.group(1), re.S)
    if not usr_block:
        sys.exit("[prompt] User Prompt 模板段内无代码块")
    return sys_block[0].strip(), usr_block[0].strip()


def render_user(tpl, facts):
    out = tpl
    for k in ("date", "weekday", "dueTasks", "overdueTasks", "yesterdayWorklogs",
              "weekStats", "inboxBacklog", "relatedHistory"):
        out = out.replace("{{" + k + "}}", str(facts[k]))
    if "{{" in out:
        left = re.findall(r"\{\{(\w+)\}\}", out)
        sys.exit(f"[prompt] 模板存在未渲染占位符: {left}")
    return out


def facts_text(facts):
    return "\n".join(str(v) for v in facts.values())


# ---------------------------------------------------------------- API 调用
def call_api(messages, api_cfg):
    payload = {"model": api_cfg["model"], "messages": messages,
               "temperature": 0.3, "max_tokens": 6000}  # 与 briefing-v1.md v1.1 执行参数一致
    t0 = time.time()
    try:
        r = requests.post(api_cfg["base_url"].rstrip("/") + "/v1/chat/completions",
                          headers={"Authorization": f"Bearer {api_cfg['key']}",
                                   "Content-Type": "application/json"},
                          json=payload, timeout=120)
        latency = time.time() - t0
        if r.status_code != 200:
            return None, latency, f"HTTP {r.status_code}: {r.text[:200]}"
        msg = r.json()["choices"][0]
        content = msg.get("message", {}).get("content") or ""
        if not content:
            return "", latency, f"empty content (finish_reason={msg.get('finish_reason')})"
        return content, latency, None
    except requests.RequestException as e:
        return None, time.time() - t0, f"request error: {e}"


# ---------------------------------------------------------------- 校验
TAG_RE = re.compile(r"【来源[：:]\s*(.+?)\s*#(\d+)】")
NUM_RE = re.compile(r"\d+(?:\.\d+)?")


def check_sample(sample, out):
    exp = sample["expected"]
    facts = sample["facts"]
    ftext = facts_text(facts)
    d = {"id": sample["id"], "category": sample["category"], "failures": [], "warnings": [],
         "constraints": {}}

    # 1) 无感叹号
    ok = "！" not in out and "!" not in out
    d["constraints"]["noExclamation"] = ok
    if not ok:
        d["failures"].append("exclamation: 输出含感叹号")

    # 2) 来源标注存在性与合法性
    tags = TAG_RE.findall(out)
    fact_ids = set(re.findall(r"#(\d+)", ftext))
    id2line = {}
    for line in ftext.splitlines():
        for fid in re.findall(r"#(\d+)", line):
            id2line.setdefault(fid, line)
    bad_tags = []
    for title, fid in tags:
        if fid not in fact_ids:
            bad_tags.append(f"#{fid}({title}) 不在事实块")
            continue
        # 标题词命中：标题去空白后至少 2 字子串出现在该 Id 所在行
        t = re.sub(r"\s", "", title)
        line = id2line[fid]
        hit = any(t[i:i + 2] in re.sub(r"\s", "", line) for i in range(max(len(t) - 1, 1)))
        if not hit:
            bad_tags.append(f"#{fid} 标题『{title}』与事实行不符：{line.strip()[:60]}")
    has_due = facts["dueTasks"].strip() not in ("", "无")
    need_tag = exp.get("requireSource", True) and has_due
    ok = (not bad_tags) and (len(tags) >= 1 if need_tag else True)
    d["constraints"]["sourceTagValid"] = ok
    d["sourceTags"] = [f"#{fid}" for _, fid in tags]
    if bad_tags:
        d["failures"].append("source_tag_invalid: " + "; ".join(bad_tags))
    if need_tag and not tags:
        d["failures"].append("source_tag_missing: 有到期任务但全文无【来源: …#Id】标注")

    # 3) 只用事实：正文数字白名单（剔除来源标注行）
    body = TAG_RE.sub("", out)
    allowed = set(NUM_RE.findall(ftext))
    out_nums = set(NUM_RE.findall(body))
    ghost = sorted(n for n in out_nums if n not in allowed)
    ok = not ghost
    d["constraints"]["factsOnlyNumbers"] = ok
    if ghost:
        d["failures"].append(f"fabricated_numbers: 正文数字 {ghost} 不在事实块中")

    # 4) 必提/禁提
    miss = [w for w in exp.get("mustMention", []) if w not in out]
    if miss:
        d["failures"].append(f"must_mention_missing: {miss}")
    ban = [w for w in exp.get("mustNotMention", []) if w in out]
    if ban:
        d["failures"].append(f"must_not_mention_violation: 出现禁词 {ban}")

    # 5) 到期任务优先级降序（关键词出现位置递增）
    order = exp.get("dueOrder") or []
    if order:
        pos = []
        for w in order:
            i = out.find(w)
            if i < 0:
                pos = None
                d["failures"].append(f"due_order: 未出现『{w}』，无法校验优先级顺序")
                break
            pos.append(i)
        if pos and pos != sorted(pos):
            d["failures"].append(f"due_order: 关键词顺序 {order} 与优先级降序不符（位置 {pos}）")

    # 6) 积压段一致性
    want_backlog = exp.get("expectBacklog", False)
    has_backlog = "积压" in body
    if want_backlog != has_backlog:
        d["failures"].append(f"backlog_section: 期望积压段={want_backlog}，实际含积压字样={has_backlog}")

    # 7) 正文字数（去来源标注与空白/Markdown 符号）
    clean = re.sub(r"[\s#*>①②③·—|-]", "", body)
    d["bodyChars"] = len(clean)
    if len(clean) > 200:
        d["failures"].append(f"too_long: 正文 {len(clean)} 字 > 200")
    return d


# ---------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--samples", default=os.path.join(HERE, "briefing-samples.jsonl"))
    ap.add_argument("--prompt", default=os.path.join(REPO, "docs", "prompts", "briefing-v1.md"))
    ap.add_argument("--ids", default="")
    ap.add_argument("--out", default=None)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    system_tpl, user_tpl = load_briefing_prompt(args.prompt)
    samples = [json.loads(l) for l in open(args.samples, encoding="utf-8")
               if l.strip() and '"id"' in l]
    if args.ids:
        want = {x.strip() for x in args.ids.split(",") if x.strip()}
        samples = [s for s in samples if s["id"] in want]
    if not samples:
        sys.exit("no samples selected")

    if args.dry_run:
        s0 = samples[0]
        print("=== SYSTEM ===\n" + system_tpl)
        print("\n=== USER ===\n" + render_user(user_tpl, s0["facts"]))
        return

    key = os.environ.get("DEEPSEEK_API_KEY", "").strip()
    if not key:
        sys.exit("缺少环境变量 DEEPSEEK_API_KEY")
    api_cfg = {"key": key, "base_url": os.environ.get("DEEPSEEK_BASE_URL", DEFAULT_BASE_URL),
               "model": os.environ.get("DEEPSEEK_MODEL", DEFAULT_MODEL)}
    print(f"[config] base={api_cfg['base_url']} model={api_cfg['model']} samples={len(samples)}",
          file=sys.stderr)

    results, total_calls = [], 0
    for i, s in enumerate(samples, 1):
        messages = [{"role": "system", "content": system_tpl},
                    {"role": "user", "content": render_user(user_tpl, s["facts"])}]
        out, latency, err = call_api(messages, api_cfg)
        total_calls += 1
        if err:  # 传输/HTTP 失败重试 1 次
            out, latency2, err = call_api(messages, api_cfg)
            total_calls += 1
            latency = (latency + latency2) / 2
        if err or not out:
            det = {"id": s["id"], "category": s["category"],
                   "failures": [f"api_fail: {err}"], "warnings": [], "constraints": {},
                   "latency": round(latency, 2), "raw": ""}
        else:
            det = check_sample(s, out)
            det["latency"] = round(latency, 2)
            det["raw"] = out
        results.append(det)
        status = "OK " if not det["failures"] else "BAD"
        print(f"[{i:02d}/{len(samples)}] {s['id']} {status} {det['latency']}s "
              f"{det.get('bodyChars', '-')}字 src={det.get('sourceTags', [])}"
              + (f" | {'; '.join(det['failures'])[:200]}" if det["failures"] else ""), flush=True)
        time.sleep(0.3)

    n = len(results)
    cons = ("noExclamation", "sourceTagValid", "factsOnlyNumbers")
    summary = {"model": api_cfg["model"], "sampleCount": n, "totalApiCalls": total_calls,
               "samplePassRate": round(sum(1 for r in results if not r["failures"]) / n, 4)}
    for c in cons:
        vals = [r["constraints"].get(c) for r in results if c in r["constraints"]]
        summary[c + "Rate"] = round(sum(1 for v in vals if v) / len(vals), 4) if vals else None
    lats = [r["latency"] for r in results]
    summary["avgLatencyS"] = round(sum(lats) / len(lats), 2)

    print("\n===== SUMMARY =====")
    for k, v in summary.items():
        print(f"{k}: {v}")
    print(f"badCases: {[r['id'] for r in results if r['failures']]}")

    out_path = args.out or os.path.join(HERE, "last-run-briefing.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump({"summary": summary,
                   "badCases": [{k: r[k] for k in ("id", "category", "failures", "warnings")}
                                for r in results if r["failures"]],
                   "results": results}, f, ensure_ascii=False, indent=2)
    print(f"[out] {out_path}")


if __name__ == "__main__":
    main()
