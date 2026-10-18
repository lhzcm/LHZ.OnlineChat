<script setup lang="ts">
import { useToast } from '@/composables/useToast'

/**
 * 轻提示浮层：顶部居中、带图标与阴影的卡片，点一下即可关掉。
 *
 * 之前这里是个**没有任何样式**的 div（`v-if` 直接渲染在文档流里），
 * 表现就是一行贴着页面顶部的裸文本 —— 长文案还会被挤到视口外。
 * 现在固定定位 + 最大宽度 + 自动换行，错误与普通提示用颜色/图标区分。
 */
const { toastMsg, toastType, clearToast } = useToast()
</script>

<template>
  <transition name="toast-fade">
    <div v-if="toastMsg" class="app-toast" :class="toastType" role="status" aria-live="polite"
      @click="clearToast">
      <span class="toast-icon" aria-hidden="true">{{ toastType === 'error' ? '⚠' : 'ℹ' }}</span>
      <span class="toast-text">{{ toastMsg }}</span>
    </div>
  </transition>
</template>

<style scoped>
.app-toast {
  position: fixed;
  top: 18px;
  left: 50%;
  transform: translateX(-50%);
  z-index: 400;
  display: flex;
  align-items: flex-start;
  gap: 8px;
  max-width: min(520px, 86vw);
  padding: 10px 14px;
  border-radius: 10px;
  border: 1px solid var(--border);
  background: var(--bg-white);
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.12);
  font-size: 13px;
  line-height: 1.5;
  color: var(--text);
  cursor: pointer;
}

.app-toast.error {
  border-color: var(--danger);
}

.toast-icon {
  flex-shrink: 0;
  font-size: 14px;
  line-height: 1.5;
  color: var(--primary);
}

.app-toast.error .toast-icon {
  color: var(--danger);
}

.toast-text {
  min-width: 0;
  word-break: break-word;
  white-space: pre-wrap;
}

.toast-fade-enter-active,
.toast-fade-leave-active {
  transition: opacity 0.18s ease, transform 0.18s ease;
}

.toast-fade-enter-from,
.toast-fade-leave-to {
  opacity: 0;
  transform: translateX(-50%) translateY(-6px);
}
</style>
