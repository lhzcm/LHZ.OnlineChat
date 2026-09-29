import { defineStore } from 'pinia'
import { ref } from 'vue'
import { groupApi } from '@/api/group'
import type { GroupInfo, GroupMemberInfo } from '@/types'

export const useGroupStore = defineStore('group', () => {
  const groups = ref<GroupInfo[]>([])
  const members = ref<GroupMemberInfo[]>([])

  async function fetchGroups() {
    const res = await groupApi.getMyGroups()
    if (res.success && res.data) {
      groups.value = res.data
    }
  }

  async function createGroup(name: string) {
    const res = await groupApi.createGroup(name)
    if (res.success && res.data) {
      groups.value.unshift(res.data)
    }
    return res
  }

  async function fetchMembers(groupId: number) {
    const res = await groupApi.getMembers(groupId)
    if (res.success && res.data) {
      members.value = res.data
    }
    return res
  }

  async function inviteMembers(groupId: number, userIds: number[]) {
    const res = await groupApi.inviteMembers(groupId, userIds)
    return res
  }

  async function joinGroup(groupId: number) {
    const res = await groupApi.joinGroup(groupId)
    if (res.success) {
      await fetchGroups()
    }
    return res
  }

  async function leaveGroup(groupId: number) {
    const res = await groupApi.leaveGroup(groupId)
    if (res.success) {
      groups.value = groups.value.filter(g => g.id !== groupId)
    }
    return res
  }

  async function kickMember(groupId: number, userId: number) {
    const res = await groupApi.kickMember(groupId, userId)
    return res
  }

  async function dismissGroup(groupId: number) {
    const res = await groupApi.dismissGroup(groupId)
    if (res.success) {
      groups.value = groups.value.filter(g => g.id !== groupId)
    }
    return res
  }

  return { groups, members, fetchGroups, createGroup, fetchMembers, inviteMembers, joinGroup, leaveGroup, kickMember, dismissGroup }
})
