<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'

const props = defineProps<{ file: File }>()
const emit = defineEmits<{ cropped: [blob: Blob]; cancel: [] }>()

// 画布逻辑分辨率（512×512 方形，导出即头像尺寸）
const CANVAS_SIZE = 512
const canvasRef = ref<HTMLCanvasElement | null>(null)
const wrapRef = ref<HTMLElement | null>(null)

const img = ref<HTMLImageElement | null>(null)
const scale = ref(1)
const offsetX = ref(0)
const offsetY = ref(0)
const dragging = ref(false)
const lastX = ref(0)
const lastY = ref(0)
const loadError = ref('')
const exporting = ref(false)
let coverScale = 1

/** 当前图片在画布上的缩放后尺寸 */
function scaledSize() {
  if (!img.value) return { w: 0, h: 0 }
  return { w: img.value.naturalWidth * scale.value, h: img.value.naturalHeight * scale.value }
}

/** 限制图片偏移：画布必须被图片完全覆盖（不露出空白） */
function clampOffset() {
  const { w, h } = scaledSize()
  offsetX.value = Math.min(0, Math.max(CANVAS_SIZE - w, offsetX.value))
  offsetY.value = Math.min(0, Math.max(CANVAS_SIZE - h, offsetY.value))
}

function draw() {
  const canvas = canvasRef.value
  if (!canvas || !img.value) return
  const ctx = canvas.getContext('2d')
  if (!ctx) return
  ctx.clearRect(0, 0, CANVAS_SIZE, CANVAS_SIZE)
  // 绘制图片（画布即裁剪框：导出整个画布内容）
  ctx.drawImage(img.value, offsetX.value, offsetY.value, scaledSize().w, scaledSize().h)
  // 裁剪框边框（提示区域）
  ctx.strokeStyle = 'rgba(255,255,255,0.9)'
  ctx.lineWidth = 2
  ctx.strokeRect(0.5, 0.5, CANVAS_SIZE - 1, CANVAS_SIZE - 1)
}

function onWheel(e: WheelEvent) {
  e.preventDefault()
  const factor = e.deltaY < 0 ? 1.1 : 0.9
  setScale(scale.value * factor)
}

function setScale(next: number) {
  // 限制缩放范围：最小为 cover（填满画布），最大 6 倍
  scale.value = Math.min(6, Math.max(coverScale, next))
  clampOffset()
  draw()
}

/** 滑条值（0~100）↔ 缩放比例（coverScale~6）映射 */
function sliderValue(): number {
  return Math.round(((scale.value - coverScale) / (6 - coverScale)) * 100)
}

function setScaleFromSlider(v: number) {
  setScale(coverScale + (v / 100) * (6 - coverScale))
}

function onPointerDown(e: PointerEvent) {
  if (e.button !== 0) return
  dragging.value = true
  lastX.value = e.clientX
  lastY.value = e.clientY
  ;(e.target as HTMLElement).setPointerCapture?.(e.pointerId)
}

function onPointerMove(e: PointerEvent) {
  if (!dragging.value) return
  offsetX.value += e.clientX - lastX.value
  offsetY.value += e.clientY - lastY.value
  lastX.value = e.clientX
  lastY.value = e.clientY
  clampOffset()
  draw()
}

function onPointerUp() {
  dragging.value = false
}

/** 导出裁剪结果：512×512 PNG Blob 并上传 */
async function confirmCrop() {
  if (!canvasRef.value || exporting.value) return
  exporting.value = true
  try {
    const blob = await new Promise<Blob | null>((resolve) =>
      canvasRef.value!.toBlob(resolve, 'image/png')
    )
    if (!blob) {
      loadError.value = '裁剪失败，请重试'
      return
    }
    emit('cropped', blob)
  } finally {
    exporting.value = false
  }
}

onMounted(() => {
  const reader = new FileReader()
  reader.onload = () => {
    const image = new Image()
    image.onload = () => {
      img.value = image
      // 初始缩放：cover（图片最小边填满画布），居中
      coverScale = CANVAS_SIZE / Math.min(image.naturalWidth, image.naturalHeight)
      scale.value = coverScale
      offsetX.value = (CANVAS_SIZE - image.naturalWidth * scale.value) / 2
      offsetY.value = (CANVAS_SIZE - image.naturalHeight * scale.value) / 2
      draw()
    }
    image.onerror = () => { loadError.value = '图片加载失败，请更换图片' }
    image.src = String(reader.result)
  }
  reader.onerror = () => { loadError.value = '文件读取失败，请重试' }
  reader.readAsDataURL(props.file)

  document.addEventListener('wheel', onWheel, { passive: false })
})

onUnmounted(() => {
  document.removeEventListener('wheel', onWheel)
})
</script>

<template>
  <div class="modal-overlay" @click.self="emit('cancel')">
    <div class="modal crop-modal">
      <h3>裁剪头像</h3>
      <p class="crop-desc">拖动图片调整位置，滚轮或滑块缩放</p>

      <div class="crop-wrap" ref="wrapRef"
        @pointerdown="onPointerDown" @pointermove="onPointerMove" @pointerup="onPointerUp" @pointerleave="onPointerUp">
        <canvas ref="canvasRef" :width="CANVAS_SIZE" :height="CANVAS_SIZE" class="crop-canvas"></canvas>
        <span class="crop-drag-hint" v-if="!dragging">↕ 拖动调整位置</span>
      </div>

      <div class="crop-zoom-row">
        <span class="zoom-icon">🔍−</span>
        <input type="range" class="crop-zoom" min="0" max="100" :value="sliderValue()" @input="e => setScaleFromSlider(Number((e.target as HTMLInputElement).value))" />
        <span class="zoom-icon">🔍+</span>
      </div>

      <p class="modal-error" v-if="loadError">{{ loadError }}</p>
      <div class="crop-btns">
        <button class="btn btn-ghost" @click="emit('cancel')">取消</button>
        <button class="btn btn-primary" :disabled="exporting || !img || !!loadError" @click="confirmCrop">
          {{ exporting ? '处理中…' : '使用此头像' }}
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.crop-modal {
  width: 380px;
  max-width: calc(100vw - 40px);
}

.crop-desc {
  font-size: 12.5px;
  color: var(--text-secondary);
  margin-bottom: 12px;
  text-align: center;
}

.crop-wrap {
  position: relative;
  width: 300px;
  height: 300px;
  margin: 0 auto;
  border-radius: 12px;
  overflow: hidden;
  cursor: grab;
  background: #111;
  user-select: none;
  touch-action: none;
}

.crop-wrap:active {
  cursor: grabbing;
}

.crop-canvas {
  width: 300px;
  height: 300px;
  display: block;
}

.crop-drag-hint {
  position: absolute;
  bottom: 10px;
  left: 50%;
  transform: translateX(-50%);
  font-size: 11px;
  color: rgba(255, 255, 255, 0.85);
  background: rgba(0, 0, 0, 0.5);
  padding: 2px 10px;
  border-radius: 20px;
  pointer-events: none;
}

.crop-zoom-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 14px 0 6px;
}

.crop-zoom {
  flex: 1;
  accent-color: var(--primary);
}

.zoom-icon {
  font-size: 13px;
  color: var(--text-secondary);
}

.crop-btns {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 10px;
}
</style>
