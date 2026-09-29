<script setup lang="ts">
import { ref, onMounted } from 'vue'
import Avatar from '@/components/Avatar.vue'
import { blacklistApi } from '@/api/blacklist'
import type { BlacklistUser } from '@/types'

const emit = defineEmits<{ close: [] }>()

const blacklist = ref<BlacklistUser[]>([])
const blacklistError = ref('')
const unblockingId = ref<number | null>(null)

onMounted(async () => {
  const res = await blacklistApi.getList()
  if (res.success && res.data) blacklist.value = res.data
})

async function unblockUser(userId: number) {
  unblockingId.value = userId
  blacklistError.value = ''
  try {
    const res = await blacklistApi.unblock(userId)
    if (res.success) {
      blacklist.value = blacklist.value.filter(b => b.userId !== userId)
    } else {
      blacklistError.value = res.message
    }
  } finally {
    unblockingId.value = null
  }
}
</script>

<template>
  <div class="modal-overlay" @click.self="emit('close')">
    <div class="modal">
      <h3>黑名单 ({{ blacklist.length }})</h3>
      <div class="empty" v-if="blacklist.length === 0">
        <span class="empty-icon">🚫</span>
        <span>黑名单为空</span>
      </div>
      <div v-for="b in blacklist" :key="b.userId" class="request-item">
        <Avatar :name="b.nickname" :url="b.avatar" size="sm" />
        <div class="request-info">
          <span class="request-name">{{ b.nickname }}</span>
          <span class="request-meta">账号 {{ b.userId }}</span>
        </div>
        <button class="btn btn-sm btn-ghost kick-btn" :disabled="unblockingId === b.userId" @click="unblockUser(b.userId)">
          {{ unblockingId === b.userId ? '解除中…' : '解除拉黑' }}
        </button>
      </div>
      <p class="modal-error" v-if="blacklistError">{{ blacklistError }}</p>
      <button class="btn btn-ghost" @click="emit('close')">关闭</button>
    </div>
  </div>
</template>
