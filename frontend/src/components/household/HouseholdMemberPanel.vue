<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppDialog from '@/components/AppDialog.vue'
import { useHouseholdFeedback } from '@/composables/useHouseholdFeedback'
import { formatAccountDateTime } from '@/utils/accountTime'
import { roleLabel } from '@/utils/householdFormat'
import { createIdempotencyKey } from '@/utils/idempotencyKey'

const { toast, store, report } = useHouseholdFeedback()
const identifier = ref('')
const formError = ref('')
const busy = ref(false)
const removingId = ref<number | null>(null)
const leaveOpen = ref(false)
const acceptKeys = ref<Record<number, string>>({})

const soleAdmin = computed(() => store.isAdmin && store.members.filter((member) => member.role === 'Admin').length <= 1)
const awaitingInvitation = computed(() => store.household?.hasHousehold === false)
const removing = computed(() => store.members.find((member) => member.id === removingId.value) ?? null)

onMounted(() => {
  void store.fetchInvitations().catch((error: unknown) => {
    void report(error)
  })
})

function keyFor(id: number) {
  if (!acceptKeys.value[id]) {
    acceptKeys.value = { ...acceptKeys.value, [id]: createIdempotencyKey() }
  }
  return acceptKeys.value[id]
}

async function invite() {
  formError.value = ''
  const value = identifier.value.trim()
  if (!value) {
    formError.value = '请填写用户名或邮箱'
    return
  }
  busy.value = true
  try {
    await store.createInvitation(value)
    identifier.value = ''
    toast.success('已发出邀请')
  } catch (error) {
    formError.value = (await report(error)).message
  } finally {
    busy.value = false
  }
}

async function revoke(id: number) {
  busy.value = true
  try {
    await store.revokeInvitation(id)
    toast.success('已撤回邀请')
  } catch (error) {
    await report(error)
  } finally {
    busy.value = false
  }
}

async function accept(id: number) {
  busy.value = true
  try {
    await store.acceptInvitation(id, keyFor(id))
    toast.success('已加入家庭')
  } catch (error) {
    await report(error)
  } finally {
    busy.value = false
  }
}

async function reject(id: number) {
  busy.value = true
  try {
    await store.rejectInvitation(id)
    toast.success('已拒绝邀请')
  } catch (error) {
    await report(error)
  } finally {
    busy.value = false
  }
}

async function confirmRemove() {
  if (removingId.value == null || !store.isAdmin) return
  busy.value = true
  try {
    await store.removeMember(removingId.value)
    toast.success('已移除成员')
    removingId.value = null
  } catch (error) {
    await report(error)
  } finally {
    busy.value = false
  }
}

