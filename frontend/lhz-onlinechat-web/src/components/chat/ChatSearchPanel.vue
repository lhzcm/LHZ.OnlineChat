<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'
import { messageApi } from '@/api/message'
import { messagePreview } from '@/utils/forward'
import { formatMsgTime } from '@/utils/format'
import type { ChatType, MessageSearchResult } from '@/types'

/**
 * 会话内搜索：输入条 + 结果下拉（防抖 300ms、分页加载）。
 *
 * 开合状态在父级（头部按钮控制），这里只负责「打开时聚焦、换会话/关闭时清空」。
 * 点结果只 emit `locate(messageId, keyword)` —— 滚动定位与关键词高亮都是父级的职责
 * （它持有消息容器与传给 MessageBubble 的 highlight）。
 */
const props = defineProps<{
  chat: { type: ChatType; id: number; name: string }
  open: boolean
}>()
const emit = defineEmits<{
  close: []
  locate: [messageId: string, keyword: string]
}>()

const keyword = ref('')
const results = ref<MessageSearchResult[]>([])
const searching = ref(false)
const noMore = ref(false)
const page = ref(1)
const inputRef = ref<HTMLInputElement | null>(null)
let timer: number | null = null
// 搜索代数：关键词变化 / 换会话 / 关闭都会 +1，在途请求返回时代数不符就丢弃，
// 否则旧关键词（或上一个会话）的慢响应会盖掉新结果
let seq = 0

function reset() {
  seq++
  if (timer) {
    clearTimeout(timer)
    timer = null
  }
  keyword.value = ''
  results.value = []
  searching.value = false
  noMore.value = false
  page.value = 1
}

/** 打开时清空并聚焦；关闭时同样清空（避免下次打开看到旧结果） */
watch(() => props.open, (open) => {
  reset()
  if (open) nextTick(() => inputRef.value?.focus())
}, { immediate: true })

/** 换会话清空（结果属于上一个会话） */
watch(() => props.chat.id, () => reset())

/** 输入防抖 300ms 搜索 */
watch(keyword, (kw) => {
  seq++
  if (timer) clearTimeout(timer)
  const q = kw.trim()
  if (!q) {
    results.value = []
    noMore.value = false
    page.value = 1
    searching.value = false
    return
  }
  timer = window.setTimeout(async () => {
    const mySeq = seq
    page.value = 1
    searching.value = true
    try {
      const res = await messageApi.searchMessages(q, 1, 20, props.chat.type, props.chat.id)
      if (mySeq !== seq) return
      if (res.success && res.data) {
        results.value = res.data.items || []
        noMore.value = results.value.length >= (res.data.total || 0)
      } else {
        results.value = []
        noMore.value = true
      }
    } catch {
      if (mySeq === seq) results.value = []
    } finally {
      if (mySeq === seq) searching.value = false
    }
  }, 300)
})

/** 加载更多（按 messageId 去重，避免翻页边界重复） */
async function loadMore() {
  const q = keyword.value.trim()
  if (!q || searching.value || noMore.value) return
  const mySeq = seq
  searching.value = true
  try {
    const nextPage = page.value + 1
    const res = await messageApi.searchMessages(q, nextPage, 20, props.chat.type, props.chat.id)
    if (mySeq !== seq) return
    if (res.success && res.data) {
      const seen = new Set(results.value.map(r => r.messageId))
      const fresh = (res.data.items || []).filter(r => !r.messageId || !seen.has(r.messageId))
      results.value = [...results.value, ...fresh]
      page.value = nextPage
      noMore.value = fresh.length === 0
    } else {
      noMore.value = true
    }
  } catch {
    if (mySeq === seq) noMore.value = true
  } finally {
    if (mySeq === seq) searching.value = false
  }
}

function pick(r: MessageSearchResult) {
  emit('locate', r.messageId || '', keyword.value.trim())
}
</script>

<template>
  <!-- 搜索条 -->
  <div class="chat-search-bar" v-if="open">
    <input ref="inputRef" v-model="keyword" class="input search-input"
      placeholder="搜索当前会话的消息…" maxlength="50" @keydown.esc="emit('close')" />
    <button class="search-close" @click="emit('close')" title="关闭搜索">✕</button>
  </div>

  <!-- 结果下拉 -->
  <div class="chat-search-results" v-if="open && (results.length > 0 || searching || keyword.trim())">
    <button v-for="r in results" :key="r.messageId || `${r.sessionId}-${r.sentAt}`"
      class="search-result-item" @click="pick(r)">
      <span class="contact-name">{{ r.senderName }}</span>
      <span class="search-content">{{ messagePreview(r.content, r.messageType) }}</span>
      <span class="search-time">{{ formatMsgTime(new Date(r.sentAt).getTime()) }}</span>
    </button>
    <div class="search-state" v-if="searching">搜索中…</div>
    <div class="search-state" v-else-if="!results.length && keyword.trim()">未找到相关消息</div>
    <div class="search-state" v-else-if="results.length && !noMore">
      <button class="search-more-btn" @click="loadMore">加载更多</button>
    </div>
  </div>
</template>

<style scoped>
/* 会话内搜索条 */
.chat-search-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 14px;
  border-bottom: 1px solid var(--border);
  background: var(--bg-white);
  flex-shrink: 0;
}

.search-input {
  flex: 1;
  margin: 0;
  font-size: 13px;
}

.search-close {
  border: none;
  background: transparent;
  color: var(--text-secondary);
  font-size: 14px;
  cursor: pointer;
  padding: 4px 6px;
  border-radius: 6px;
}

.search-close:hover {
  background: var(--bg-hover);
  color: var(--text);
}

/* 搜索结果下拉 */
.chat-search-results {
  position: absolute;
  top: 56px;
  right: 14px;
  width: 340px;
  max-width: calc(100% - 28px);
  max-height: 320px;
  overflow-y: auto;
  background: var(--bg-white);
  border: 1px solid var(--border);
  border-radius: 12px;
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.15);
  z-index: 30;
  display: flex;
  flex-direction: column;
  padding: 6px;
}

.search-result-item {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 2px;
  padding: 8px 10px;
  border: none;
  background: transparent;
  border-radius: 8px;
  cursor: pointer;
  text-align: left;
}

.search-result-item:hover {
  background: var(--bg-hover);
}

.search-content {
  font-size: 13px;
  color: var(--text);
  width: 100%;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.search-time {
  font-size: 11px;
  color: var(--text-secondary);
}

.search-state {
  text-align: center;
  color: var(--text-secondary);
  font-size: 12.5px;
  padding: 10px 0;
}

.search-more-btn {
  border: 1px solid var(--border);
  background: var(--bg-hover);
  color: var(--primary);
  font-size: 12px;
  padding: 5px 16px;
  border-radius: 8px;
  cursor: pointer;
}

.search-more-btn:hover {
  background: var(--active-bg);
}

/* 通用输入框基础样式（本组件与 MessageComposer 各持一份：scoped 不能跨组件共享） */
.input {
  flex: 1;
  min-width: 0;
}
</style>
