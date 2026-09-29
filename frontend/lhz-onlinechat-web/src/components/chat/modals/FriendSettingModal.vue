<script setup lang="ts">
import { ref, onMounted } from 'vue'
import Avatar from '@/components/Avatar.vue'
import { useFriendStore } from '@/stores/friend'
import { useChatStore } from '@/stores/chat'
import { useToast } from '@/composables/useToast'
import { copyText } from '@/utils/clipboard'
import { blacklistApi } from '@/api/blacklist'
import { robotApi } from '@/api/robot'
import type { FriendInfo } from '@/types'

const props = defineProps<{ friend: FriendInfo }>()
const emit = defineEmits<{ close: []; saved: [] }>()
const friendStore = useFriendStore()
const chatStore = useChatStore()
const { toast } = useToast()

const presetCategories = ['家人', '朋友', '同事', '同学', '客户', '其他']
const friendRemark = ref('')
const friendCategory = ref('')
const savingFriendTag = ref(false)
const friendTagError = ref('')
const friendTagSuccess = ref('')
const blockingFriend = ref(false)
const deletingFriendRobot = ref(false)
const friendRobotUrl = ref('')

onMounted(() => {
  friendRemark.value = props.friend.remark || ''
  friendCategory.value = props.friend.category || ''
  // 机器人：加载调用链接
  if (props.friend.isBot) {
    robotApi.getMyRobots().then(res => {
      const bot = res.data?.find(x => x.userId === props.friend.userId)
      if (bot) friendRobotUrl.value = `${window.location.origin}/api/robots/${bot.token}/reply`
    })
  }
})

async function saveFriendTag() {
  savingFriendTag.value = true
  friendTagError.value = ''
  friendTagSuccess.value = ''
  try {
    const remarkRes = await friendStore.setRemark(props.friend.userId, friendRemark.value)
    if (!remarkRes.success) {
      friendTagError.value = remarkRes.message
      return
    }
    const catRes = await friendStore.setCategory(props.friend.userId, friendCategory.value)
    if (!catRes.success) {
      friendTagError.value = catRes.message
      return
    }
    friendTagSuccess.value = '已保存'
    emit('saved') // 父级更新会话显示名
  } finally {
    savingFriendTag.value = false
  }
}

/** 拉黑好友（自动解除好友关系） */
async function blockFriend() {
  if (!window.confirm(`确定拉黑 ${props.friend.nickname}？拉黑后将自动解除好友关系，且对方无法再给你发消息和好友申请。`)) return
  blockingFriend.value = true
  friendTagError.value = ''
  try {
    const res = await blacklistApi.block(props.friend.userId)
    if (res.success) {
      emit('close')
      friendStore.fetchFriends()
      chatStore.fetchSessions()
      toast('已拉黑')
    } else {
      friendTagError.value = res.message
    }
  } finally {
    blockingFriend.value = false
  }
}

/** 删除机器人（机器人好友显示"删除机器人"而非"拉黑"） */
async function deleteFriendRobot() {
  if (!window.confirm(`确定删除机器人「${props.friend.nickname}」？将同时解除好友关系并移出所有群。`)) return
  deletingFriendRobot.value = true
  friendTagError.value = ''
  try {
    const list = await robotApi.getMyRobots()
    const robot = list.data?.find(r => r.userId === props.friend.userId)
    if (!robot) {
      friendTagError.value = '未找到该机器人配置，可能已被删除'
      return
    }
    const res = await robotApi.deleteRobot(robot.id)
    if (res.success) {
      emit('close')
      friendStore.fetchFriends()
      chatStore.fetchSessions()
      toast('机器人已删除')
    } else {
      friendTagError.value = res.message
    }
  } finally {
    deletingFriendRobot.value = false
  }
}

/** 复制机器人调用链接（Clipboard API + execCommand 降级，兼容 http 站点） */
async function copyFriendRobotUrl() {
  const ok = await copyText(friendRobotUrl.value)
  toast(ok ? '调用链接已复制' : '复制失败，请手动选择复制')
}
</script>

<template>
  <div class="modal-overlay" @click.self="emit('close')">
    <div class="modal">
      <h3>{{ props.friend.isBot ? '机器人设置' : '好友设置' }}</h3>
      <div class="friend-setting-head">
        <Avatar :name="props.friend.nickname" :url="props.friend.avatar" size="sm" />
        <div class="friend-setting-names">
          <span class="request-name">{{ props.friend.nickname }}</span>
          <span class="request-meta" v-if="props.friend.remark">当前备注：{{ props.friend.remark }}</span>
          <span v-if="!props.friend.isBot" class="request-meta">账号 {{ props.friend.userId }}</span>
        </div>
      </div>
      <!-- 机器人：展示第三方推送调用链接 -->
      <div v-if="props.friend.isBot && friendRobotUrl" class="robot-token-line robot-line-in-modal" title="第三方推送调用链接：POST 该地址即可让机器人发消息">
        <span class="robot-line-label">调用链接</span>
        <code>{{ friendRobotUrl }}</code>
        <button class="token-copy" @click="copyFriendRobotUrl">复制</button>
      </div>
      <label class="set-label">备注名</label>
      <input v-model="friendRemark" class="input" placeholder="给好友设置备注（留空显示对方昵称）" maxlength="50" />
      <label class="set-label">分类标签</label>
      <div class="cat-chips">
        <button v-for="c in presetCategories" :key="c"
          :class="['chip', { active: friendCategory === c }]"
          @click="friendCategory = c">{{ c }}</button>
        <button :class="['chip', { active: !friendCategory }]" @click="friendCategory = ''">未分组</button>
      </div>
      <input v-model="friendCategory" class="input" placeholder="或输入自定义分类" maxlength="30" />
      <button class="btn btn-primary" :disabled="savingFriendTag" @click="saveFriendTag">
        {{ savingFriendTag ? '保存中…' : '保 存' }}
      </button>
      <p class="modal-error" v-if="friendTagError">{{ friendTagError }}</p>
      <p class="modal-success" v-if="friendTagSuccess">{{ friendTagSuccess }}</p>
      <button class="btn btn-ghost" @click="emit('close')">关闭</button>
      <!-- 机器人：删除机器人；普通好友：拉黑 -->
      <button v-if="props.friend.isBot" class="btn btn-danger" @click="deleteFriendRobot" :disabled="deletingFriendRobot">
        {{ deletingFriendRobot ? '删除中…' : '删除机器人' }}
      </button>
      <button v-else class="btn btn-danger" @click="blockFriend" :disabled="blockingFriend">
        {{ blockingFriend ? '拉黑中…' : '拉黑该好友' }}
      </button>
    </div>
  </div>
</template>
