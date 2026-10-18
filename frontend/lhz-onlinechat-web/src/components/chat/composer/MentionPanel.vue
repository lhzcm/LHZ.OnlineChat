<script setup lang="ts">
import Avatar from '@/components/Avatar.vue'
import type { GroupMemberInfo } from '@/types'

/**
 * @ 提及面板：纯展示（过滤与截断由 MessageComposer 做，它掌握输入框光标位置）。
 */
defineProps<{ members: GroupMemberInfo[] }>()
const emit = defineEmits<{ pick: [member: GroupMemberInfo] }>()
</script>

<template>
  <div class="mention-panel" @click.stop>
    <button v-for="m in members" :key="m.userId" class="mention-item" @click="emit('pick', m)">
      <Avatar :name="m.nickname" :url="m.avatar" size="sm" />
      <span class="contact-name">{{ m.nickname }}</span>
      <span class="mention-role" v-if="m.role === 0">群主</span>
    </button>
  </div>
</template>

<style scoped>
.mention-panel {
  position: absolute;
  bottom: calc(100% + 8px);
  left: 16px;
  right: 16px;
  max-height: 240px;
  overflow-y: auto;
  background: var(--bg-white);
  border: 1px solid var(--border);
  border-radius: 12px;
  box-shadow: var(--shadow);
  z-index: 20;
  padding: 6px;
}

.mention-item {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 8px 10px;
  border: none;
  border-radius: 8px;
  background: transparent;
  cursor: pointer;
  text-align: left;
  transition: background 0.15s;
}

.mention-item:hover {
  background: var(--bg-hover);
}

.mention-role {
  font-size: 11px;
  color: var(--primary);
  background: var(--active-bg);
  border-radius: 8px;
  padding: 0 6px;
  line-height: 16px;
  flex-shrink: 0;
}
</style>