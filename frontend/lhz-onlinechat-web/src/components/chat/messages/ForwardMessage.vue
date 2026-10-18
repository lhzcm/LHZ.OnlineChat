<script setup lang="ts">
import { computed } from 'vue'
import { forwardItemPreview, parseForwardBundle, type ForwardedItem } from '@/utils/forward'

/**
 * 「聊天记录」合并转发卡片（messageType = 4）。
 * content 是 JSON 快照；解析失败时退化成占位卡片，绝不让界面崩。
 */
const props = defineProps<{ content: string }>()
const emit = defineEmits<{ open: [items: ForwardedItem[]] }>()

const items = computed(() => parseForwardBundle(props.content || ''))
const preview = computed(() => (items.value || []).slice(0, 3))

function open() {
  emit('open', items.value || [])
}
</script>

<template>
  <div class="msg-forward" @click.stop="open">
    <div class="fwd-head">
      <span class="fwd-ico">🗂</span>
      <span class="fwd-title">聊天记录</span>
      <span class="fwd-count" v-if="items">{{ items.length }} 条</span>
    </div>
    <div class="fwd-list" v-if="items">
      <div class="fwd-line" v-for="(it, i) in preview" :key="i">
        <span class="fwd-name">{{ it.n || '对方' }}：</span>
        <span class="fwd-text">{{ forwardItemPreview(it) }}</span>
      </div>
      <div class="fwd-more" v-if="items.length > 3">…共 {{ items.length }} 条</div>
    </div>
    <div class="fwd-broken" v-else>记录无法解析</div>
    <div class="fwd-foot">点击查看 ›</div>
  </div>
</template>

<style scoped>
.msg-forward {
  border: 1px solid var(--border, #e5e7eb);
  border-left: 3px solid var(--primary, #3b82f6);
  border-radius: 10px;
  background: var(--bg-panel, #fff);
  padding: 10px 12px;
  cursor: pointer;
  transition: background 0.15s;
}

.msg-forward:hover {
  background: var(--bg-hover);
}

.fwd-head {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
  margin-bottom: 6px;
}

.fwd-count {
  margin-left: auto;
  font-size: 11px;
  font-weight: normal;
  color: var(--text-secondary);
}

.fwd-list {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.fwd-line {
  display: flex;
  gap: 2px;
  font-size: 12.5px;
  color: var(--text-secondary);
  overflow: hidden;
  white-space: nowrap;
}

.fwd-name {
  color: var(--text-primary);
  flex-shrink: 0;
}

.fwd-text {
  overflow: hidden;
  text-overflow: ellipsis;
}

.fwd-more,
.fwd-broken {
  font-size: 12px;
  color: var(--text-secondary);
}

.fwd-foot {
  margin-top: 8px;
  font-size: 11px;
  color: var(--primary);
  text-align: right;
}
</style>