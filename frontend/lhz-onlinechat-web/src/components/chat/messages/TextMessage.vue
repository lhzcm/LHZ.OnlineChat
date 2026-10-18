<script setup lang="ts">
import { computed } from 'vue'
import { renderTextContent } from '@/utils/messageText'

/**
 * 文本消息体：转义 HTML → 搜索关键词高亮 → @提及高亮（纯逻辑在 utils/messageText.ts）。
 * 高亮节点是 v-html 注入的，样式由父级 .msg-bubble 的 :deep(.mention/.search-mark) 提供。
 */
const props = defineProps<{ content: string; highlight?: string }>()

const html = computed(() => renderTextContent(props.content || '', props.highlight))
</script>

<template>
  <span v-html="html"></span>
</template>