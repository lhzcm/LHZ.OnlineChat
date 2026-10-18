<script setup lang="ts">
/**
 * 图片消息体：只负责渲染与把点击抛给父级。
 * 多选模式下点击要切换选中而不是放大，这个判断留在父级（MessageBubble），
 * 否则组件要认识"多选"这个概念，职责就跑偏了。
 */
defineProps<{ src: string }>()
const emit = defineEmits<{ click: [] }>()
</script>

<template>
  <img :src="src" class="msg-image" alt="图片" loading="lazy" @click.stop="emit('click')" />
</template>

<style scoped>
.msg-image {
  max-width: 260px;
  max-height: 260px;
  border-radius: 10px;
  display: block;
  cursor: pointer;
}
</style>