<script setup lang="ts">
import { ref } from 'vue'
import Avatar from '@/components/Avatar.vue'
import { useFriendStore } from '@/stores/friend'
import type { FriendRequestInfo } from '@/types'

const emit = defineEmits<{ close: [] }>()
const friendStore = useFriendStore()

const handlingRequestId = ref<number | null>(null)
const requestError = ref('')

async function acceptRequest(r: FriendRequestInfo) {
  handlingRequestId.value = r.id
  requestError.value = ''
  try {
    const res = await friendStore.acceptRequest(r.id)
    if (!res.success) requestError.value = res.message
  } finally {
    handlingRequestId.value = null
  }
}

async function rejectRequest(r: FriendRequestInfo) {
  handlingRequestId.value = r.id
  requestError.value = ''
  try {
    const res = await friendStore.rejectRequest(r.id)
    if (!res.success) requestError.value = res.message
  } finally {
    handlingRequestId.value = null
  }
}
</script>

<template>
  <div class="modal-overlay" @click.self="emit('close')">
    <div class="modal">
      <h3>好友申请</h3>
      <div class="empty" v-if="friendStore.pendingRequests.length === 0">
        <span class="empty-icon">📭</span>
        <span>暂无待处理的申请</span>
      </div>
      <div v-for="r in friendStore.pendingRequests" :key="r.id" class="request-item">
        <Avatar :name="r.nickname" :url="r.avatar" size="sm" />
        <div class="request-info">
          <span class="request-name">{{ r.nickname }}</span>
          <span class="request-meta">账号 {{ r.userId }}</span>
        </div>
        <div class="request-actions">
          <button class="btn btn-sm btn-primary" :disabled="handlingRequestId === r.id" @click="acceptRequest(r)">接受</button>
          <button class="btn btn-sm btn-ghost" :disabled="handlingRequestId === r.id" @click="rejectRequest(r)">拒绝</button>
        </div>
      </div>
      <p class="modal-error" v-if="requestError">{{ requestError }}</p>
      <button class="btn btn-ghost" @click="emit('close')">关闭</button>
    </div>
  </div>
</template>
