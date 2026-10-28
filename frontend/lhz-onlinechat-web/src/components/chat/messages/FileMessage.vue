<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { cachedFileMeta, rememberFileMeta, resolveFileMeta } from '@/utils/fileMeta'
import { formatBytes } from '@/utils/format'
import type { FileMeta } from '@/types'

/**
 * 文件消息气泡 / 转发明细行。
 *
 * 消息 content 只有 URL，原始文件名与大小来自元数据接口（见 utils/fileMeta.ts，按屏合并批量取）。
 * 取不到时用 URL 末段兜底 —— 宁可显示 GUID，也不要让气泡空着或卡在 loading。
 */
const props = defineProps<{
  url: string
  /** 发送方刚上传完可以直接给名字，省一次查询 */
  name?: string
  size?: number
  /** 转发明细里的紧凑行（少内边距、无下载图标） */
  compact?: boolean
}>()

const meta = ref<FileMeta | null>(
  props.name && props.size !== undefined
    ? { url: props.url, name: props.name, size: props.size }
    : cachedFileMeta(props.url)
)
const loading = ref(!meta.value)

const displayName = computed(() => {
  if (meta.value?.name) return meta.value.name
  const segment = props.url.split('/').pop() || ''
  return segment || '文件'
})

const displaySize = computed(() => (meta.value ? formatBytes(meta.value.size) : ''))

const extension = computed(() => {
  const name = displayName.value
  const dot = name.lastIndexOf('.')
  return dot > 0 ? name.slice(dot + 1).toUpperCase().slice(0, 5) : 'FILE'
})

/** 图标只做"一眼区分"，不追求精确 MIME */
const icon = computed(() => {
  const ext = extension.value.toLowerCase()
  if (['pdf'].includes(ext)) return '📕'
  if (['doc', 'docx', 'odt', 'rtf'].includes(ext)) return '📄'
  if (['xls', 'xlsx', 'ods', 'csv', 'tsv'].includes(ext)) return '📊'
  if (['ppt', 'pptx', 'odp'].includes(ext)) return '📽'
  if (['zip', 'rar', '7z', 'tar', 'gz', 'bz2', 'xz'].includes(ext)) return '📦'
  if (['txt', 'md', 'log'].includes(ext)) return '📝'
  if (['cs', 'ts', 'tsx', 'vue', 'java', 'py', 'go', 'rs', 'c', 'h', 'cpp', 'hpp', 'sql', 'json', 'yaml', 'yml', 'xml'].includes(ext)) return '🧾'
  if (['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp'].includes(ext)) return '🖼'
  if (['mp3', 'wav', 'ogg', 'm4a'].includes(ext)) return '🎵'
  if (['mp4', 'mov', 'avi', 'mkv'].includes(ext)) return '🎬'
  return '📎'
})

async function load() {
  if (meta.value) {
    loading.value = false
    return
  }
  const resolved = await resolveFileMeta(props.url)
  if (resolved) meta.value = resolved
  loading.value = false
}

onMounted(load)

// 列表复用组件实例时 URL 会变：重新解析，避免显示上一个文件的名字
watch(() => props.url, () => {
  meta.value = cachedFileMeta(props.url)
  loading.value = !meta.value
  void load()
})

// 上传方把已知的元数据写进缓存，其它气泡（同 URL）就能直接命中
watch(meta, (value) => {
  if (value) rememberFileMeta(value)
}, { immediate: true })
</script>

<template>
  <a class="msg-file" :class="{ compact, loading }" :href="url" :download="displayName"
    @click.stop rel="noopener">
    <span class="file-icon" aria-hidden="true">{{ icon }}</span>
    <span class="file-info">
      <span class="file-name" :title="displayName">{{ displayName }}</span>
      <span class="file-meta">
        <span class="file-ext">{{ extension }}</span>
        <span v-if="displaySize" class="file-size">{{ displaySize }}</span>
        <span v-else-if="loading" class="file-size">读取中…</span>
      </span>
    </span>
    <span class="file-download" aria-hidden="true">⤓</span>
  </a>
</template>

<style scoped>
.msg-file {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 200px;
  max-width: 300px;
  padding: 8px 10px;
  border-radius: 8px;
  background: var(--bg-hover);
  color: var(--text);
  text-decoration: none;
  transition: background 0.15s;
}

.msg-file:hover {
  background: var(--active-bg);
}

.msg-file.compact {
  min-width: 0;
  max-width: 100%;
  padding: 6px 8px;
  border: 1px solid var(--border);
  background: var(--bg-white);
}

.file-icon {
  font-size: 20px;
  flex-shrink: 0;
}

.file-info {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  flex: 1;
}

.file-name {
  font-size: 13px;
  line-height: 1.4;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-meta {
  display: flex;
  gap: 6px;
  font-size: 11px;
  color: var(--text-secondary);
}

.file-ext {
  font-weight: 600;
}

.file-download {
  flex-shrink: 0;
  font-size: 15px;
  color: var(--primary);
}

.compact .file-download {
  display: none;
}

.msg-file.loading .file-name {
  color: var(--text-secondary);
}
</style>
