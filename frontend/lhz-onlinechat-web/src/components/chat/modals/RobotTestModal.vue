<script setup lang="ts">
import { ref } from 'vue'
import { robotApi } from '@/api/robot'
import type { RobotInfo, RobotTestResult } from '@/types'

const props = defineProps<{ robot: RobotInfo }>()
const emit = defineEmits<{ close: [] }>()

const content = ref('')
const testing = ref(false)
const result = ref<RobotTestResult | null>(null)

async function runTest() {
  if (testing.value) return
  testing.value = true
  result.value = null
  try {
    const res = await robotApi.testRobot(props.robot.id, content.value || '你好')
    if (res.success && res.data) {
      result.value = res.data
    } else {
      result.value = { success: false, reply: null, message: res.message }
    }
  } catch (e: any) {
    result.value = { success: false, reply: null, message: e?.message || '测试失败' }
  } finally {
    testing.value = false
  }
}
</script>

<template>
  <div class="modal-overlay" @click.self="emit('close')">
    <div class="modal">
      <h3>测试 · {{ robot.name }}</h3>
      <p class="robot-tip">模拟一条私聊消息触发 Webhook，展示机器人同步回复。</p>
      <input v-model="content" class="input" placeholder="模拟用户发送的内容" @keydown.enter="runTest" />
      <button class="btn btn-primary" :disabled="testing" @click="runTest">
        {{ testing ? '测试中…' : '发送测试' }}
      </button>
      <div v-if="result" class="robot-test-result" :class="{ fail: !result.success }">
        <template v-if="result.success">
          <p v-if="result.reply">🤖 回复：{{ result.reply }}</p>
          <p v-else>已触发，机器人未返回回复</p>
        </template>
        <p v-else>❌ {{ result.message }}</p>
      </div>
      <button class="btn btn-ghost" @click="emit('close')">关闭</button>
    </div>
  </div>
</template>

<style scoped>
.robot-tip {
  font-size: 12px;
  color: var(--text-secondary);
  margin: 4px 0 10px;
  line-height: 1.6;
}
.robot-test-result {
  margin-top: 12px;
  padding: 10px 12px;
  border-radius: 10px;
  background: var(--bg-hover);
  font-size: 13px;
  color: var(--text);
  word-break: break-word;
}
.robot-test-result.fail {
  color: var(--danger);
}
</style>
