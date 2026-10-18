<script setup lang="ts">
import { emojiGroups } from '@/constants/emojis'

/**
 * 表情面板：纯展示。
 * 开合、定位、点外面关闭都由 MessageComposer 负责（它同时持有面板与触发按钮的 ref），
 * 面板自己只管把选中的 emoji 抛出去。
 */
const emit = defineEmits<{ pick: [emoji: string] }>()
</script>

<template>
  <div class="emoji-panel" @click.stop>
    <div class="emoji-group" v-for="g in emojiGroups" :key="g.name">
      <div class="emoji-group-title">{{ g.name }}</div>
      <div class="emoji-grid">
        <button v-for="e in g.list" :key="e" class="emoji-item" @click="emit('pick', e)">{{ e }}</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.emoji-panel {
  position: absolute;
  bottom: calc(100% + 8px);
  left: 16px;
  width: 340px;
  max-height: 260px;
  overflow-y: auto;
  background: var(--bg-white);
  border: 1px solid var(--border);
  border-radius: 12px;
  box-shadow: var(--shadow);
  z-index: 20;
  padding: 12px;
}

.emoji-group {
  margin-bottom: 10px;
}

.emoji-group-title {
  font-size: 12px;
  color: var(--text-secondary);
  margin-bottom: 6px;
}

.emoji-grid {
  display: grid;
  grid-template-columns: repeat(8, 1fr);
  gap: 2px;
}

.emoji-item {
  font-size: 20px;
  padding: 4px;
  border: none;
  background: transparent;
  border-radius: 8px;
  cursor: pointer;
  transition: background 0.15s;
}

.emoji-item:hover {
  background: var(--bg-hover);
}

@media (max-width: 720px) {
  .emoji-panel {
    left: 8px;
    right: 8px;
    width: auto;
  }

  .emoji-grid {
    grid-template-columns: repeat(7, 1fr);
  }
}
</style>