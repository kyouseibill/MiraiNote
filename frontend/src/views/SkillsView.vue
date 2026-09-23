<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { onBeforeRouteLeave, RouterLink } from 'vue-router'
import { IconArrowRight, IconLoader2, IconPlus, IconSparkles, IconTrash } from '@tabler/icons-vue'
import AppDialog from '@/components/AppDialog.vue'
import { useToast } from '@/composables/useToast'
import { skillsApi, type SkillDocument, type SkillSummary } from '@/api/skills'

const toast = useToast()
const skills = ref<SkillSummary[]>([])
const selected = ref<SkillDocument | null>(null)
const markdown = ref('')
const implicit = ref(true)
const newName = ref('')
const newDescription = ref('')
const newInstructions = ref('')
const creating = ref(false)
const loading = ref(true)
const busy = ref(false)
const error = ref('')
const showDelete = ref(false)
const showDiscard = ref(false)
let pendingAction: (() => void) | null = null
let leaveResolve: ((allow: boolean) => void) | null = null

const dirty = computed(() => creating.value
  ? Boolean(newName.value || newDescription.value || newInstructions.value)
  : Boolean(selected.value &&
      (markdown.value !== selected.value.markdown || implicit.value !== selected.value.allowImplicitInvocation)))
const activeName = computed(() => creating.value ? newName.value.trim() : selected.value?.name ?? '')

function messageOf(cause: unknown) {
  return cause instanceof Error ? cause.message : '操作失败，请稍后重试'
}

async function refresh() {
  loading.value = true
  try {
    skills.value = await skillsApi.list()
    error.value = ''
  } catch (cause) {
    error.value = `技能列表加载失败：${messageOf(cause)}`
  } finally {
    loading.value = false
  }
}

function requestSwitch(action: () => void) {
  if (!dirty.value) return action()
  pendingAction = action
  showDiscard.value = true
}

function closeDiscard() {
  showDiscard.value = false
  pendingAction = null
  leaveResolve?.(false)
  leaveResolve = null
}

function discardChanges() {
  showDiscard.value = false
  const action = pendingAction
  pendingAction = null
  action?.()
  leaveResolve?.(true)
  leaveResolve = null
}

async function openSkill(name: string) {
  if (busy.value) return
  loading.value = true
  try {
    const detail = await skillsApi.get(name)
    selected.value = detail
    markdown.value = detail.markdown
    implicit.value = detail.allowImplicitInvocation
    creating.value = false
    error.value = ''
  } catch (cause) {
    error.value = `技能加载失败：${messageOf(cause)}`
  } finally {
    loading.value = false
  }
}

function startCreate() {
  creating.value = true
  selected.value = null
  newName.value = ''
  newDescription.value = ''
  newInstructions.value = ''
  implicit.value = true
  error.value = ''
}

function createMarkdown() {
  const descriptionLines = newDescription.value.trim().split(/\r\n|\r|\n/).map((line) => `  ${line.trim()}`).join('\n')
  return `---\nname: ${newName.value.trim()}\ndescription: >\n${descriptionLines}\n---\n\n${newInstructions.value.trim()}\n`
}

async function save() {
  if (busy.value) return
  error.value = ''
  if (creating.value && !/^[a-z0-9][a-z0-9_-]{0,63}$/.test(newName.value.trim())) {
    error.value = '名称只能使用小写字母、数字、- 和 _，最多 64 字符。'
    return
  }
  if (creating.value && (!newDescription.value.trim() || !newInstructions.value.trim())) {
    error.value = '请填写触发描述和执行步骤。'
    return
  }
  busy.value = true
  try {
    const payload = {
      name: activeName.value,
      markdown: creating.value ? createMarkdown() : markdown.value,
      allowImplicitInvocation: implicit.value,
    }
    const detail = creating.value
      ? await skillsApi.create(payload)
      : await skillsApi.update(activeName.value, payload)
    selected.value = detail
    markdown.value = detail.markdown
    creating.value = false
    await refresh()
    toast.success('Skill 已保存')
  } catch (cause) {
    error.value = `保存失败：${messageOf(cause)}`
  } finally {
    busy.value = false
  }
}

