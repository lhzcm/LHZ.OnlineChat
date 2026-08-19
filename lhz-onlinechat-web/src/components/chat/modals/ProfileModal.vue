<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import Avatar from '@/components/Avatar.vue'
import { useAuthStore } from '@/stores/auth'
import { authApi } from '@/api/auth'
import AvatarCropModal from './AvatarCropModal.vue'

const props = defineProps<{ notifySoundEnabled: boolean }>()
const emit = defineEmits<{
  close: []
  openBlacklist: []
  openSessions: []
  'update:notifySoundEnabled': [value: boolean]
}>()

const auth = useAuthStore()

const profileNickname = ref('')
const newEmail = ref('')
const emailCode = ref('')
const savingProfile = ref(false)
const savingEmail = ref(false)
const sendingEmailCode = ref(false)
const emailCounting = ref(0)
const showEmailEdit = ref(false)
const showPasswordEdit = ref(false)
const oldPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const savingPassword = ref(false)
const profileError = ref('')
const profileSuccess = ref('')
const avatarInputRef = ref<HTMLInputElement | null>(null)
// 头像裁剪：选中图片后先裁剪再上传
const cropFile = ref<File | null>(null)
const uploadingAvatar = ref(false)
let emailCountTimer: number | null = null

onMounted(() => {
  profileNickname.value = auth.user?.nickname || ''
  newEmail.value = ''
  emailCode.value = ''
  showEmailEdit.value = false
  showPasswordEdit.value = false
  oldPassword.value = ''
  newPassword.value = ''
  confirmPassword.value = ''
  profileError.value = ''
  profileSuccess.value = ''
})

onUnmounted(() => {
  if (emailCountTimer) clearInterval(emailCountTimer)
})

function triggerAvatarInput() {
  avatarInputRef.value?.click()
}

/** 选择图片：校验后打开裁剪弹窗（不直接上传） */
function onAvatarChange(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (file.size > 5 * 1024 * 1024) {
    profileError.value = '图片大小不能超过 5MB'
    return
  }
  if (!file.type.startsWith('image/')) {
    profileError.value = '请选择图片文件'
    return
  }
  profileError.value = ''
  profileSuccess.value = ''
  cropFile.value = file
}

/** 裁剪完成：上传裁剪后的 PNG */
async function onCropped(blob: Blob) {
  cropFile.value = null
  uploadingAvatar.value = true
  profileError.value = ''
  profileSuccess.value = ''
  try {
    const file = new File([blob], 'avatar.png', { type: 'image/png' })
    const res = await auth.uploadAvatar(file)
    if (res.success) profileSuccess.value = res.message || '头像修改成功'
    else profileError.value = res.message
  } catch (err: any) {
    profileError.value = err?.message || '头像上传失败'
  } finally {
    uploadingAvatar.value = false
  }
}

async function saveNickname() {
  if (!profileNickname.value.trim()) {
    profileError.value = '昵称不能为空'
    return
  }
  savingProfile.value = true
  profileError.value = ''
  profileSuccess.value = ''
  try {
    const res = await auth.updateProfile(profileNickname.value)
    if (res.success) profileSuccess.value = res.message
    else profileError.value = res.message
  } finally {
    savingProfile.value = false
  }
}

function startEmailCountdown() {
  emailCounting.value = 60
  emailCountTimer = window.setInterval(() => {
    emailCounting.value--
    if (emailCounting.value <= 0 && emailCountTimer) {
      clearInterval(emailCountTimer)
      emailCountTimer = null
    }
  }, 1000)
}

async function sendEmailCode() {
  if (!newEmail.value || emailCounting.value > 0 || sendingEmailCode.value) return
  sendingEmailCode.value = true
  profileError.value = ''
  profileSuccess.value = ''
  try {
    const res = await authApi.sendCode({ email: newEmail.value.trim() })
    if (res.success) {
      profileSuccess.value = `验证码已发送至 ${newEmail.value.trim()}，5 分钟内有效`
      startEmailCountdown()
    } else {
      profileError.value = res.message
    }
  } catch (err: any) {
    profileError.value = err?.message || '验证码发送失败'
  } finally {
    sendingEmailCode.value = false
  }
}

async function saveEmail() {
  if (!newEmail.value || !emailCode.value) return
  savingEmail.value = true
  profileError.value = ''
  profileSuccess.value = ''
  try {
    const res = await auth.updateEmail(newEmail.value.trim(), emailCode.value.trim())
    if (res.success) {
      profileSuccess.value = res.message
      showEmailEdit.value = false
    } else {
      profileError.value = res.message
    }
  } finally {
    savingEmail.value = false
  }
}

async function savePassword() {
  if (newPassword.value.length < 6) {
    profileError.value = '新密码长度不能少于 6 位'
    return
  }
  if (newPassword.value !== confirmPassword.value) {
    profileError.value = '两次输入的新密码不一致'
    return
  }
  savingPassword.value = true
  profileError.value = ''
  profileSuccess.value = ''
  try {
    const res = await authApi.changePassword(oldPassword.value, newPassword.value)
    if (res.success) {
      profileSuccess.value = res.message
      showPasswordEdit.value = false
      oldPassword.value = ''
      newPassword.value = ''
      confirmPassword.value = ''
    } else {
      profileError.value = res.message
    }
  } finally {
    savingPassword.value = false
  }
}
</script>

