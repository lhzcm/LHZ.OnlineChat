<script setup lang="ts">
import { ref, computed } from 'vue'
import Avatar from '@/components/Avatar.vue'
import InviteModal from './InviteModal.vue'
import GroupRobotModal from './GroupRobotModal.vue'
import { groupApi } from '@/api/group'
import { useGroupStore } from '@/stores/group'
import { useAuthStore } from '@/stores/auth'
import type { GroupMemberInfo } from '@/types'

const props = defineProps<{ groupId: number; groupName: string }>()
const emit = defineEmits<{ close: []; changed: [] }>()
const groupStore = useGroupStore()
const auth = useAuthStore()

const membersError = ref('')
const showInviteModal = ref(false)
const showGroupRobotModal = ref(false)

/** 当前用户是否可管理群组（群主或管理员） */
const canInvite = computed(() => {
  const g = groupStore.groups.find(x => x.id === props.groupId)
  if (!g) return false
  if (g.ownerId === auth.user?.id) return true
  const me = groupStore.members.find(m => m.userId === auth.user?.id)
  return me ? me.role <= 1 : false
})

function roleText(role: number): string {
  return role === 0 ? '群主' : role === 1 ? '管理员' : ''
}

/** 我是否可设置/取消管理员（仅群主） */
function canSetAdmin(m: GroupMemberInfo): boolean {
  const me = groupStore.members.find(x => x.userId === auth.user?.id)
  if (!me || me.role !== 0) return false
  if (m.userId === auth.user?.id) return false
  if (m.role === 0) return false // 不能改群主
  return true
}

async function toggleAdmin(m: GroupMemberInfo) {
  membersError.value = ''
  const isAdmin = m.role !== 1
  const res = await groupApi.setAdmin(props.groupId, m.userId, isAdmin)
  if (res.success) {
    await groupStore.fetchMembers(props.groupId)
  } else {
    membersError.value = res.message
  }
}

function canKick(m: GroupMemberInfo): boolean {
  if (!canInvite.value) return false
  if (m.userId === auth.user?.id) return false
  if (m.role === 0) return false // 不能踢群主
  const me = groupStore.members.find(x => x.userId === auth.user?.id)
  if (me && me.role === 1 && m.role === 1) return false // 管理员不能踢管理员
  return true
}

async function kickGroupMember(m: GroupMemberInfo) {
  if (!window.confirm(`确定将 ${m.nickname} 踢出群组？`)) return
  const res = await groupStore.kickMember(props.groupId, m.userId)
  if (res.success) {
    await groupStore.fetchMembers(props.groupId)
    groupStore.fetchGroups()
    emit('changed')
  } else {
    membersError.value = res.message
  }
}

function onGroupRobotAdded() {
  groupStore.fetchMembers(props.groupId)
}
</script>

<template>
  <div class="modal-overlay" @click.self="emit('close')">
    <div class="modal">
      <h3>{{ props.groupName }} · 群成员 ({{ groupStore.members.length }})</h3>
      <div class="invite-bar" v-if="canInvite">
        <button class="btn btn-primary btn-sm" @click="showInviteModal = true">+ 邀请好友</button>
        <button class="btn btn-ghost btn-sm" @click="showGroupRobotModal = true">+ 添加机器人</button>
      </div>
      <div class="empty" v-if="groupStore.members.length === 0">
        <span class="empty-icon">👥</span>
        <span>暂无成员</span>
      </div>
      <div v-for="m in groupStore.members" :key="m.userId" class="request-item">
        <Avatar :name="m.nickname" :url="m.avatar" size="sm" />
        <div class="request-info">
          <span class="request-name">
            {{ m.nickname }}
            <span class="bot-tag" v-if="m.isBot">🤖</span>
            <span class="role-tag" :class="'role-' + m.role">{{ roleText(m.role) }}</span>
          </span>
          <span class="request-meta">
            <span :class="['status-dot', m.isOnline ? 'online' : 'offline']"></span>
            {{ m.isOnline ? '在线' : '离线' }} · 账号 {{ m.userId }}
          </span>
        </div>
        <button v-if="canSetAdmin(m)" class="btn btn-sm btn-ghost kick-btn" @click="toggleAdmin(m)">
          {{ m.role === 1 ? '取消管理员' : '设为管理员' }}
        </button>
        <button v-if="canKick(m)" class="btn btn-sm btn-ghost kick-btn" @click="kickGroupMember(m)">踢出</button>
      </div>
      <p class="modal-error" v-if="membersError">{{ membersError }}</p>
      <button class="btn btn-ghost" @click="emit('close')">关闭</button>
    </div>
  </div>

  <InviteModal v-if="showInviteModal" :groupId="props.groupId" @close="showInviteModal = false" @invited="emit('changed')" />
  <GroupRobotModal v-if="showGroupRobotModal" :groupId="props.groupId" @close="showGroupRobotModal = false" @added="onGroupRobotAdded" />
</template>
