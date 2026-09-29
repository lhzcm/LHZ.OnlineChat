<script setup lang="ts">
import { ref, onMounted } from 'vue'
import Avatar from '@/components/Avatar.vue'
import { robotApi } from '@/api/robot'
import { useGroupStore } from '@/stores/group'
import { useToast } from '@/composables/useToast'
import type { RobotInfo } from '@/types'

const props = defineProps<{ groupId: number }>()
const emit = defineEmits<{ close: []; added: [] }>()
const groupStore = useGroupStore()
const { toast } = useToast()

const robots = ref<RobotInfo[]>([])
const error = ref('')
const busy = ref(false)

onMounted(async () => {
  const res = await robotApi.getMyRobots()
  if (res.success && res.data) robots.value = res.data
})

async function addRobot(r: RobotInfo) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try {
    const res = await robotApi.addGroupRobot(props.groupId, r.userId)
    if (res.success) {
      emit('added')
      emit('close')
      toast(res.message)
    } else {
      error.value = res.message
    }
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="modal-overlay" @click.self="emit('close')">
    <div class="modal">
      <h3>添加机器人到群</h3>
      <div class="empty" v-if="robots.length === 0">
        <span class="empty-icon">🤖</span>
        <span>没有可添加的机器人，请先在「我的机器人」中创建</span>
      </div>
      <div v-for="r in robots" :key="r.id" class="request-item">
        <Avatar :name="r.name" :url="r.avatar" size="sm" />
        <div class="request-info">
          <span class="request-name">{{ r.name }} <span class="bot-tag">🤖</span></span>
          <span class="request-meta">账号 {{ r.userId }}</span>
        </div>
        <button class="btn btn-sm btn-primary kick-btn" :disabled="busy" @click="addRobot(r)">添加</button>
      </div>
      <p class="modal-error" v-if="error">{{ error }}</p>
      <button class="btn btn-ghost" @click="emit('close')">关闭</button>
    </div>
  </div>
</template>