async function confirmLeave() {
  if (soleAdmin.value) return
  busy.value = true
  try {
    await store.leaveHousehold()
    toast.success('已退出家庭')
    leaveOpen.value = false
  } catch (error) {
    await report(error)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div>
    <p v-if="!awaitingInvitation" class="mb-4 text-[12px] leading-6 text-[var(--mn-muted)]">
      {{ store.household?.name || '家庭' }}目前有 {{ store.members.length }} 位成员。邀请在站内确认，7 天内有效。
    </p>
    <p v-else class="mb-4 text-[12px] leading-6 text-[var(--mn-muted)]">接受或拒绝下面的邀请。还没处理之前，不会自动创建家庭。</p>

    <section v-if="store.incomingInvitations.length" class="mb-6" data-testid="incoming-invitations">
      <h2 class="mb-2 text-[13px] font-medium text-[var(--mn-ink)]">收到的邀请</h2>
      <ul class="divide-y divide-[var(--mn-line)] border-y border-[var(--mn-line)]">
        <li v-for="invite in store.incomingInvitations" :key="invite.id" class="flex flex-wrap items-center gap-3 py-4">
          <div class="min-w-0 flex-1">
            <p class="text-[14px] text-[var(--mn-ink)]">{{ invite.householdName }}</p>
            <p class="mt-1 text-[12px] text-[var(--mn-muted)]">
              {{ invite.inviterUsername }} 邀请你担任{{ roleLabel(invite.role) }}
              · 有效至 {{ formatAccountDateTime(invite.expiresAt) }}
              <span v-if="invite.isExpired"> · 已过期</span>
            </p>
          </div>
          <button type="button" class="h-9 rounded-md bg-[var(--mn-indigo)] px-3 text-[13px] text-white disabled:opacity-50" data-testid="accept-invitation" :disabled="busy || invite.isExpired" @click="accept(invite.id)">接受</button>
          <button type="button" class="h-9 rounded-md border border-[var(--mn-line)] px-3 text-[13px]" data-testid="reject-invitation" :disabled="busy" @click="reject(invite.id)">拒绝</button>
        </li>
      </ul>
    </section>

    <section v-if="store.isAdmin && !awaitingInvitation" class="mb-6">
      <h2 class="mb-2 text-[13px] font-medium text-[var(--mn-ink)]">邀请成员</h2>
      <p v-if="formError" role="alert" class="mb-2 text-[12px] text-[#9d3b34]">{{ formError }}</p>
      <form class="flex flex-wrap items-center gap-2" data-testid="invite-form" @submit.prevent="invite">
        <label class="sr-only" for="invite-identifier">用户名或邮箱</label>
        <input id="invite-identifier" v-model="identifier" class="form-input h-10 min-w-[220px] flex-1" placeholder="用户名或邮箱" maxlength="200" :disabled="busy" />
        <button type="submit" class="h-10 rounded-md bg-[var(--mn-indigo)] px-4 text-[13px] text-white disabled:opacity-50" :disabled="busy">邀请</button>
      </form>
      <ul v-if="store.outgoingInvitations.length" class="mt-4 divide-y divide-[var(--mn-line)] border-y border-[var(--mn-line)]" data-testid="outgoing-invitations">
        <li v-for="invite in store.outgoingInvitations" :key="invite.id" class="flex flex-wrap items-center gap-3 py-4">
          <div class="min-w-0 flex-1">
            <p class="truncate text-[14px] text-[var(--mn-ink)]">{{ invite.inviteeUsername }}</p>
            <p v-if="invite.inviteeEmail" class="truncate text-[12px] text-[var(--mn-muted)]">{{ invite.inviteeEmail }}</p>
            <p class="mt-1 text-[12px] text-[var(--mn-muted)]">
              {{ roleLabel(invite.role) }} · 有效至 {{ formatAccountDateTime(invite.expiresAt) }}
              <span v-if="invite.isExpired"> · 已过期</span>
            </p>
          </div>
          <button type="button" class="h-9 rounded-md border border-[var(--mn-line)] px-3 text-[12px]" data-testid="revoke-invitation" :disabled="busy" @click="revoke(invite.id)">撤回</button>
        </li>
      </ul>
      <p v-else class="mt-3 text-[12px] text-[var(--mn-muted)]">没有待处理的邀请。</p>
    </section>

    <ul v-if="!awaitingInvitation" class="divide-y divide-[var(--mn-line)] border-y border-[var(--mn-line)]">
      <li v-for="member in store.members" :key="member.id" class="flex items-center justify-between gap-3 py-4">
        <div class="min-w-0">
          <p class="truncate text-[14px] text-[var(--mn-ink)]">{{ member.username }}</p>
          <p v-if="store.isAdmin && member.email" class="truncate text-[12px] text-[var(--mn-muted)]" data-testid="member-email">{{ member.email }}</p>
        </div>
        <div class="flex shrink-0 items-center gap-3">
          <span class="text-[12px]" :class="member.role === 'Admin' ? 'text-[#4c6178]' : 'text-[var(--mn-muted)]'">
            {{ roleLabel(member.role) }}
          </span>
          <button
            v-if="store.isAdmin && member.id !== store.household?.myMemberId"
            type="button"
            class="text-[12px] text-[#b4493f] hover:underline"
            data-testid="remove-member"
            :disabled="busy"
            @click="removingId = member.id"
          >移除</button>
        </div>
      </li>
    </ul>
    <p v-if="!awaitingInvitation && !store.members.length" class="py-8 text-center text-[13px] text-[var(--mn-muted)]">还没有成员。</p>

    <div v-if="!awaitingInvitation" class="mt-6 flex flex-wrap items-center gap-3">
      <button type="button" class="h-9 rounded-md border border-[var(--mn-line)] px-3 text-[13px] disabled:opacity-50" data-testid="leave-household" :disabled="busy || soleAdmin" @click="leaveOpen = true">退出家庭</button>
      <p v-if="soleAdmin" class="text-[12px] text-[var(--mn-muted)]" data-testid="sole-admin-note">家庭至少需要一名管理员</p>
    </div>

    <AppDialog :open="removing != null" title="移除成员" :description="removing ? `确定移除「${removing.username}」？` : ''" :busy="busy" @close="removingId = null">
      <p class="text-[13px] leading-6">移除后，对方负责的事项会变为未指定。完成记录会保留。</p>
      <template #footer>
        <button type="button" class="h-9 rounded-md border border-[var(--mn-line)] px-4 text-[13px]" :disabled="busy" @click="removingId = null">取消</button>
        <button type="button" class="h-9 rounded-md bg-[#b4493f] px-4 text-[13px] text-white disabled:opacity-50" data-testid="confirm-remove-member" :disabled="busy" @click="confirmRemove">移除</button>
      </template>
    </AppDialog>

    <AppDialog :open="leaveOpen" title="退出家庭" description="退出后你将看不到这个家庭的事项。" :busy="busy" @close="leaveOpen = false">
      <p class="text-[13px] leading-6">你负责的事项会变为未指定。完成记录会保留。</p>
      <template #footer>
        <button type="button" class="h-9 rounded-md border border-[var(--mn-line)] px-4 text-[13px]" :disabled="busy" @click="leaveOpen = false">取消</button>
        <button type="button" class="h-9 rounded-md bg-[#b4493f] px-4 text-[13px] text-white disabled:opacity-50" data-testid="confirm-leave" :disabled="busy" @click="confirmLeave">退出</button>
      </template>
    </AppDialog>
  </div>
</template>
