#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Mirai M1 分拣 prompt 评测脚本（PROMPT 流）

用法:
  DEEPSEEK_API_KEY=xxx python run-triage-eval.py                       # 全量 30 条
  DEEPSEEK_API_KEY=xxx python run-triage-eval.py --ids t08,t13          # bad case 子集
  python run-triage-eval.py --dry-run --ids t01                         # 只看装配后的 prompt，不调 API

密钥只从环境变量 DEEPSEEK_API_KEY 读取，绝不写入任何文件。
base URL / 模型名可用 DEEPSEEK_BASE_URL / DEEPSEEK_MODEL 覆盖，默认与
backend/MiraiNote.API/appsettings.json 的 DeepSeek 段一致。
"""
import argparse
import json
import os
import re
import sys
import time
from datetime import datetime

import requests

DEFAULT_BASE_URL = "https://api.deepseek.com"
DEFAULT_MODEL = "deepseek-v4-flash"
ALLOWED_TYPES = {"task", "worklog", "lifelog", "knowledge", "ignore"}


# ---------------------------------------------------------------- prompt 装配
def extract_fenced_blocks(md: str):
    return re.findall(r"```(?:\w+)?\s*\n(.*?)```", md, re.S)


def load_prompt(path: str):
    md = open(path, encoding="utf-8").read()
    m = re.search(r"## System Prompt\s*\n(.*?)(?=\n## |\Z)", md, re.S)
    if not m:
        sys.exit(f"[prompt] 未找到 ## System Prompt 段: {path}")
    blocks = extract_fenced_blocks(m.group(1))
    if not blocks:
        sys.exit(f"[prompt] System Prompt 段内无代码块: {path}")
    system_tpl = blocks[0].strip()

    few_user, few_assistant = None, None
    mu = re.search(r"\*\*(?:示例)?输入\*\*[：:]\s*`(.+?)`", md)
    if mu:
        few_user = mu.group(1)
        tail = md[mu.end():]
        fa = re.search(r"```(?:json)?\s*\n(.*?)```", tail, re.S)
        if fa:
            few_assistant = fa.group(1).strip()
    return system_tpl, few_user, few_assistant


def render_system(tpl: str, sample: dict) -> str:
    out = tpl
    out = out.replace("{{localTime}}", sample["localTime"])
    out = out.replace("{{tzOffsetMinutes}}", str(sample["tzOffsetMinutes"]))
    out = out.replace("{{recentTags}}", sample["recentTags"])
    return out


def build_messages(system_tpl, few_user, few_assistant, raw: str):
    """与 BE 装配约定一致：few-shot 与真实输入均带前缀标记，防止示例内容泄漏进分拣结果。"""
    messages = []
    if few_user and few_assistant:
        messages.append({"role": "user", "content": "【示例输入】\n" + few_user})
        messages.append({"role": "assistant", "content": few_assistant})
    # system 放首条（BE 为 System Prompt + few-shot user/assistant + raw user）
    return [{"role": "system", "content": system_tpl}] + messages + [
        {"role": "user", "content": "【待分拣输入】\n" + raw}]


# ---------------------------------------------------------------- API 调用
def call_api(messages, api_cfg, temperature=0.2, max_tokens=8000, use_response_format=True):
    """返回 (content, finish_reason, latency_s, error)。max_tokens 与 prompt 执行参数一致（v1.3: 8000）。"""
    payload = {
        "model": api_cfg["model"],
        "messages": messages,
        "temperature": temperature,
        "max_tokens": max_tokens,
    }
    if use_response_format:
        payload["response_format"] = {"type": "json_object"}
    t0 = time.time()
    try:
        r = requests.post(
            api_cfg["base_url"].rstrip("/") + "/v1/chat/completions",
            headers={"Authorization": f"Bearer {api_cfg['key']}", "Content-Type": "application/json"},
            json=payload, timeout=120)
        latency = time.time() - t0
        if r.status_code != 200:
            return None, None, latency, f"HTTP {r.status_code}: {r.text[:300]}"
        msg = r.json()["choices"][0]
        content = msg.get("message", {}).get("content") or ""
        finish = msg.get("finish_reason")
        if not content:
            rc = (msg.get("message", {}).get("reasoning_content") or "")[:200]
            return "", finish, latency, f"empty content (finish_reason={finish}) reasoning_head={rc!r}"
        return content, finish, latency, None
    except requests.RequestException as e:
        return None, None, time.time() - t0, f"request error: {e}"


def strip_fences(text: str) -> str:
    t = text.strip()
    if t.startswith("```"):
        t = re.sub(r"^```(?:\w+)?\s*\n?", "", t)
        t = re.sub(r"\n?```\s*$", "", t)
    return t.strip()


def parse_ai_json(text: str):
    """返回 (obj, error)；obj 需含 items 列表才算成功"""
    if text is None:
        return None, "empty response"
    try:
        obj = json.loads(strip_fences(text))
    except json.JSONDecodeError as e:
        return None, f"json decode: {e}"
    if not isinstance(obj, dict) or not isinstance(obj.get("items"), list):
        return None, "schema: missing items[]"
    for it in obj["items"]:
        if not isinstance(it, dict) or it.get("type") not in ALLOWED_TYPES:
            return None, f"schema: bad item type {it.get('type') if isinstance(it, dict) else it!r}"
    return obj, None


def triage_once(sample, system_tpl, few_user, few_assistant, api_cfg, max_tokens=8000):
    """完整分拣调用：解析失败或传输失败均重试 1 次（镜像 BE『失败重试 1 次』）。"""
    msgs = build_messages(render_system(system_tpl, sample), few_user, few_assistant, sample["raw"])
    content, finish, latency, err = call_api(msgs, api_cfg, max_tokens=max_tokens)
    latencies, errors, raw_out, finishs = [latency], [], content, [finish]
    calls = 1
    obj, perr = (None, "api error") if err else parse_ai_json(content)
    if err:
        errors.append(err)
    if err or perr:  # 重试 1 次：附错误提示（BE 同款策略）
        if perr:
            errors.append(perr)
        msgs2 = msgs + [{"role": "user",
                         "content": f"上一次输出不是合法的分拣 JSON（错误：{perr or err}）。请重新输出，仅输出符合约定结构的 JSON，不要任何其他文字。"}]
        content2, finish2, latency2, err2 = call_api(msgs2, api_cfg, max_tokens=max_tokens)
        calls = 2
        latencies.append(latency2)
        finishs.append(finish2)
        if err2:
            errors.append(err2)
            return {"obj": None, "raw": raw_out, "errors": errors, "latencies": latencies,
                    "calls": calls, "retried": True, "finishReasons": finishs}
        obj2, perr2 = parse_ai_json(content2)
        return {"obj": obj2, "raw": content2, "errors": errors + ([perr2] if perr2 else []),
                "latencies": latencies, "calls": calls, "retried": True, "finishReasons": finishs}
    return {"obj": obj, "raw": content, "errors": [], "latencies": latencies,
            "calls": calls, "retried": False, "finishReasons": finishs}


# ---------------------------------------------------------------- 评分
def norm_dt(s):
    if s in (None, ""):
        return None
    if not isinstance(s, str):
        return "INVALID"
    t = s.strip()
    try:
        d = datetime.fromisoformat(t)
        return d.replace(tzinfo=None)  # 期望为本地墙钟时间；时区后缀仅告警
    except ValueError:
        return "INVALID"


def field_of(item, warn_sink=None):
    """取 fields；若模型误把字段包在 fields.task/worklog/lifelog 下则解包并告警（契约要求扁平结构）"""
    f = item.get("fields")
    if isinstance(f, dict) and len(f) == 1 and next(iter(f)) in ("task", "worklog", "lifelog") \
            and isinstance(next(iter(f.values())), dict):
        if warn_sink is not None:
            warn_sink.append("fields_nested_wrapper: 字段被包在 fields.<type> 下（契约要求扁平 fields.*）")
        return next(iter(f.values()))
    return f or {}


def content_text(item):
    f = field_of(item)
    if not f:
        return ""
    parts = [f.get("content") or "", f.get("title") or ""]
    return " ".join(p for p in parts if p)


def match_text(item):
    """内容关键词匹配源：fields(title+content)；knowledge/ignore 契约规定 fields=null，回退到 rationale"""
    t = content_text(item)
    if t:
        return t
    return item.get("rationale") or ""


def grade_sample(sample, result):
    """返回 (details, counters)"""
    exp = sample["expected"]
    d = {"id": sample["id"], "category": sample["category"], "failures": [], "warnings": [],
         "expectedItems": len(exp["items"]), "matched": 0,
         "timeChecked": 0, "timeOk": 0, "sectionChecked": 0, "sectionOk": 0,
         "contentChecked": 0, "contentOk": 0}
    if result["obj"] is None:
        d["failures"].append("parse_fail: " + "; ".join(result["errors"] or ["unknown"]))
        d["actualItems"] = []
        d["actualTypes"] = []
        d["parseOk"] = False
        return d
    d["parseOk"] = True
    items = result["obj"]["items"]
    d["actualItems"] = [{"suggestionId": it.get("suggestionId"), "type": it.get("type"),
                         "fields": it.get("fields")} for it in items]
    d["actualTypes"] = [it.get("type") for it in items]

    # 两阶段匹配：先“类型+关键词”精确配对（消除模型输出顺序不同导致的错位误判），
    # 剩余期望条目再按类型贪心兜底
    pairs = {}
    taken = set()
    for i, ei in enumerate(exp["items"]):
        kws = ei.get("contentKeywordsAny")
        if not kws:
            continue
        for j, it in enumerate(items):
            if j in taken or it.get("type") != ei["type"]:
                continue
            if any(k in match_text(it) for k in kws):
                pairs[i] = j
                taken.add(j)
                break
    used = [False] * len(items)
    for j in taken:
        used[j] = True
    for i, ei in enumerate(exp["items"]):
        if i in pairs:
            cand = pairs[i]
        else:
            cand = -1
            for j, it in enumerate(items):
                if not used[j] and it.get("type") == ei["type"]:
                    cand = j
                    break
        if cand < 0:
            d["failures"].append(f"type_missing: 期望 {ei['type']}（{ei.get('note', '')}），实际类型集 {d['actualTypes']}")
            continue
        used[cand] = True
        d["matched"] += 1
        act = items[cand]
        label = f"{ei['type']}#{act.get('suggestionId', cand + 1)}"

        kws = ei.get("contentKeywordsAny")
        if kws:
            d["contentChecked"] += 1
            text = match_text(act)
            if any(k in text for k in kws):
                d["contentOk"] += 1
            else:
                d["failures"].append(f"content_mismatch[{label}]: 关键词 {kws} 未命中，实际内容: {text[:80]}")

        if "remindAtLocal" in ei:
            d["timeChecked"] += 1
            want = ei["remindAtLocal"]
            got = field_of(act, d["warnings"]).get("remindAtLocal")
            if isinstance(got, str) and ("+" in got[10:] or got.endswith("Z")):
                d["warnings"].append(f"time_tz_suffix[{label}]: {got}（约定应无时区后缀）")
            wn, gn = norm_dt(want), norm_dt(got)
            if wn == gn:
                d["timeOk"] += 1
            else:
                d["failures"].append(f"time_mismatch[{label}]: 期望 {want}，实际 {got!r}")

        if "section" in ei:
            d["sectionChecked"] += 1
            if field_of(act, d["warnings"]).get("section") == ei["section"]:
                d["sectionOk"] += 1
            else:
                d["failures"].append(f"section_mismatch[{label}]: 期望 {ei['section']}，"
                                     f"实际 {field_of(act).get('section')!r}")

    extras = used.count(False)
    d["extraItems"] = extras
    if extras > exp.get("maxExtraItems", 1):
        extra_types = [items[j].get("type") for j in range(len(items)) if not used[j]]
        d["failures"].append(f"extra_items: 多出 {extras} 条（容忍 {exp.get('maxExtraItems', 1)}）：{extra_types}")

    uk = exp.get("uncertainKeywords") or []
    if uk:
        unc = " ".join(result["obj"].get("uncertain") or [])
        missed = [k for k in uk if k not in unc]
        if missed:
            d["warnings"].append(f"uncertain_missing: 未在 uncertain 中提及 {missed}")
    return d


# ---------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--samples", default=__file__.rsplit(os.sep, 1)[0] + os.sep + "triage-samples.jsonl")
    ap.add_argument("--prompt", default=None, help="prompt md 路径，默认仓库 docs/prompts/triage-v1.md")
    ap.add_argument("--ids", default="", help="逗号分隔的样本 id 过滤（bad case 子集回归）")
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--out", default=None, help="结果 JSON 输出路径")
    ap.add_argument("--dry-run", action="store_true", help="不调 API，仅打印装配后的消息")
    args = ap.parse_args()

    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.abspath(here + os.sep + ".." + os.sep + "..")
    prompt_path = args.prompt or os.path.join(repo, "docs", "prompts", "triage-v1.md")
    system_tpl, few_user, few_assistant = load_prompt(prompt_path)

    samples = []
    for line in open(args.samples, encoding="utf-8"):
        line = line.strip()
        if not line:
            continue
        o = json.loads(line)
        if "raw" in o:
            samples.append(o)
    if args.ids:
        want = {x.strip() for x in args.ids.split(",") if x.strip()}
        samples = [s for s in samples if s["id"] in want]
    if args.limit:
        samples = samples[:args.limit]
    if not samples:
        sys.exit("no samples selected")

    api_cfg = None
    if not args.dry_run:
        key = os.environ.get("DEEPSEEK_API_KEY", "").strip()
        if not key:
            sys.exit("缺少环境变量 DEEPSEEK_API_KEY（密钥不得写入文件，请从 appsettings.Development.json 只读注入进程环境）")
        api_cfg = {"key": key,
                   "base_url": os.environ.get("DEEPSEEK_BASE_URL", DEFAULT_BASE_URL),
                   "model": os.environ.get("DEEPSEEK_MODEL", DEFAULT_MODEL)}
        print(f"[config] base={api_cfg['base_url']} model={api_cfg['model']} samples={len(samples)} "
              f"prompt={os.path.relpath(prompt_path, repo)}", file=sys.stderr)

    if args.dry_run:
        s0 = samples[0]
        print("=== SYSTEM ===\n" + render_system(system_tpl, s0))
        print("\n=== MESSAGES ===")
        for m in build_messages(render_system(system_tpl, s0), few_user, few_assistant, s0["raw"]):
            print(f"--- {m['role']} ---\n{m['content'][:600]}")
        return

    results, details = [], []
    total_calls = 0
    for i, s in enumerate(samples, 1):
        res = triage_once(s, system_tpl, few_user, few_assistant, api_cfg)
        total_calls += res["calls"]
        det = grade_sample(s, res)
        det["latency"] = round(sum(res["latencies"]) / len(res["latencies"]), 2)
        det["retried"] = res["retried"]
        det["finishReasons"] = res.get("finishReasons")
        det["raw"] = res["raw"]
        results.append(det)
        status = "OK " if not det["failures"] else "BAD"
        print(f"[{i:02d}/{len(samples)}] {s['id']} {status} type {det['matched']}/{det['expectedItems']} "
              f"time {det['timeOk']}/{det['timeChecked']} {det['latency']}s"
              + (f" | {'; '.join(det['failures'])[:160]}" if det["failures"] else "")
              + (f" | warn: {'; '.join(det['warnings'])[:120]}" if det["warnings"] else ""), flush=True)
        time.sleep(0.3)

    n = len(results)
    tot_exp = sum(d["expectedItems"] for d in results)
    tot_match = sum(d["matched"] for d in results)
    tot_time = sum(d["timeChecked"] for d in results)
    tot_time_ok = sum(d["timeOk"] for d in results)
    tot_sec = sum(d["sectionChecked"] for d in results)
    tot_sec_ok = sum(d["sectionOk"] for d in results)
    tot_ct = sum(d["contentChecked"] for d in results)
    tot_ct_ok = sum(d["contentOk"] for d in results)
    parse_ok = sum(1 for d in results if d["parseOk"])
    lats = [d["latency"] for d in results]
    summary = {
        "promptFile": os.path.relpath(prompt_path, repo),
        "model": api_cfg["model"], "sampleCount": n, "totalApiCalls": total_calls,
        "jsonParseSuccessRate": round(parse_ok / n, 4),
        "typeAccuracy": round(tot_match / tot_exp, 4) if tot_exp else None,
        "timeAccuracy": round(tot_time_ok / tot_time, 4) if tot_time else None,
        "sectionAccuracy": round(tot_sec_ok / tot_sec, 4) if tot_sec else None,
        "contentAccuracy": round(tot_ct_ok / tot_ct, 4) if tot_ct else None,
        "sampleFullPassRate": round(sum(1 for d in results if not d["failures"]) / n, 4),
        "avgLatencyS": round(sum(lats) / len(lats), 2),
        "maxLatencyS": round(max(lats), 2),
    }
    bad = [d for d in results if d["failures"]]
    print("\n===== SUMMARY =====")
    for k, v in summary.items():
        print(f"{k}: {v}")
    print(f"badCases: {[d['id'] for d in bad]}")
    print(f"warnings: {[(d['id'], d['warnings']) for d in results if d['warnings']]}")

    out = args.out or os.path.join(here, "last-run-triage.json")
    with open(out, "w", encoding="utf-8") as f:
        json.dump({"summary": summary, "badCases": [{k: d[k] for k in
                                                      ("id", "category", "failures", "warnings", "actualTypes")}
                                                     for d in bad],
                   "details": results}, f, ensure_ascii=False, indent=2)
    print(f"[out] {out}")


if __name__ == "__main__":
    main()
