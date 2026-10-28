<script setup lang="ts">
import { computed } from 'vue'
import FileMessage from '@/components/chat/messages/FileMessage.vue'
import { renderMarkdownMessage } from '@/utils/markdown'
import type { ForwardedItem } from '@/utils/forward'
import { localMediaSrc } from '@/utils/media'

/**
 * 「聊天记录」查看器：把转发卡片里的只读快照摊开展示。
 * 内容来自消息 content（JSON），渲染前一律按类型消毒：Markdown 走 renderMarkdownMessage，
 * 文本用插值（自动转义），图片只允许 http(s)/站内路径。
 */
const props = defineProps<{
  items: ForwardedItem[]
  /** 来源说明（如「转发自 张三」）；转发卡片本身没有单一来源，默认不展示 */
  title?: string
}>()
const emit = defineEmits<{ close: [] }>()

const total = computed(() => props.items.length)

/** 图片/音频地址只放行站内上传目录（与服务端同口径），外链 / javascript: / data: 一律不渲染 */
const mediaSrc = localMediaSrc
</script>

<template>
  <div class="modal-overlay" role="dialog" aria-modal="true" @click.self="emit('close')">
    <div class="modal forward-viewer">
      <h3>聊天记录<span class="viewer-count">{{ total }} 条</span></h3>
      <p class="viewer-hint" v-if="title">{{ title }}</p>

      <div class="viewer-body">
        <div v-for="(it, i) in items" :key="i" class="viewer-item">
          <div class="viewer-meta">
            <span class="viewer-name">{{ it.n || '对方' }}</span>
            <span class="viewer-time" v-if="it.ts">{{ new Date(it.ts).toLocaleString() }}</span>
          </div>
          <div class="viewer-content">
            <img v-if="it.t === 1 && mediaSrc(it.c)" :src="mediaSrc(it.c)" class="viewer-image" alt="图片" loading="lazy" />
            <audio v-else-if="it.t === 5 && mediaSrc(it.c)" :src="mediaSrc(it.c)" class="viewer-audio"
              controls preload="metadata"></audio>
            <div v-else-if="it.t === 3" class="viewer-markdown" v-html="renderMarkdownMessage(it.c)"></div>
            <FileMessage v-else-if="it.t === 2 && mediaSrc(it.c)" :url="it.c" compact />
            <span v-else class="viewer-text">{{ it.t === 1 ? '[图片]' : it.t === 5 ? '[语音]' : it.t === 2 ? '[文件]' : it.c }}</span>
          </div>
        </div>
        <div class="viewer-empty" v-if="items.length === 0">这条记录是空的</div>
      </div>

      <div class="viewer-actions">
        <button class="btn btn-primary" @click="emit('close')">关闭</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.forward-viewer {
  max-width: 520px;
  width: 92vw;
  max-height: 80vh;
  display: flex;
  flex-direction: column;
}

.viewer-count {
  margin-left: 6px;
  font-size: 12px;
  font-weight: normal;
  color: var(--text-secondary);
}

.viewer-hint {
  margin: 0 0 8px;
  font-size: 12px;
  color: var(--text-secondary);
}

.viewer-body {
  flex: 1;
  overflow-y: auto;
  padding: 4px 2px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.viewer-item {
  border-left: 3px solid var(--primary-light, #bfdbfe);
  padding-left: 10px;
}

.viewer-meta {
  display: flex;
  align-items: baseline;
  gap: 8px;
  margin-bottom: 2px;
}

.viewer-name {
  font-size: 12.5px;
  font-weight: 500;
  color: var(--primary);
}

.viewer-time {
  font-size: 11px;
  color: var(--text-secondary);
}

.viewer-content {
  font-size: 14px;
  color: var(--text-primary);
  word-break: break-word;
}

.viewer-text {
  white-space: pre-wrap;
}

.viewer-audio {
  width: 100%;
  max-width: 320px;
  height: 36px;
}

.viewer-image {
  max-width: 100%;
  max-height: 240px;
  border-radius: 6px;
}

.viewer-markdown :deep(p) {
  margin: 4px 0;
}

.viewer-markdown :deep(pre) {
  overflow-x: auto;
  background: var(--code-bg, #f6f8fa);
  padding: 8px 10px;
  border-radius: 6px;
}

.viewer-empty {
  padding: 20px;
  text-align: center;
  color: var(--text-secondary);
  font-size: 13px;
}

.viewer-actions {
  display: flex;
  justify-content: flex-end;
  padding-top: 10px;
}
</style>
