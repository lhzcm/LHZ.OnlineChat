<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import Avatar from '@/components/Avatar.vue'
import { useGroupStore } from '@/stores/group'
import type { GroupJoinRequestInfo } from '@/types'

/**
 * 入群申请（凭邀请链接提交的待审批列表）。
 * 与好友申请（RequestsModal）同一套路：逐条处理、失败只提示原因、不整页报错。
 */
const props = defineProps<{ groupId: number; groupName: string }>()
const emit = defineEmits<{ close: [] }>()

const groupStore = useGroupStore()
const loading = ref(true)
const handlingId = ref<number | null>(null)
const error = ref('')

const requests = computed(() => groupStore.joinRequests)

function formatTime(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '' : date.toLocaleString()
}

async function load() {
  loading.value = true
  const res = await groupStore.fetchJoinRequests(props.groupId)
  if (!res.success) error.value = res.message || '读取入群申请失败'
  loading.value = false
}

async function approve(request: GroupJoinRequestInfo) {
  handlingId.value = request.id
  error.value = ''
  const res = await groupStore.approveJoinRequest(props.groupId, request.id)
  if (!res.success) error.value = res.message || '操作失败'
  handlingId.value = null
}

async function reject(request: GroupJoinRequestInfo) {
  handlingId.value = request.id
  error.value = ''
  const res = await groupStore.rejectJoinRequest(props.groupId, request.id)
  if (!res.success) error.value = res.message || '操作失败'
  handlingId.value = null
}

onMounted(load)
</script>

<template>
  <div class="modal-overlay" role="dialog" aria-modal="true" @click.self="emit('close')">
    <div class="modal">
      <h3>入群申请<span class="request-group">{{ groupName }}</span></h3>

      <div class="link-loading" v-if="loading">加载中…</div>

      <template v-else>
        <div class="empty" v-if="requests.length === 0">
          <span class="empty-icon">📭</span>
          <span>暂无待处理的入群申请</span>
        </div>

        <div v-for="r in requests" :key="r.id" class="request-item">
          <Avatar :name="r.nickname" :url="r.avatar" size="sm" />
          <div class="request-info">
            <span class="request-name">{{ r.nickname }}</span>
            <span class="request-meta">
              账号 {{ r.userId }} · {{ formatTime(r.createdAt) }}
            </span>
            <span class="request-message" v-if="r.message">“{{ r.message }}”</span>
          </div>
          <div class="request-actions">
            <button class="btn btn-sm btn-primary" :disabled="handlingId === r.id"
              @click="approve(r)">通过</button>
            <button class="btn btn-sm btn-ghost" :disabled="handlingId === r.id"
              @click="reject(r)">拒绝</button>
          </div>
        </div>
      </template>

      <p class="modal-error" v-if="error">{{ error }}</p>
      <button class="btn btn-ghost" @click="emit('close')">关闭</button>
    </div>
  </div>
</template>

<style scoped>
.request-group {
  margin-left: 8px;
  font-size: 13px;
  font-weight: normal;
  color: var(--text-secondary);
}

.link-loading {
  padding: 18px 0;
  text-align: center;
  color: var(--text-secondary);
  font-size: 13px;
}

.request-message {
  font-size: 12.5px;
  color: var(--text);
  word-break: break-word;
}
</style>
