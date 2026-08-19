<script setup lang="ts">
import { ref, reactive, onMounted } from 'vue'
import Avatar from '@/components/Avatar.vue'
import RobotTestModal from './RobotTestModal.vue'
import { robotApi } from '@/api/robot'
import { useFriendStore } from '@/stores/friend'
import { useChatStore } from '@/stores/chat'
import { useToast } from '@/composables/useToast'
import type { RobotInfo } from '@/types'

const emit = defineEmits<{ close: [] }>()
const friendStore = useFriendStore()
const chatStore = useChatStore()
const { toast } = useToast()

const robots = ref<RobotInfo[]>([])
const robotEditing = ref(false)
const robotForm = reactive({ id: 0, name: '', webhookUrl: '', webhookSecret: '', timeoutMs: 10000, enabled: true })
const robotFormError = ref('')
const savingRobot = ref(false)

const showRobotTestModal = ref(false)
const robotTesting = ref<RobotInfo | null>(null)

onMounted(loadRobots)

async function loadRobots() {
  const res = await robotApi.getMyRobots()
  if (res.success && res.data) robots.value = res.data
}

function resetRobotForm() {
  robotForm.id = 0
  robotForm.name = ''
  robotForm.webhookUrl = ''
  robotForm.webhookSecret = ''
  robotForm.timeoutMs = 10000
  robotForm.enabled = true
}

function startCreateRobot() {
  robotFormError.value = ''
  resetRobotForm()
  robotEditing.value = true
}

function startEditRobot(r: RobotInfo) {
  robotFormError.value = ''
  robotForm.id = r.id
  robotForm.name = r.name
  robotForm.webhookUrl = r.webhookUrl
  robotForm.webhookSecret = r.webhookSecret || ''
  robotForm.timeoutMs = r.timeoutMs
  robotForm.enabled = r.enabled
  robotEditing.value = true
}

async function saveRobot() {
  robotFormError.value = ''
  savingRobot.value = true
  try {
    const data = {
      name: robotForm.name,
      webhookUrl: robotForm.webhookUrl,
      webhookSecret: robotForm.webhookSecret || null,
      timeoutMs: robotForm.timeoutMs,
      enabled: robotForm.enabled
    }
    const res = robotForm.id
      ? await robotApi.updateRobot(robotForm.id, data)
      : await robotApi.createRobot(data)
    if (res.success) {
      robotEditing.value = false
      await loadRobots()
      friendStore.fetchFriends()
      chatStore.fetchSessions()
      toast('机器人已保存')
    } else {
      robotFormError.value = res.message
    }
  } finally {
    savingRobot.value = false
  }
}

async function deleteRobot(r: RobotInfo) {
  if (!window.confirm(`确定删除机器人「${r.name}」？将同时解除好友关系并移出所有群。`)) return
  const res = await robotApi.deleteRobot(r.id)
  if (res.success) {
    robots.value = robots.value.filter(x => x.id !== r.id)
    friendStore.fetchFriends()
    chatStore.fetchSessions()
    toast('机器人已删除')
  } else {
    toast(res.message)
  }
}

function openRobotTest(r: RobotInfo) {
  robotTesting.value = r
  showRobotTestModal.value = true
}

/** 机器人推送调用链接：{当前站点}/api/robots/{令牌}/reply */
function robotPushUrl(token: string): string {
  return `${window.location.origin}/api/robots/${token}/reply`
}

/** 复制机器人调用链接 */
async function copyRobotToken(url: string) {
  try {
    await navigator.clipboard.writeText(url)
    toast('调用链接已复制')
  } catch {
    toast('复制失败，请手动选择复制')
  }
}
</script>

<template>
  <div class="modal-overlay" @click.self="emit('close')">
    <div class="modal modal-wide">
      <h3>🤖 我的机器人</h3>
      <p class="robot-tip">机器人收到消息会 POST 事件到 Webhook 地址，返回 <code>{"content":"回复"}</code> 即自动回复；<b>也可不配置 Webhook</b>，仅由第三方通过 <code>/api/robots/&#123;id&#125;/reply</code> 主动推送消息。</p>
      <button v-if="!robotEditing" class="btn btn-primary btn-sm" @click="startCreateRobot">+ 创建机器人</button>

      <!-- 创建/编辑表单 -->
      <div v-if="robotEditing" class="robot-form">
        <input v-model="robotForm.name" class="input" placeholder="机器人名称（如：小助手）" maxlength="50" />
        <input v-model="robotForm.webhookUrl" class="input" placeholder="Webhook 地址（可选，仅接收消息回调时需要）" />
        <input v-model="robotForm.webhookSecret" class="input" placeholder="签名密钥（可选，配置后第三方推送需验签；不配置仅靠令牌鉴权）" />
        <input v-model.number="robotForm.timeoutMs" class="input" type="number" min="1000" max="60000" placeholder="回调超时（毫秒，默认 10000）" />
        <label class="setting-switch">
          <span><span class="set-label">启用</span></span>
          <input type="checkbox" v-model="robotForm.enabled" />
        </label>
        <div class="modal-actions">
          <button class="btn btn-sm btn-primary" :disabled="savingRobot" @click="saveRobot">
            {{ savingRobot ? '保存中…' : '保存' }}
          </button>
          <button class="btn btn-sm btn-ghost" @click="robotEditing = false">取消</button>
        </div>
        <p class="modal-error" v-if="robotFormError">{{ robotFormError }}</p>
      </div>

      <div class="empty" v-if="!robotEditing && robots.length === 0">
        <span class="empty-icon">🤖</span>
        <span>还没有机器人，点击上方按钮创建一个</span>
      </div>
      <div v-for="r in robots" :key="r.id" class="request-item">
        <Avatar :name="r.name" :url="r.avatar" size="sm" />
        <div class="request-info">
          <span class="request-name">
            {{ r.name }}
            <span class="bot-tag">🤖</span>
            <span class="role-tag" :class="r.enabled ? 'role-0' : 'role-2'">{{ r.enabled ? '启用' : '停用' }}</span>
          </span>
          <span class="request-meta">账号 {{ r.userId }} · {{ r.webhookUrl || '纯推送（未配置 Webhook）' }}</span>
          <span class="robot-token-line" title="第三方推送调用链接：POST 该地址即可让机器人发消息">
            调用链接 <code>{{ robotPushUrl(r.token) }}</code>
            <button class="token-copy" @click="copyRobotToken(robotPushUrl(r.token))">复制</button>
          </span>
        </div>
        <button class="btn btn-sm btn-ghost kick-btn" @click="startEditRobot(r)">编辑</button>
        <button class="btn btn-sm btn-ghost kick-btn" @click="openRobotTest(r)">测试</button>
        <button class="btn btn-sm btn-ghost kick-btn" @click="deleteRobot(r)">删除</button>
      </div>
      <button class="btn btn-ghost" @click="emit('close')">关闭</button>
    </div>
  </div>

  <RobotTestModal v-if="showRobotTestModal && robotTesting" :robot="robotTesting" @close="showRobotTestModal = false" />
</template>

<style scoped>
.robot-tip {
  font-size: 12px;
  color: var(--text-secondary);
  margin: 4px 0 10px;
  line-height: 1.6;
}
.robot-tip code {
  background: var(--bg-hover);
  padding: 1px 5px;
  border-radius: 5px;
  font-size: 11px;
}
.robot-form {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin: 10px 0;
}
.robot-form .input {
  width: 100%;
}
</style>
