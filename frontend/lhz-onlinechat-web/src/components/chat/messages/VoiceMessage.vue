<script setup lang="ts">
import { computed, onUnmounted, ref } from 'vue'
import { claimVoicePlayback, releaseVoicePlayback } from '@/composables/useVoicePlayback'
import { formatVoiceDuration } from '@/utils/voice'

/**
 * 语音消息体：播放/暂停 + 进度条 + 时长。
 *
 * 时长不落库（content 只有地址），从 `<audio>` 的元数据读：`preload="metadata"` 只加载头部，
 * 不会因为一条语音把整段音频拉下来；元数据回来之前显示 `--:--`。
 */
const props = defineProps<{ src: string }>()

const audioEl = ref<HTMLAudioElement | null>(null)
const playing = ref(false)
const duration = ref(0)
const current = ref(0)

const progress = computed(() => (
  duration.value > 0 ? Math.min(100, (current.value / duration.value) * 100) : 0
))

function onLoadedMetadata() {
  const d = audioEl.value?.duration
  if (d && Number.isFinite(d)) duration.value = d
}

function onTimeUpdate() {
  current.value = audioEl.value?.currentTime || 0
}

function onPause() {
  playing.value = false
  if (audioEl.value) releaseVoicePlayback(audioEl.value)
}

function onEnded() {
  playing.value = false
  current.value = 0
  if (audioEl.value) releaseVoicePlayback(audioEl.value)
}

async function toggle() {
  const el = audioEl.value
  if (!el) return
  if (playing.value) {
    el.pause()
    return
  }
  claimVoicePlayback(el)
  try {
    await el.play()
    playing.value = true
  } catch {
    // 自动播放策略 / 加载失败：保持暂停态，不弹错误打断聊天
    playing.value = false
  }
}

onUnmounted(() => {
  audioEl.value?.pause()
})
</script>

<template>
  <div class="msg-voice">
    <button type="button" class="voice-btn" :class="{ playing }" @click.stop="toggle"
      :title="playing ? '暂停' : '播放'">
      <svg v-if="!playing" viewBox="0 0 24 24" width="18" height="18" fill="currentColor">
        <path d="M8 5v14l11-7z" />
      </svg>
      <svg v-else viewBox="0 0 24 24" width="18" height="18" fill="currentColor">
        <rect x="7" y="5" width="4" height="14" rx="1" />
        <rect x="14" y="5" width="4" height="14" rx="1" />
      </svg>
    </button>
    <span class="voice-bar" :class="{ playing }">
      <span class="voice-progress" :style="{ width: progress + '%' }"></span>
    </span>
    <span class="voice-duration">{{ formatVoiceDuration(duration) }}</span>
    <audio ref="audioEl" :src="props.src" preload="metadata" @loadedmetadata="onLoadedMetadata"
      @timeupdate="onTimeUpdate" @pause="onPause" @ended="onEnded"></audio>
  </div>
</template>

<style scoped>
.msg-voice {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 180px;
  max-width: 260px;
}

.voice-btn {
  width: 30px;
  height: 30px;
  flex-shrink: 0;
  border: none;
  border-radius: 50%;
  background: var(--primary);
  color: #fff;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  transition: opacity 0.15s;
}

.voice-btn:hover {
  opacity: 0.88;
}

.voice-bar {
  position: relative;
  flex: 1;
  height: 4px;
  border-radius: 2px;
  background: var(--border);
  overflow: hidden;
}

.voice-progress {
  position: absolute;
  inset: 0 auto 0 0;
  background: var(--primary);
  transition: width 0.12s linear;
}

.voice-duration {
  font-size: 12px;
  color: var(--text-secondary);
  font-variant-numeric: tabular-nums;
  flex-shrink: 0;
}

audio {
  display: none;
}
</style>
