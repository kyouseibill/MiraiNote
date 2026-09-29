<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { IconLoader2 } from '@tabler/icons-vue'
import HouseholdCompleteDialog from '@/components/household/HouseholdCompleteDialog.vue'
import { useDesignPreview } from '@/composables/useDesignPreview'
import { useHouseholdFeedback } from '@/composables/useHouseholdFeedback'
import type { HouseholdCategory } from '@/types/household'
import { HOUSEHOLD_CATEGORIES, categoryLabel, dueOffsetLabel } from '@/utils/householdFormat'

const { active, asMember, withPreview } = useDesignPreview()
const { store, report } = useHouseholdFeedback()
const category = ref<HouseholdCategory | ''>('')
const loading = ref(false)
const error = ref('')
const completeId = ref<number | null>(null)

const groups = computed(() => [
  { key: 'overdue', title: '已逾期', items: store.upcoming?.overdue ?? [] },
  { key: 'soon', title: '7天内', items: store.upcoming?.within7Days ?? [] },
  { key: 'month', title: '30天内', items: store.upcoming?.within30Days ?? [] },
])

const total = computed(() => groups.value.reduce((sum, group) => sum + group.items.length, 0))

async function load() {
  loading.value = true
  error.value = ''
  try {
    if (active.value) store.ensurePreview(asMember.value)
    await store.fetchUpcoming(category.value || undefined, !active.value)
  } catch (cause) {
    error.value = (await report(cause)).message
  } finally {
    loading.value = false
  }
}

onMounted(load)
watch(category, () => { void load() })
watch(() => active.value, () => { void load() })
</script>

<template>
  <section class="mt-16">
    <div class="flex flex-wrap items-end justify-between gap-3">
      <div>
        <h2 class="font-serif text-[17px] font-medium tracking-[0.04em] text-[#2f2d29]">近期到期</h2>
        <p v-if="store.upcoming" class="mt-1 text-[11px] text-[#99938b]">按北京时间 {{ store.upcoming.today }} 分组，到期日来自服务器</p>
      </div>
      <label class="text-[12px] text-[#7f7a72]">
        <span class="sr-only">按分类筛选</span>
        <select v-model="category" class="h-9 rounded-md border border-[#d8d3ca] bg-white/70 px-2 text-[12px]">
          <option value="">全部分类</option>
          <option v-for="item in HOUSEHOLD_CATEGORIES" :key="item.value" :value="item.value">{{ item.label }}</option>
        </select>
      </label>
    </div>

    <div class="mt-5 border-y border-[#e1dcd4]">
      <p v-if="loading" class="flex h-24 items-center justify-center text-[12px] text-[#99938b]">
        <IconLoader2 :size="16" class="mr-2 animate-spin" />正在读取近期到期
      </p>
      <div v-else-if="error" class="py-8 text-center text-[12px] text-[#9d3b34]">
        <p>{{ error }}</p>
        <button type="button" class="mt-2 text-[#4c6178] hover:underline" @click="load">重试</button>
      </div>
      <p v-else-if="!total" class="py-10 text-center text-[12px] text-[#99938b]">未来 30 天没有到期事项。</p>
      <div v-else>
        <section v-for="group in groups" :key="group.key" class="border-b border-[#e8e3dc] py-3 last:border-b-0">
          <h3 class="text-[11px] font-medium tracking-[0.12em] text-[#8a857c]">{{ group.title }}</h3>
          <p v-if="!group.items.length" class="py-3 text-[12px] text-[#b0aaa2]">暂无</p>
          <div v-for="item in group.items" :key="item.id" class="flex items-start gap-3 py-3">
            <RouterLink :to="withPreview(`/household/items/${item.id}`)" class="min-w-0 flex-1">
              <span class="block truncate text-[13px] text-[#3e3b37] hover:text-[#384b60]">{{ item.name }}</span>
              <span class="mt-1 block text-[11px] text-[#8a857c]">
                {{ categoryLabel(item.category) }}
                <template v-if="item.location"> · {{ item.location }}</template>
                · {{ item.dueDate }} · {{ dueOffsetLabel(item) }}
                <template v-if="item.assigneeName"> · {{ item.assigneeName }}</template>
              </span>
            </RouterLink>
            <button type="button" class="h-8 shrink-0 rounded-md bg-[#4c6178] px-3 text-[12px] text-white hover:bg-[#384b60]" @click="completeId = item.id">
              已完成
            </button>
          </div>
        </section>
      </div>
    </div>
    <RouterLink :to="withPreview('/household')" class="mt-5 inline-flex text-[12px] text-[#4c6178] hover:text-[#384b60]">查看全部家务</RouterLink>
    <HouseholdCompleteDialog :open="completeId != null" :item-id="completeId" @close="completeId = null" @completed="load" />
  </section>
</template>
