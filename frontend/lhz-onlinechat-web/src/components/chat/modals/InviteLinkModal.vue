<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import QRCode from 'qrcode'
import { useGroupStore } from '@/stores/group'
import { useToast } from '@/composables/useToast'
import { copyText } from '@/utils/clipboard'
import { buildInviteUrl } from '@/utils/inviteLink'

/**
 * 群邀请链接：生成 / 复制 / 二维码 / 有效期 / 关闭 / 「需要审批」开关。
 *
 * 链接在本机拼：服务端只存 token，不知道用户是从域名还是局域网 IP 访问的，
 * 用当前 origin 拼出来的链接（和二维码）才能在扫码端真正打得开。
 */
const props = defineProps<{ groupId: number; groupName: string }>()
const emit = defineEmits<{ close: [] }>()

const groupStore = useGroupStore()
const { toast } = useToast()

const loading = ref(true)
const busy = ref(false)
const error = ref('')
const canvasEl = ref<HTMLCanvasElement | null>(null)

const link = computed(() => groupStore.inviteLink)
const inviteUrl = computed(() => (
  link.value?.token ? buildInviteUrl(window.location.origin, link.value.token) : ''
))

const expiresText = computed(() => {
  const value = link.value?.expiresAt
  if (!value) return ''
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '' : date.toLocaleString()
})

/** 把链接画成二维码；链接没变就不重画（避免每次渲染都重排 canvas） */
let lastRenderedUrl = ''
async function renderQr() {
  const url = inviteUrl.value
  if (!url) {
    lastRenderedUrl = ''
    return
  }
  if (url === lastRenderedUrl) return

  await nextTick()
  if (!canvasEl.value) return
  try {
    await QRCode.toCanvas(canvasEl.value, url, { width: 180, margin: 1 })
    lastRenderedUrl = url
  } catch {
    error.value = '二维码生成失败，可以直接复制链接'
  }
}

async function load() {
  loading.value = true
  error.value = ''
  const res = await groupStore.fetchInviteLink(props.groupId)
  if (!res.success) error.value = res.message || '读取邀请链接失败'
  loading.value = false
  await renderQr()
}

watch(inviteUrl, () => { void renderQr() })

async function generate() {
  busy.value = true
  error.value = ''
  const res = await groupStore.createInviteLink(props.groupId)
  if (!res.success) error.value = res.message || '生成邀请链接失败'
  else toast('邀请链接已生成')
  busy.value = false
  await renderQr()
}

async function disable() {
  busy.value = true
  error.value = ''
  const res = await groupStore.disableInviteLink(props.groupId)
  if (!res.success) error.value = res.message || '关闭邀请链接失败'
  else toast('邀请链接已关闭')
  busy.value = false
}

async function copyLink() {
  const ok = await copyText(inviteUrl.value)
  toast(ok ? '邀请链接已复制' : '复制失败，请手动选择链接复制', ok ? 'info' : 'error')
}

async function toggleApproval() {
  const next = !link.value?.requiresApproval
  busy.value = true
  const res = await groupStore.setJoinApproval(props.groupId, next)
  if (!res.success) error.value = res.message || '修改审批开关失败'
  else toast(next ? '凭链接入群需要审批' : '凭链接入群可直接进入')
  busy.value = false
}

onMounted(load)
</script>

<template>
  <div class="modal-overlay" role="dialog" aria-modal="true" @click.self="emit('close')">
    <div class="modal invite-link-modal">
      <h3>邀请链接<span class="link-group">{{ groupName }}</span></h3>

      <p class="link-hint">
        链接 7 天后自动失效；重置或关闭后旧链接立即失效。扫码的人登录后即可查看群信息。
      </p>

      <div class="link-loading" v-if="loading">加载中…</div>

      <template v-else>
        <div class="link-empty" v-if="!inviteUrl">
          <p>还没有开启邀请链接。</p>
          <button class="btn btn-primary" :disabled="busy" @click="generate">生成邀请链接</button>
        </div>

        <template v-else>
          <div class="link-box">
            <input class="link-input" :value="inviteUrl" readonly @focus="($event.target as HTMLInputElement).select()" />
            <button class="btn btn-primary btn-sm" :disabled="busy" @click="copyLink">复制</button>
          </div>

          <div class="link-qr">
            <canvas ref="canvasEl"></canvas>
            <p class="link-qr-hint">用手机扫描二维码加入</p>
          </div>

          <p class="link-expire" v-if="expiresText">有效期至 {{ expiresText }}</p>

          <label class="link-approval">
            <input type="checkbox" :checked="link?.requiresApproval !== false" :disabled="busy"
              @change="toggleApproval" />
            <span>凭链接入群需要管理员审批（关闭后凭链接直接进群）</span>
          </label>

          <div class="link-actions">
            <button class="btn btn-ghost btn-sm" :disabled="busy" @click="generate">重置链接</button>
            <button class="btn btn-ghost btn-sm" :disabled="busy" @click="disable">关闭链接</button>
          </div>
        </template>
      </template>

      <p class="modal-error" v-if="error">{{ error }}</p>

      <button class="btn btn-ghost" @click="emit('close')">关闭</button>
    </div>
  </div>
</template>

<style scoped>
.invite-link-modal {
  max-width: 440px;
}

.link-group {
  margin-left: 8px;
  font-size: 13px;
  font-weight: normal;
  color: var(--text-secondary);
}

.link-hint {
  margin: 0 0 12px;
  font-size: 12px;
  line-height: 1.6;
  color: var(--text-secondary);
}

.link-loading,
.link-empty {
  padding: 18px 0;
  text-align: center;
  color: var(--text-secondary);
  font-size: 13px;
}

.link-box {
  display: flex;
  align-items: center;
  gap: 8px;
}

.link-input {
  flex: 1;
  min-width: 0;
  padding: 8px 10px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--chat-bg);
  font-size: 12.5px;
  color: var(--text);
}

.link-qr {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  margin: 14px 0 8px;
}

.link-qr canvas {
  border: 1px solid var(--border);
  border-radius: 8px;
  background: #fff;
}

.link-qr-hint {
  margin: 0;
  font-size: 12px;
  color: var(--text-secondary);
}

.link-expire {
  margin: 0 0 10px;
  font-size: 12px;
  color: var(--text-secondary);
  text-align: center;
}

.link-approval {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  font-size: 12.5px;
  color: var(--text);
  cursor: pointer;
}

.link-approval input {
  margin-top: 2px;
}

.link-actions {
  display: flex;
  gap: 8px;
  margin: 12px 0 4px;
}
</style>
