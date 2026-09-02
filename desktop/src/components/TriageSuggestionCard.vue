<script setup lang="ts">
import { computed, nextTick, ref } from 'vue'
import type { TriageSuggestion } from '@/api/types'

// 建议卡（字段级 diff）· 视觉稿 ②：
// - 类型图标 + 置信度 + 依据
// - 勾选即采纳；字段点击行内编辑 → 写入 overrides（dispatch 时深合并覆盖建议值，契约 §2.4）
const props = defineProps<{
  suggestion: TriageSuggestion
  checked: boolean
  overrides: Record<string, unknown>
}>()

const emit = defineEmits<{
  toggle: []
  patch: [key: string, value: unknown]
}>()

const editingKey = ref('')
const editValue = ref('')
const inputEl = ref<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement | null>(null)

/** v-for 内模板 ref 会退化为数组，改用函数 ref 精确捕获当前编辑控件 */
function setInputEl(el: unknown) {
  inputEl.value = (el as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement | null) ?? null
}

const TYPE_META: Record<string, { icon: string; label: string; cls: string }> = {
  task: { icon: '✓', label: '任务', cls: 'bg-blue-50 text-blue-600' },
  worklog: { icon: '▤', label: '工作记录草稿', cls: 'bg-emerald-50 text-emerald-600' },
  lifelog: { icon: '☘', label: '生活记录草稿', cls: 'bg-pink-50 text-pink-600' },
  knowledge: { icon: '📚', label: '知识', cls: 'bg-purple-50 text-purple-600' },
  ignore: { icon: '🚫', label: '忽略', cls: 'bg-gray-100 text-gray-500' },
}
const meta = computed(() => TYPE_META[props.suggestion.type] ?? TYPE_META.ignore!)

const fields = computed<Record<string, unknown>>(() => (props.suggestion.fields as unknown as Record<string, unknown>) ?? {})

/** 当前生效值 = override ?? 建议值 */
function val(key: string): unknown {
  return key in props.overrides ? props.overrides[key] : fields.value[key]
}
function isChanged(key: string): boolean {
  return key in props.overrides
}

const PRIORITY = [
  { v: 1, t: '低' },
  { v: 2, t: '中' },
  { v: 3, t: '高' },
]
const SECTION = [
  { v: 'work', t: '工作' },
  { v: 'life', t: '生活' },
]

const EDITABLE: Record<string, string[]> = {
  task: ['content', 'remindAtLocal', 'priority', 'section'],
  worklog: ['title', 'content', 'tags'],
  lifelog: ['content', 'mood'],
}

/** 字段展示定义（顺序即视觉稿 ② 的 kv 顺序） */
interface FieldDef {
  key: string
  label: string
  kind: 'text' | 'textarea' | 'datetime' | 'select' | 'tags'
  options?: { v: unknown; t: string }[]
}
const fieldDefs = computed<FieldDef[]>(() => {
  if (props.suggestion.type === 'task') {
    return [
      { key: 'content', label: '内容', kind: 'text' },
      { key: 'remindAtLocal', label: '截止', kind: 'datetime' },
      { key: 'priority', label: '优先级', kind: 'select', options: PRIORITY },
      { key: 'section', label: '分区', kind: 'select', options: SECTION },
    ]
  }
  if (props.suggestion.type === 'worklog') {
    return [
      { key: 'title', label: '标题', kind: 'text' },
      { key: 'content', label: '内容', kind: 'textarea' },
      { key: 'tags', label: '标签', kind: 'tags' },
    ]
  }
  if (props.suggestion.type === 'lifelog') {
    return [
      { key: 'content', label: '内容', kind: 'textarea' },
      { key: 'mood', label: '心情', kind: 'text' },
    ]
  }
  return []
})

const editableKeys = computed(() => EDITABLE[props.suggestion.type] ?? [])

function display(key: string): string {
  const v = val(key)
  if (key === 'remindAtLocal') return v ? String(v).replace('T', ' ') : '（无提醒）'
  if (key === 'priority') return PRIORITY.find((p) => p.v === v)?.t ?? String(v)
  if (key === 'section') return SECTION.find((s) => s.v === v)?.t ?? String(v)
  if (key === 'tags') return Array.isArray(v) ? (v as string[]).join('、') || '（无）' : String(v ?? '（无）')
  if (v == null || v === '') return '（空）'
  return String(v)
}

function startEdit(key: string) {
  if (!editableKeys.value.includes(key)) return
  const v = val(key)
  editingKey.value = key
  editValue.value =
    key === 'tags' ? (Array.isArray(v) ? (v as string[]).join(', ') : '') : v == null ? '' : String(v)
  void nextTick(() => inputEl.value?.focus())
}

function commitEdit() {
  const key = editingKey.value
  if (!key) return
  let value: unknown = editValue.value
  if (key === 'tags') {
    value = editValue.value
      .split(/[,，、\s]+/)
      .map((t) => t.trim())
      .filter(Boolean)
  } else if (key === 'priority') {
    value = Number(editValue.value)
  } else if (key === 'remindAtLocal' && !editValue.value) {
    value = null
  }
  emit('patch', key, value)
  editingKey.value = ''
}

