import { defineStore } from 'pinia'
import { ref } from 'vue'
import { friendApi } from '@/api/friend'
import type { FriendInfo, FriendRequestInfo } from '@/types'

export const useFriendStore = defineStore('friend', () => {
  const friends = ref<FriendInfo[]>([])
  const pendingRequests = ref<FriendRequestInfo[]>([])

  async function fetchFriends() {
    const res = await friendApi.getFriends()
    if (res.success && res.data) {
      friends.value = res.data
    }
  }

  async function fetchPendingRequests() {
    const res = await friendApi.getPendingRequests()
    if (res.success && res.data) {
      pendingRequests.value = res.data
    }
  }

  async function sendRequest(username: string) {
    return await friendApi.sendRequest(username)
  }

  async function acceptRequest(requestId: number) {
    const res = await friendApi.acceptRequest(requestId)
    if (res.success) {
      await fetchFriends()
      await fetchPendingRequests()
    }
    return res
  }

  async function rejectRequest(requestId: number) {
    const res = await friendApi.rejectRequest(requestId)
    if (res.success) {
      await fetchPendingRequests()
    }
    return res
  }

  async function deleteFriend(friendId: number) {
    const res = await friendApi.deleteFriend(friendId)
    if (res.success) {
      friends.value = friends.value.filter(f => f.userId !== friendId)
    }
    return res
  }

  function updateOnlineStatus(userId: number, isOnline: boolean) {
    const friend = friends.value.find(f => f.userId === userId)
    if (friend) {
      friend.isOnline = isOnline
    }
  }

  return { friends, pendingRequests, fetchFriends, fetchPendingRequests, sendRequest, acceptRequest, rejectRequest, deleteFriend, updateOnlineStatus }
})
