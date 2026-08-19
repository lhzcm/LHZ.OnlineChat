<script setup lang="ts">
import { reactive, ref } from 'vue'
import Avatar from '@/components/Avatar.vue'
import { useChatStore } from '@/stores/chat'
import type { SessionInfo } from '@/types'

const props = defineProps<{ session: SessionInfo }>()
const emit = defineEmits<{ close: [] }>()
const chatStore = useChatStore()

const target = reactive({ ...props.session })
const error = ref('')
const success = ref('')

async function apply(patch: { isPinned?: boolean; muted?: boolean }) {
  error.value = ''
  success.value = ''
  const res = await chatStore.updateSessionSetting(target.type, target.id, patch)
  if (res.success) {
    if (patch.isPinned !== undefined) target.isPinned = patch.isPinned
    if (patch.muted !== undefined) target.muted = patch.muted
    success.value = '设置已保存'
  } else {
    error.value = res.message
  }
}

async function togglePinned(e: Event) {
  await apply({ isPinned: (e.target as HTMLInputElement).checked })
}

async function toggleMuted(e: Event) {
  await apply({ muted: (e.target as HTMLInputElement).checked })
}
</script>

<template>
  <div class="modal-overlay" @click.self="emit('close')">
    <div class="modal">
      <h3>会话设置</h3>
      <div class="friend-setting-head">
        <Avatar :name="target.name" :url="target.avatar" size="sm" />
        <div class="friend-setting-names">
          <span class="request-name">{{ target.name }}</span>
          <span class="request-meta">{{ target.type === 'group' ? '群聊' : '私聊' }}</span>
        </div>
      </div>
      <label class="setting-switch">
        <span>
          <span class="set-label">置顶会话</span>
          <span class="setting-desc">固定显示在会话列表顶部</span>
        </span>
        <input type="checkbox" :checked="target.isPinned" @change="togglePinned" />
      </label>
      <label class="setting-switch">
        <span>
          <span class="set-label">消息免打扰</span>
          <span class="setting-desc">静音后不增加未读提醒</span>
        </span>
        <input type="checkbox" :checked="target.muted" @change="toggleMuted" />
      </label>
      <p class="modal-error" v-if="error">{{ error }}</p>
      <p class="modal-success" v-if="success">{{ success }}</p>
      <button class="btn btn-ghost" @click="emit('close')">关闭</button>
    </div>
  </div>
</template>