async function setEnabled(enabled: boolean) {
  if (!selected.value || busy.value) return
  busy.value = true
  try {
    const detail = await skillsApi.setEnabled(selected.value.name, enabled)
    selected.value = detail
    await refresh()
    toast.success(enabled ? 'Skill 已启用' : 'Skill 已停用')
  } catch (cause) {
    error.value = `切换状态失败：${messageOf(cause)}`
  } finally {
    busy.value = false
  }
}

async function removeSkill() {
  if (!selected.value || busy.value) return
  busy.value = true
  try {
    await skillsApi.remove(selected.value.name)
    showDelete.value = false
    selected.value = null
    markdown.value = ''
    await refresh()
    toast.success('Skill 已移至回收目录')
  } catch (cause) {
    error.value = `删除失败：${messageOf(cause)}`
  } finally {
    busy.value = false
  }
}

onBeforeRouteLeave(() => {
  if (!dirty.value) return true
  showDiscard.value = true
  return new Promise<boolean>((resolve) => { leaveResolve = resolve })
})

onMounted(async () => {
  await refresh()
  if (skills.value.length) await openSkill(skills.value[0]!.name)
})
</script>

<template>
  <div class="mx-auto w-full max-w-[1160px] px-4 py-8 sm:px-8 lg:py-10">
    <div class="mb-7 flex flex-wrap items-end justify-between gap-4 border-b border-[var(--mn-line)] pb-6">
      <div>
        <p class="mb-2 text-[11px] font-medium tracking-[0.17em] text-[var(--mn-muted)]">MIRAI / WORKFLOWS</p>
        <h1 class="font-serif text-2xl text-[var(--mn-ink)] sm:text-[28px]">技能管理</h1>
        <p class="mt-2 max-w-2xl text-[13px] leading-7 text-[#68665f]">
          Skill 是可复用的工作步骤。输入 <code class="rounded bg-[#f0eee8] px-1.5 py-0.5">$名称</code> 明确调用，也可允许 Mirai 根据描述自动选用。
        </p>
      </div>
      <button type="button" class="inline-flex h-10 items-center gap-2 rounded-md bg-[var(--mn-indigo)] px-4 text-[13px] font-medium text-white transition hover:bg-[var(--mn-indigo-dark)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--mn-indigo)]" @click="requestSwitch(startCreate)">
        <IconPlus :size="17" />新建 Skill
      </button>
    </div>

    <div v-if="error" role="alert" class="mb-5 rounded-md border border-[#e4bbb7] bg-[#fff5f3] px-4 py-3 text-[13px] text-[#9d3b34]">{{ error }}</div>
    <div class="grid gap-6 lg:grid-cols-[284px_minmax(0,1fr)]">
      <aside class="min-w-0 rounded-lg border border-[var(--mn-line)] bg-[var(--mn-paper-light)] p-3">
        <div class="flex items-center justify-between px-3 py-2 text-[11px] font-medium tracking-[0.12em] text-[var(--mn-muted)]">
          <span>我的技能</span><span>{{ skills.length }}</span>
        </div>
        <div v-if="loading && !skills.length" role="status" class="px-3 py-8 text-[13px] text-[var(--mn-muted)]">正在读取工作区…</div>
        <div v-else-if="!skills.length" class="px-3 py-8 text-[13px] leading-6 text-[var(--mn-muted)]">还没有 Skill。新建一个，或将 SKILL.md 放入私有工作区的 skills/名称/ 目录。</div>
        <ul v-else class="space-y-1">
          <li v-for="skill in skills" :key="skill.name">
            <button type="button" class="w-full rounded-md px-3 py-3 text-left transition hover:bg-[#f4f1eb] focus-visible:outline focus-visible:outline-2 focus-visible:outline-[var(--mn-indigo)]" :class="selected?.name === skill.name && !creating ? 'bg-[#edf0f2]' : ''" @click="requestSwitch(() => openSkill(skill.name))">
              <span class="flex items-center justify-between gap-2 text-[13px] font-medium text-[var(--mn-ink)]"><span class="truncate">{{ skill.name }}</span><span class="shrink-0 text-[10px]" :class="skill.error ? 'text-[#b4493f]' : skill.enabled ? 'text-[#4c6178]' : 'text-[var(--mn-muted)]'">{{ skill.error ? '需修复' : skill.enabled ? '启用' : '停用' }}</span></span>
              <span class="mt-1 block line-clamp-2 text-[11px] leading-5 text-[var(--mn-muted)]">{{ skill.error || skill.description }}</span>
            </button>
          </li>
        </ul>
      </aside>

      <section class="min-w-0 rounded-lg border border-[var(--mn-line)] bg-[var(--mn-paper-light)] p-5 sm:p-7">
        <div v-if="creating">
          <div class="mb-5 flex items-center gap-2 text-[var(--mn-indigo)]"><IconSparkles :size="19" /><h2 class="text-lg font-medium text-[var(--mn-ink)]">新建 Skill</h2></div>
          <form novalidate class="space-y-5" @submit.prevent="save">
            <label class="block text-[13px] font-medium" for="skill-name">名称</label>
            <input id="skill-name" v-model="newName" autocomplete="off" class="mt-1.5 h-10 w-full rounded-md border border-[var(--mn-line)] bg-white px-3 font-mono text-[13px] focus:outline focus:outline-2 focus:outline-[var(--mn-indigo)]" placeholder="例如 yahoo-transit-jp" :disabled="busy" />
            <label class="block text-[13px] font-medium" for="skill-description">何时使用</label>
            <textarea id="skill-description" v-model="newDescription" rows="6" class="mt-1.5 w-full resize-none rounded-md border border-[var(--mn-line)] bg-white p-3 text-[13px] leading-6 focus:outline focus:outline-2 focus:outline-[var(--mn-indigo)]" placeholder="例如：查询日本铁路、地铁和公交换乘时使用" :disabled="busy" />
            <label class="block text-[13px] font-medium" for="skill-steps">执行步骤</label>
            <textarea id="skill-steps" v-model="newInstructions" rows="12" class="mt-1.5 w-full resize-none rounded-md border border-[var(--mn-line)] bg-white p-3 font-mono text-[12px] leading-6 focus:outline focus:outline-2 focus:outline-[var(--mn-indigo)]" placeholder="写清楚输入、查询步骤、结果格式，以及无法查询时如何处理。" :disabled="busy" />
            <label class="flex cursor-pointer items-center gap-3 text-[13px]"><input v-model="implicit" type="checkbox" class="accent-[var(--mn-indigo)]" :disabled="busy" />允许根据请求自动调用</label>
            <div class="flex flex-wrap items-center gap-3 border-t border-[var(--mn-line)] pt-5"><button type="submit" class="h-10 rounded-md bg-[var(--mn-indigo)] px-5 text-[13px] font-medium text-white hover:bg-[var(--mn-indigo-dark)] disabled:opacity-50" :disabled="busy">{{ busy ? '保存中…' : '创建 Skill' }}</button><button type="button" class="h-10 px-3 text-[13px] text-[var(--mn-muted)] hover:text-[var(--mn-ink)]" @click="requestSwitch(() => { creating = false })">取消</button></div>
          </form>
        </div>

        <div v-else-if="selected">
          <div class="mb-5 flex flex-wrap items-start justify-between gap-4 border-b border-[var(--mn-line)] pb-5">
            <div class="min-w-0"><p class="text-[11px] text-[var(--mn-muted)]">SKILL.md · 私有工作区</p><h2 class="mt-1 break-all font-mono text-[18px] text-[var(--mn-ink)]">{{ selected.name }}</h2><p class="mt-2 text-[12px] leading-6 text-[var(--mn-muted)]">{{ selected.description || '请修复文件头中的描述' }}</p></div>
            <label class="flex cursor-pointer items-center gap-2 text-[12px]"><input type="checkbox" :checked="selected.enabled" class="accent-[var(--mn-indigo)]" :disabled="busy" @change="setEnabled(($event.target as HTMLInputElement).checked)" />{{ selected.enabled ? '已启用' : '已停用' }}</label>
          </div>
          <div v-if="selected.error" role="alert" class="mb-4 rounded-md border border-[#e4bbb7] bg-[#fff5f3] px-3 py-2 text-[12px] text-[#9d3b34]">{{ selected.error }}</div>
          <form novalidate @submit.prevent="save">
            <label for="skill-markdown" class="block text-[13px] font-medium">Skill 原文</label>
            <p class="mb-2 mt-1 text-[11px] leading-5 text-[var(--mn-muted)]">保留原有的引用和附属文件。修改后需确保文件头包含与目录同名的 name 和清晰的 description。</p>
            <textarea id="skill-markdown" v-model="markdown" rows="18" spellcheck="false" class="w-full resize-none rounded-md border border-[var(--mn-line)] bg-white p-4 font-mono text-[12px] leading-6 focus:outline focus:outline-2 focus:outline-[var(--mn-indigo)]" :disabled="busy" />
            <label class="mt-4 flex cursor-pointer items-center gap-3 text-[13px]"><input v-model="implicit" type="checkbox" class="accent-[var(--mn-indigo)]" :disabled="busy" />允许根据请求自动调用</label>
            <p class="mt-1 pl-6 text-[11px] text-[var(--mn-muted)]">关闭后仍可在聊天中用 ${{ selected.name }} 显式调用。</p>
            <div class="mt-6 flex flex-wrap items-center gap-3 border-t border-[var(--mn-line)] pt-5">
              <button type="submit" class="h-10 rounded-md bg-[var(--mn-indigo)] px-5 text-[13px] font-medium text-white hover:bg-[var(--mn-indigo-dark)] disabled:opacity-50" :disabled="busy || !dirty">{{ busy ? '保存中…' : '保存修改' }}</button>
              <RouterLink :to="`/chat?skill=${encodeURIComponent(selected.name)}`" class="inline-flex h-10 items-center gap-1 px-2 text-[13px] text-[var(--mn-indigo)] hover:underline">在聊天中调用<IconArrowRight :size="15" /></RouterLink>
              <button type="button" class="ml-auto inline-flex h-10 items-center gap-1 text-[12px] text-[#b4493f] hover:underline" :disabled="busy" @click="showDelete = true"><IconTrash :size="15" />删除</button>
            </div>
          </form>
        </div>
        <div v-else role="status" class="flex min-h-[350px] flex-col items-center justify-center text-center text-[13px] leading-7 text-[var(--mn-muted)]"><IconSparkles :size="26" class="mb-4 text-[var(--mn-indigo)]" /><p>{{ loading ? '正在读取 Skill…' : '选择左侧 Skill 查看详情，或新建一个。' }}</p><IconLoader2 v-if="loading" :size="18" class="mt-4 animate-spin" /></div>
      </section>
    </div>
  </div>

  <AppDialog :open="showDelete" title="删除 Skill" :description="`将 ${selected?.name || '此 Skill'} 移至工作区回收目录；聊天将不再调用它。`" :busy="busy" @close="showDelete = false">
    <p class="text-[13px] leading-6">附属文件会一同移动，可由管理员从工作区回收目录恢复。</p>
    <template #footer><button type="button" data-dialog-autofocus class="h-9 rounded-md border border-[var(--mn-line)] px-4 text-[13px]" :disabled="busy" @click="showDelete = false">取消</button><button type="button" class="h-9 rounded-md bg-[#b4493f] px-4 text-[13px] text-white disabled:opacity-50" :disabled="busy" @click="removeSkill">{{ busy ? '删除中…' : '删除 Skill' }}</button></template>
  </AppDialog>
  <AppDialog :open="showDiscard" title="放弃未保存的修改？" description="离开当前 Skill 后，未保存的内容将丢失。" @close="closeDiscard">
    <template #footer><button type="button" data-dialog-autofocus class="h-9 rounded-md border border-[var(--mn-line)] px-4 text-[13px]" @click="closeDiscard">继续编辑</button><button type="button" class="h-9 rounded-md bg-[#b4493f] px-4 text-[13px] text-white" @click="discardChanges">放弃修改</button></template>
  </AppDialog>
</template>