function cancelEdit() {
  editingKey.value = ''
}
</script>

<template>
  <div
    class="rounded-xl border bg-paper-card p-3.5"
    :class="checked ? 'border-emerald-300 bg-emerald-50/40' : 'border-ink-faint/20'"
  >
    <!-- 卡头：勾选 + 类型 + 置信度 -->
    <div class="flex items-center gap-2">
      <button
        class="flex h-[18px] w-[18px] shrink-0 items-center justify-center rounded-[5px] border text-[11px]"
        :class="checked ? 'border-emerald-600 bg-emerald-600 text-white' : 'border-ink-faint/40 hover:border-emerald-500'"
        :title="checked ? '取消采纳' : '勾选采纳'"
        @click="emit('toggle')"
      >
        {{ checked ? '✓' : '' }}
      </button>
      <span
        class="flex h-[26px] w-[26px] items-center justify-center rounded-md text-sm"
        :class="meta.cls"
      >
        {{ meta.icon }}
      </span>
      <b class="text-[13px]">
        {{ meta.label
        }}<template v-if="suggestion.type === 'task' && val('section')">
          · {{ val('section') === 'work' ? '工作' : '生活' }}</template
        >
      </b>
      <span v-if="Object.keys(overrides).length" class="rounded border border-amber-300 bg-amber-50 px-1 text-[10px] text-amber-700">
        已修改 {{ Object.keys(overrides).length }} 项
      </span>
      <span class="ml-auto text-[11px] text-ink-faint">置信度 {{ suggestion.confidence.toFixed(2) }}</span>
    </div>

    <!-- 字段级 kv（点击行内编辑） -->
    <dl class="mt-2 grid grid-cols-[64px_1fr] items-start gap-x-3 gap-y-1 text-[12.5px]">
      <template v-for="f in fieldDefs" :key="f.key">
        <dt class="pt-0.5 text-ink-sub">{{ f.label }}</dt>
        <dd class="min-w-0">
          <template v-if="editingKey === f.key">
            <!-- 编辑态 -->
            <select
              v-if="f.kind === 'select'"
              :ref="setInputEl"
              v-model="editValue"
              class="w-full max-w-[220px] rounded border border-brand-line bg-white px-1.5 py-0.5 text-[12.5px] outline-none"
              @change="commitEdit"
              @blur="commitEdit"
              @keydown.esc.stop="cancelEdit"
            >
              <option v-for="o in f.options" :key="String(o.v)" :value="String(o.v)">{{ o.t }}</option>
            </select>
            <input
              v-else-if="f.kind === 'datetime'"
              :ref="setInputEl"
              v-model="editValue"
              type="datetime-local"
              class="w-full max-w-[220px] rounded border border-brand-line bg-white px-1.5 py-0.5 text-[12.5px] outline-none"
              @keydown.enter.prevent="commitEdit"
              @keydown.esc.stop="cancelEdit"
              @blur="commitEdit"
            />
            <textarea
              v-else-if="f.kind === 'textarea'"
              :ref="setInputEl"
              v-model="editValue"
              rows="2"
              class="w-full rounded border border-brand-line bg-white px-1.5 py-0.5 text-[12.5px] leading-6 outline-none"
              @keydown.enter.exact.prevent="commitEdit"
              @keydown.esc.stop="cancelEdit"
              @blur="commitEdit"
            />
            <input
              v-else
              :ref="setInputEl"
              v-model="editValue"
              type="text"
              class="w-full rounded border border-brand-line bg-white px-1.5 py-0.5 text-[12.5px] outline-none"
              @keydown.enter.prevent="commitEdit"
              @keydown.esc.stop="cancelEdit"
              @blur="commitEdit"
            />
            <span class="ml-2 text-[10px] text-ink-faint">Enter 保存 · Esc 取消</span>
          </template>
          <template v-else>
            <!-- 展示态（可编辑字段带虚线下划线提示） -->
            <button
              v-if="editableKeys.includes(f.key)"
              class="group max-w-full truncate text-left text-ink"
              :class="isChanged(f.key) ? 'rounded bg-amber-50 px-1 font-medium text-amber-800' : ''"
              :title="`${f.label}可编辑`"
              @click="startEdit(f.key)"
            >
              {{ display(f.key) }}<span class="ml-1 text-[10px] text-ink-faint opacity-0 transition group-hover:opacity-60">✎</span>
            </button>
            <span v-else class="text-ink-sub">{{ display(f.key) }}</span>
          </template>
        </dd>
      </template>
    </dl>

    <div v-if="suggestion.rationale" class="mt-2 text-[11px] text-ink-faint">依据：{{ suggestion.rationale }}</div>
    <div v-if="suggestion.type === 'knowledge' || suggestion.type === 'ignore'" class="mt-2 text-[11px] text-ink-faint">
      该类型建议不可分发入库（仅参考）
    </div>
  </div>
</template>
