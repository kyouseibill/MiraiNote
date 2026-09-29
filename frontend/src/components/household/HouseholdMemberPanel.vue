<script setup lang="ts">
import { useHouseholdStore } from '@/stores/household'
import { roleLabel } from '@/utils/householdFormat'

const store = useHouseholdStore()
</script>

<template>
  <div>
    <p class="mb-4 text-[12px] leading-6 text-[var(--mn-muted)]">
      {{ store.household?.name || '家庭' }}目前有 {{ store.members.length }} 位成员。邀请和权限调整会在后续版本里做。
    </p>
    <ul class="divide-y divide-[var(--mn-line)] border-y border-[var(--mn-line)]">
      <li v-for="member in store.members" :key="member.id" class="flex items-center justify-between gap-3 py-4">
        <div class="min-w-0">
          <p class="truncate text-[14px] text-[var(--mn-ink)]">{{ member.username }}</p>
          <p v-if="store.isAdmin && member.email" class="truncate text-[12px] text-[var(--mn-muted)]">{{ member.email }}</p>
        </div>
        <span class="shrink-0 text-[12px]" :class="member.role === 'Admin' ? 'text-[#4c6178]' : 'text-[var(--mn-muted)]'">
          {{ roleLabel(member.role) }}
        </span>
      </li>
    </ul>
    <p v-if="!store.members.length" class="py-8 text-center text-[13px] text-[var(--mn-muted)]">还没有成员。</p>
  </div>
</template>
