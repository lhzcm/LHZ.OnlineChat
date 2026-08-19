<script setup lang="ts">
import { ref, computed } from 'vue'
import Avatar from '@/components/Avatar.vue'
import { useFriendStore } from '@/stores/friend'
import { useGroupStore } from '@/stores/group'

const props = defineProps<{ groupId: number }>()
const emit = defineEmits<{ close: []; invited: [] }>()
const friendStore = useFriendStore()
const groupStore = useGroupStore()

const selectedInviteIds = ref<number[]>([])
const inviting = ref(false)
const inviteError = ref('')
const inviteSuccess = ref('')

/** 可邀请的好友：我的好友中不在当前群成员里的 */
const invitableFriends = computed(() => {
  const memberIds = new Set(groupStore.members.map(m => m.userId))
  return friendStore.friends.filter(f => !memberIds.has(f.userId))
})

async function doInvite() {
  if (selectedInviteIds.value.length === 0) return
  inviting.value = true
  inviteError.value = ''
  inviteSuccess.value = ''
  try {
    const res = await groupStore.inviteMembers(props.groupId, selectedInviteIds.value)
    if (res.success) {
      inviteSuccess.value = res.message
      selectedInviteIds.value = []
      await groupStore.fetchMembers(props.groupId)
      groupStore.fetchGroups()
      emit('invited')
    } else {
      inviteError.value = res.message
    }
  } finally {
    inviting.value = false
  }
}
</script>

<template>
  <div class="modal-overlay" @click.self="emit('close')">
    <div class="modal">
      <h3>邀请好友加入群组</h3>
      <div class="empty" v-if="invitableFriends.length === 0">
        <span class="empty-icon">👥</span>
        <span>没有可邀请的好友</span>
      </div>
      <label v-for="f in invitableFriends" :key="f.userId" class="friend-check">
        <input type="checkbox" :value="f.userId" v-model="selectedInviteIds" />
        <Avatar :name="f.nickname" :url="f.avatar" size="sm" />
        <span class="contact-name">{{ f.nickname }}</span>
        <span class="request-meta">账号 {{ f.userId }}</span>
      </label>
      <button class="btn btn-primary" :disabled="selectedInviteIds.length === 0 || inviting" @click="doInvite">
        {{ inviting ? '邀请中…' : `邀请 (${selectedInviteIds.length})` }}
      </button>
      <p class="modal-error" v-if="inviteError">{{ inviteError }}</p>
      <p class="modal-success" v-if="inviteSuccess">{{ inviteSuccess }}</p>
      <button class="btn btn-ghost" @click="emit('close')">关闭</button>
    </div>
  </div>
</template>
