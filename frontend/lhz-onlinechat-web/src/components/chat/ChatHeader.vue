<script setup lang="ts">
import { computed } from 'vue'
import Avatar from '@/components/Avatar.vue'
import { useFriendStore } from '@/stores/friend'
import { useGroupStore } from '@/stores/group'
import type { ChatType } from '@/types'

/**
 * 聊天窗口头部：返回、头像、名称、副标题（在线状态 / 正在输入）、群成员、会话内搜索。
 *
 * 副标题与头像直接由 store 派生（不再让 ChatArea 算好传下来）：
 * 这两个值只在这里用，放在父级只会让"谁的会话"这类事实散落两处。
 */
const props = defineProps<{
  chat: { type: ChatType; id: number; name: string }
  /** 对方正在输入（4 秒无新帧自动消失，计时在 ChatArea） */
  typing: boolean
  /** 会话内搜索是否展开（按钮高亮用） */
  searchOpen: boolean
}>()
const emit = defineEmits<{
  back: []
  members: []
  'toggle-search': []
}>()

const friendStore = useFriendStore()
const groupStore = useGroupStore()

/** 私聊显示原昵称+在线状态（有备注时），群聊显示人数 */
const sub = computed(() => {
  if (props.chat.type === 'private') {
    const f = friendStore.friends.find(x => x.userId === props.chat.id)
    if (!f) return ''
    const status = f.isOnline ? '在线' : '离线'
    return f.remark ? `${f.nickname} · ${status}` : status
  }
  const g = groupStore.groups.find(x => x.id === props.chat.id)
  return g ? `${g.memberCount} 人` : ''
})

/** 会话头像（私聊=好友头像，群聊=群头像） */
const avatar = computed(() => (
  props.chat.type === 'private'
    ? friendStore.friends.find(x => x.userId === props.chat.id)?.avatar ?? null
    : groupStore.groups.find(x => x.id === props.chat.id)?.avatar ?? null
))
</script>

<template>
  <div class="chat-header">
    <button class="back-btn" @click="emit('back')" title="返回">
      <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <polyline points="15 18 9 12 15 6" />
      </svg>
    </button>
    <Avatar :name="chat.name" :url="avatar" size="sm" />
    <div class="chat-title">
      <div class="chat-title-row">
        <span class="chat-type-tag" v-if="chat.type === 'group'">群</span>
        <span class="chat-name">{{ chat.name }}</span>
      </div>
      <span class="chat-sub" :class="{ typing }">{{ typing ? '正在输入…' : sub }}</span>
    </div>
    <div class="header-spacer"></div>
    <button v-if="chat.type === 'group'" class="icon-btn" @click="emit('members')" title="群成员">
      <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
        <circle cx="9" cy="7" r="4" />
        <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
        <path d="M16 3.13a4 4 0 0 1 0 7.75" />
      </svg>
    </button>
    <button class="icon-btn" :class="{ active: searchOpen }" @click="emit('toggle-search')" title="搜索会话内消息">
      <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <circle cx="11" cy="11" r="8" />
        <line x1="21" y1="21" x2="16.65" y2="16.65" />
      </svg>
    </button>
  </div>
</template>

<style scoped>
.chat-header {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 18px;
  background: var(--bg-white);
  border-bottom: 1px solid var(--border);
  box-shadow: 0 1px 4px rgba(31, 35, 41, 0.04);
  z-index: 1;
}

.back-btn {
  display: none;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  border: none;
  background: var(--bg-hover);
  border-radius: 50%;
  cursor: pointer;
  color: var(--text);
  flex-shrink: 0;
}

.back-btn:active {
  background: var(--border);
}

.chat-title {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.chat-title-row {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 0;
}

.chat-type-tag {
  flex-shrink: 0;
  font-size: 11px;
  font-weight: 600;
  padding: 1px 7px;
  border-radius: 8px;
  background: var(--active-bg);
  color: var(--primary);
}

.header-spacer {
  flex: 1;
}

.chat-name {
  font-weight: 600;
  font-size: 16px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.chat-sub {
  font-size: 12px;
  color: var(--text-secondary);
}


.chat-sub.typing {
  color: var(--primary, #3b82f6);
}

/* 群公告横幅 */

.icon-btn {
  position: relative;
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: none;
  background: transparent;
  border-radius: 50%;
  color: var(--text-secondary);
  cursor: pointer;
  transition: all 0.2s;
  flex-shrink: 0;
}

.icon-btn:hover {
  background: var(--bg-hover);
  color: var(--text);
}

.icon-btn.active {
  color: var(--primary);
  background: var(--active-bg);
}

/* 返回按钮只在移动端出现（桌面端用侧边栏切换会话） */
@media (max-width: 720px) {
  .back-btn {
    display: flex;
  }
}

@media (min-width: 721px) {
  .back-btn {
    display: none;
  }
}
</style>