<template>
  <div class="modal-overlay" @click.self="emit('close')">
    <div class="modal">
      <h3>个人信息</h3>
      <div class="profile-avatar">
        <Avatar :name="auth.user?.nickname || ''" :url="auth.user?.avatar" size="lg" />
        <button class="btn btn-sm btn-ghost" :disabled="uploadingAvatar" @click="triggerAvatarInput">
          {{ uploadingAvatar ? '上传中…' : '更换头像' }}
        </button>
        <input ref="avatarInputRef" type="file" accept="image/*" class="hidden-file" @change="onAvatarChange" />
      </div>
      <div class="profile-row">
        <span class="profile-label">账号 ID</span>
        <span class="profile-value">{{ auth.user?.id }}</span>
      </div>
      <div class="profile-row">
        <span class="profile-label">昵称</span>
        <div class="profile-edit">
          <input v-model="profileNickname" class="input" maxlength="50" />
          <button class="btn btn-sm btn-primary" :disabled="savingProfile" @click="saveNickname">保存</button>
        </div>
      </div>
      <div class="profile-row">
        <span class="profile-label">邮箱</span>
        <div class="profile-edit">
          <span class="profile-value profile-email">{{ auth.user?.email }}</span>
          <button class="btn btn-sm btn-ghost" @click="showEmailEdit = !showEmailEdit">
            {{ showEmailEdit ? '取消' : '修改' }}
          </button>
        </div>
      </div>
      <template v-if="showEmailEdit">
        <div class="profile-edit email-edit">
          <input v-model="newEmail" class="input" type="email" placeholder="新邮箱地址" />
          <div class="email-code-row">
            <input v-model="emailCode" class="input" placeholder="6 位验证码" inputmode="numeric" maxlength="6" />
            <button class="btn btn-sm code-btn" :disabled="emailCounting > 0 || !newEmail || sendingEmailCode" @click="sendEmailCode">
              {{ sendingEmailCode ? '发送中…' : emailCounting > 0 ? `${emailCounting}s 后重发` : '获取验证码' }}
            </button>
          </div>
          <button class="btn btn-sm btn-primary" :disabled="!newEmail || !emailCode || savingEmail" @click="saveEmail">
            {{ savingEmail ? '保存中…' : '确认修改' }}
          </button>
        </div>
      </template>
      <div class="profile-row">
        <span class="profile-label">密码</span>
        <div class="profile-edit">
          <span class="profile-value profile-email">••••••••</span>
          <button class="btn btn-sm btn-ghost" @click="showPasswordEdit = !showPasswordEdit">
            {{ showPasswordEdit ? '取消' : '修改' }}
          </button>
        </div>
      </div>
      <template v-if="showPasswordEdit">
        <div class="profile-edit email-edit">
          <input v-model="oldPassword" class="input" type="password" placeholder="原密码" />
          <input v-model="newPassword" class="input" type="password" placeholder="新密码（至少 6 位）" />
          <input v-model="confirmPassword" class="input" type="password" placeholder="确认新密码" />
          <button class="btn btn-sm btn-primary" :disabled="!oldPassword || !newPassword || savingPassword" @click="savePassword">
            {{ savingPassword ? '保存中…' : '确认修改' }}
          </button>
        </div>
      </template>
      <label class="setting-switch">
        <span>
          <span class="set-label">消息提示音</span>
          <span class="setting-desc">新消息到达时播放提示音</span>
        </span>
        <input type="checkbox" :checked="props.notifySoundEnabled" @change="e => emit('update:notifySoundEnabled', (e.target as HTMLInputElement).checked)" />
      </label>
      <p class="modal-error" v-if="profileError">{{ profileError }}</p>
      <p class="modal-success" v-if="profileSuccess">{{ profileSuccess }}</p>
      <button class="btn btn-ghost" @click="emit('close')">关闭</button>
      <button class="btn btn-ghost" @click="emit('openBlacklist')">黑名单管理</button>
      <button class="btn btn-ghost" @click="emit('openSessions')">登录设备</button>
    </div>

    <!-- 头像裁剪弹窗（选中图片后先裁剪再上传） -->
    <AvatarCropModal v-if="cropFile" :file="cropFile" @cropped="onCropped" @cancel="cropFile = null" />
  </div>
</template>

<style scoped>
.profile-avatar {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  padding: 6px 0 10px;
}
.hidden-file {
  display: none;
}
.profile-row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 6px 0;
}
.profile-label {
  width: 64px;
  flex-shrink: 0;
  font-size: 13px;
  color: var(--text-secondary);
}
.profile-value {
  font-size: 14px;
  font-weight: 500;
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
}
.profile-email {
  font-weight: 400;
  color: var(--text-secondary);
}
.profile-edit {
  flex: 1;
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.profile-edit .input {
  flex: 1;
  min-width: 0;
}
.profile-edit .btn {
  flex-shrink: 0;
}
.email-edit {
  flex-direction: column;
  align-items: stretch;
  padding: 10px 0 4px;
  border-top: 1px dashed var(--border);
}
.email-code-row {
  display: flex;
  gap: 8px;
}
.email-code-row .input {
  flex: 1;
}
.email-code-row .code-btn {
  flex-shrink: 0;
  padding: 0 12px;
  font-size: 12.5px;
  border-radius: 8px;
  background: var(--bg-hover);
  color: var(--primary);
  border: 1px solid var(--border);
}
.email-code-row .code-btn:hover:not(:disabled) {
  background: var(--active-bg);
  border-color: var(--primary-light);
}
.email-code-row .code-btn:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}
</style>
