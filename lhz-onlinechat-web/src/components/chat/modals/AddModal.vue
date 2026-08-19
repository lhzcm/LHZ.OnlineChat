<script setup lang="ts">
import { ref } from 'vue'
import { useFriendStore } from '@/stores/friend'
import { useGroupStore } from '@/stores/group'

const props = defineProps<{ mode: 'friend' | 'group' }>()
const emit = defineEmits<{ close: [] }>()
const friendStore = useFriendStore()
const groupStore = useGroupStore()

const addFriendAccount = ref('')
const newGroupName = ref('')
const modalError = ref('')
const modalSuccess = ref('')

async function addFriend() {
  const account = Number(addFriendAccount.value.trim())
  if (!account || account <= 0) {
    modalError.value = '请输入正确的账号 ID'
    modalSuccess.value = ''
    return
  }
  const res = await friendStore.sendRequest(account)
  if (res.success) {
    modalSuccess.value = '好友申请已发送'
    modalError.value = ''
    addFriendAccount.value = ''
  } else {
    modalError.value = res.message
    modalSuccess.value = ''
  }
}

async function createGroup() {
  const res = await groupStore.createGroup(newGroupName.value)
  if (res.success) {
    modalSuccess.value = '群组创建成功'
    modalError.value = ''
    newGroupName.value = ''
  } else {
    modalError.value = res.message
    modalSuccess.value = ''
  }
}
</script>

<template>
  <div class="modal-overlay" @click.self="emit('close')">
    <div class="modal">
      <h3>{{ props.mode === 'friend' ? '添加好友' : '创建群组' }}</h3>
      <template v-if="props.mode === 'friend'">
        <input v-model="addFriendAccount" class="input" type="text" inputmode="numeric" placeholder="输入对方账号 ID" @keyup.enter="addFriend" />
        <button class="btn btn-primary" @click="addFriend">发送申请</button>
      </template>
      <template v-else>
        <input v-model="newGroupName" class="input" placeholder="输入群组名称" @keyup.enter="createGroup" />
        <button class="btn btn-primary" @click="createGroup">创建</button>
      </template>
      <p class="modal-error" v-if="modalError">{{ modalError }}</p>
      <p class="modal-success" v-if="modalSuccess">{{ modalSuccess }}</p>
      <button class="btn btn-ghost" @click="emit('close')">关闭</button>
    </div>
  </div>
</template>
