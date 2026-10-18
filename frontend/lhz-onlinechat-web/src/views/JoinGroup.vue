<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Avatar from '@/components/Avatar.vue'
import { useGroupStore } from '@/stores/group'
import { useToast } from '@/composables/useToast'
import { isValidInviteToken, parseInviteToken } from '@/utils/inviteLink'
import type { GroupInvitePreview } from '@/types'

/**
 * 群邀请页（扫码/点链接进入）。
 *
 * 必须登录才能看到群资料：匿名接口会把群名/头像/人数暴露给任何拿到链接的人，
 * 而链接本身就是"持有即可申请"的凭证，没必要再开一个匿名读口。
 * 未登录时由路由守卫带去 /login?redirect=/join/<token>，登录后回跳到这里。
 */
const route = useRoute()
const router = useRouter()
const groupStore = useGroupStore()
const { toast } = useToast()

const token = ref('')
const loading = ref(true)
const submitting = ref(false)
const error = ref('')
const preview = ref<GroupInvitePreview | null>(null)
const message = ref('')
/** 提交成功后进入"等待审批"态（不依赖刷新页面） */
const submitted = ref(false)

async function load() {
  const fromPath = parseInviteToken(route.fullPath)
  const fromParams = String(route.params.token || '')
  token.value = fromPath || (isValidInviteToken(fromParams) ? fromParams : '')

  if (!token.value) {
    error.value = '邀请链接格式不正确'
    loading.value = false
    return
  }

  const res = await groupStore.fetchInvitePreview(token.value)
  if (!res.success || !res.data) {
    error.value = res.message || '邀请链接已失效，请让群主重新生成'
  } else {
    preview.value = res.data
  }
  loading.value = false
}

async function submit() {
  if (!token.value || submitting.value) return
  submitting.value = true
  error.value = ''
  const res = await groupStore.joinByToken(token.value, message.value.trim() || undefined)
  if (!res.ok) {
    error.value = res.message || '操作失败，请稍后重试'
  } else if (res.data?.joined) {
    toast(`已加入群「${res.data.groupName}」`)
    router.push('/chat')
  } else {
    submitted.value = true
    toast('已提交申请，请等待管理员审批')
  }
  submitting.value = false
}

/** 进入群聊：回到聊天页，由侧边栏选择会话 */
function goChat() {
  router.push('/chat')
}

onMounted(load)
</script>

<template>
  <div class="join-page">
    <div class="join-card">
      <div class="join-loading" v-if="loading">正在读取邀请信息…</div>

      <template v-else-if="preview">
        <div class="join-head">
          <Avatar :name="preview.name" :url="preview.avatar" size="lg" />
          <h2 class="join-name">{{ preview.name }}</h2>
          <p class="join-meta">{{ preview.memberCount }} 人</p>
        </div>

        <p class="join-tip" v-if="preview.alreadyMember">你已经是该群成员。</p>
        <p class="join-tip" v-else-if="preview.hasPendingRequest || submitted">
          申请已提交，等待管理员审批。通过后会出现在你的会话列表里。
        </p>
        <p class="join-tip" v-else-if="preview.requiresApproval">
          加入需要管理员审批，通过后即可参与聊天。
        </p>
        <p class="join-tip" v-else>点击下方按钮直接加入群聊。</p>

        <div class="join-form" v-if="!preview.alreadyMember && !preview.hasPendingRequest && !submitted">
          <input v-if="preview.requiresApproval" v-model="message" class="join-input" maxlength="100"
            placeholder="申请附言（可选，最多 100 字）" />
          <button class="btn btn-primary join-btn" :disabled="submitting" @click="submit">
            {{ preview.requiresApproval ? '申请加入' : '加入群聊' }}
          </button>
        </div>

        <button v-if="preview.alreadyMember" class="btn btn-primary join-btn" @click="goChat">进入群聊</button>

        <p class="join-error" v-if="error">{{ error }}</p>
      </template>

      <template v-else>
        <p class="join-error join-error-block">{{ error }}</p>
      </template>

      <button class="btn btn-ghost join-back" @click="goChat">返回聊天</button>
    </div>
  </div>
</template>

<style scoped>
.join-page {
  height: 100vh;
  height: 100dvh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 20px;
  background: linear-gradient(135deg, #5b6cff 0%, #764ba2 100%);
}

.join-card {
  width: 100%;
  max-width: 380px;
  padding: 26px 22px;
  border-radius: 16px;
  background: var(--bg-white, #fff);
  box-shadow: 0 18px 48px rgba(0, 0, 0, 0.18);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
}

.join-loading {
  padding: 12px 0;
  font-size: 13px;
  color: var(--text-secondary);
}

.join-head {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
}

.join-name {
  margin: 0;
  font-size: 18px;
  color: var(--text);
  text-align: center;
  word-break: break-word;
}

.join-meta {
  margin: 0;
  font-size: 12.5px;
  color: var(--text-secondary);
}

.join-tip {
  margin: 4px 0 0;
  font-size: 13px;
  line-height: 1.6;
  color: var(--text-secondary);
  text-align: center;
}

.join-form {
  width: 100%;
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: 6px;
}

.join-input {
  width: 100%;
  padding: 9px 12px;
  border: 1px solid var(--border);
  border-radius: 10px;
  background: var(--chat-bg, #f7f8fa);
  font-size: 13.5px;
  color: var(--text);
}

.join-btn {
  width: 100%;
  padding: 10px 16px;
  border-radius: 10px;
}

.join-error {
  margin: 4px 0 0;
  font-size: 12.5px;
  color: var(--danger);
  text-align: center;
}

.join-error-block {
  padding: 10px 0;
}

.join-back {
  margin-top: 4px;
  font-size: 13px;
}
</style>
