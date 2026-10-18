<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import Avatar from '@/components/Avatar.vue'
import { MAX_FORWARD_TARGETS, type ForwardTarget } from '@/utils/forward'

const props = defineProps<{
  /** 可选的转发目标（会话 + 好友 + 群，已去重） */
  targets: ForwardTarget[]
  /** 本次要转发的消息条数（只用于文案） */
  count: number
}>()
const emit = defineEmits<{ confirm: [targets: ForwardTarget[]]; close: [] }>()

const keyword = ref('')
const selected = ref<Set<string>>(new Set())
const inputRef = ref<HTMLInputElement | null>(null)

const keyOf = (t: ForwardTarget) => `${t.type}_${t.id}`

const filtered = computed(() => {
  const q = keyword.value.trim().toLowerCase()
  if (!q) return props.targets
  return props.targets.filter(t => t.name.toLowerCase().includes(q))
})

const atLimit = computed(() => selected.value.size >= MAX_FORWARD_TARGETS)

function toggle(t: ForwardTarget) {
  const key = keyOf(t)
  if (selected.value.has(key)) {
    selected.value.delete(key)
    // Set 是浅层响应式：显式重建一次，模板才会更新
    selected.value = new Set(selected.value)
    return
  }
  if (atLimit.value) return
  selected.value = new Set([...selected.value, key])
}

function confirm() {
  const picked = props.targets.filter(t => selected.value.has(keyOf(t)))
  if (picked.length === 0) return
  emit('confirm', picked)
}

onMounted(() => nextTick(() => inputRef.value?.focus()))
</script>

<template>
  <div class="modal-overlay" role="dialog" aria-modal="true" @click.self="emit('close')">
    <div class="modal forward-modal">
      <h3>转发到…</h3>
      <p class="forward-hint">
        将转发 {{ count }} 条消息 · 最多选 {{ MAX_FORWARD_TARGETS }} 个会话
      </p>

      <input ref="inputRef" v-model="keyword" class="search-input forward-search" placeholder="搜索会话 / 好友 / 群" @keydown.enter="confirm" />

      <div class="forward-list">
        <button
          v-for="t in filtered"
          :key="keyOf(t)"
          class="forward-item"
          :class="{ picked: selected.has(keyOf(t)), disabled: atLimit && !selected.has(keyOf(t)) }"
          @click="toggle(t)">
          <span class="forward-check">{{ selected.has(keyOf(t)) ? '✓' : '' }}</span>
          <Avatar :name="t.name" :url="t.avatar" size="sm" />
          <span class="forward-name">{{ t.name }}</span>
          <span class="forward-tag">{{ t.type === 'group' ? '群' : '私聊' }}</span>
        </button>
        <div class="forward-empty" v-if="filtered.length === 0">没有匹配的会话</div>
      </div>

      <div class="forward-actions">
        <button class="btn btn-ghost" @click="emit('close')">取消</button>
        <button class="btn btn-primary" :disabled="selected.size === 0" @click="confirm">
          转发<span v-if="selected.size">（{{ selected.size }}）</span>
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.forward-modal {
  max-width: 420px;
}

.forward-hint {
  margin: 0 0 10px;
  font-size: 12px;
  color: var(--text-secondary);
}

.forward-search {
  width: 100%;
  margin-bottom: 10px;
}

.forward-list {
  max-height: 46vh;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 2px;
  margin-bottom: 12px;
}

.forward-item {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 7px 8px;
  border: none;
  background: transparent;
  border-radius: 8px;
  cursor: pointer;
  text-align: left;
  color: var(--text-primary);
}

.forward-item:hover {
  background: var(--bg-hover);
}

.forward-item.picked {
  background: var(--bg-active, rgba(59, 130, 246, 0.12));
}

.forward-item.disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.forward-check {
  width: 18px;
  height: 18px;
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--border, #d1d5db);
  border-radius: 50%;
  font-size: 12px;
  color: var(--primary);
}

.forward-name {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.forward-tag {
  font-size: 11px;
  color: var(--text-secondary);
  border: 1px solid var(--border, #e5e7eb);
  border-radius: 6px;
  padding: 0 4px;
}

.forward-empty {
  padding: 16px;
  text-align: center;
  font-size: 13px;
  color: var(--text-secondary);
}

.forward-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>
