<template>
  <!-- 有真实头像时显示图片，否则显示渐变首字母 -->
  <span v-if="url" class="avatar-photo" :class="sizeClass">
    <img :src="url" alt="" />
  </span>
  <span v-else class="avatar" :class="sizeClass" :style="{ background: avatarGradient(name) }">
    {{ avatarInitial(name) }}
  </span>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { avatarGradient, avatarInitial } from '@/utils/avatar'

const props = defineProps<{
  name: string
  url?: string | null
  size?: 'sm' | 'lg'
}>()

const sizeClass = computed(() =>
  props.size === 'lg' ? 'avatar-lg' : props.size === 'sm' ? 'avatar-sm' : ''
)
</script>

<style scoped>
.avatar {
  width: 44px;
  height: 44px;
  border-radius: 50%;
  color: white;
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: 600;
  font-size: 18px;
  flex-shrink: 0;
  user-select: none;
  box-shadow: inset 0 -2px 6px rgba(0, 0, 0, 0.08);
}

.avatar-sm {
  width: 38px;
  height: 38px;
  font-size: 15px;
}

.avatar-lg {
  width: 72px;
  height: 72px;
  font-size: 28px;
}

.avatar-photo {
  width: 44px;
  height: 44px;
  border-radius: 50%;
  overflow: hidden;
  flex-shrink: 0;
  display: flex;
  background: var(--bg-hover);
}

.avatar-photo.avatar-sm {
  width: 38px;
  height: 38px;
}

.avatar-photo.avatar-lg {
  width: 72px;
  height: 72px;
}

.avatar-photo img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
</style>
