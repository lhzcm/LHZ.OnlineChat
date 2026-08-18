<template>
  <div class="chat-layout">
    <!-- 侧边栏 -->
    <aside class="sidebar" :class="{ 'is-hidden': mobileChatOpen }">
      <div class="sidebar-header">
        <div class="user-info" @click="openProfileModal" title="个人信息">
          <Avatar :name="auth.user?.nickname || ''" :url="auth.user?.avatar" />
          <div class="user-text">
            <span class="nickname">{{ auth.user?.nickname }}</span>
            <span class="account-id" @click.stop="copyAccountId" :title="copyTip">{{ copyTip }}</span>
          </div>
        </div>
        <div class="header-actions">
          <button class="icon-btn" @click="applyTheme(!isDark)" :title="isDark ? '切换到浅色模式' : '切换到深色模式'">
            <svg v-if="!isDark" viewBox="0 0 24 24" width="19" height="19" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
            </svg>
            <svg v-else viewBox="0 0 24 24" width="19" height="19" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <circle cx="12" cy="12" r="5" />
              <line x1="12" y1="1" x2="12" y2="3" />
              <line x1="12" y1="21" x2="12" y2="23" />
              <line x1="4.22" y1="4.22" x2="5.64" y2="5.64" />
              <line x1="18.36" y1="18.36" x2="19.78" y2="19.78" />
              <line x1="1" y1="12" x2="3" y2="12" />
              <line x1="21" y1="12" x2="23" y2="12" />
              <line x1="4.22" y1="19.78" x2="5.64" y2="18.36" />
              <line x1="18.36" y1="5.64" x2="19.78" y2="4.22" />
            </svg>
          </button>
          <button class="icon-btn" @click="openRequestsModal" title="好友申请">
            <svg viewBox="0 0 24 24" width="19" height="19" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9" />
              <path d="M13.73 21a2 2 0 0 1-3.46 0" />
            </svg>
            <span v-if="friendStore.pendingRequests.length" class="badge">{{ friendStore.pendingRequests.length }}</span>
          </button>
          <button class="icon-btn" @click="handleLogout" title="退出登录">
            <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
              <polyline points="16 17 21 12 16 7" />
              <line x1="21" y1="12" x2="9" y2="12" />
            </svg>
          </button>
        </div>
      </div>

      <!-- Tab 切换 -->
      <div class="tabs">
        <button :class="['tab', { active: activeTab === 'sessions' }]" @click="activeTab = 'sessions'">会话</button>
        <button :class="['tab', { active: activeTab === 'friends' }]" @click="activeTab = 'friends'">好友</button>
        <button :class="['tab', { active: activeTab === 'groups' }]" @click="activeTab = 'groups'">群组</button>
      </div>

      <!-- 操作栏 -->
      <div class="action-bar">
        <button class="btn btn-primary" @click="openAddModal">
          {{ activeTab === 'friends' ? '+ 添加好友' : activeTab === 'groups' ? '+ 创建群组' : '＋' }}
        </button>
      </div>

      <!-- 会话列表 -->
      <div class="contact-list" v-if="activeTab === 'sessions'">
        <div v-for="s in sortedSessions" :key="s.type + '_' + s.id"
          :class="['contact-item', { active: currentChat?.type === s.type && currentChat.id === s.id }]"
          @click="selectSession(s)">
          <div class="avatar-wrap">
            <Avatar :name="s.name" :url="s.avatar" size="sm" />
            <span class="group-badge" v-if="s.type === 'group'">群</span>
          </div>
          <div class="contact-info">
            <div class="info-top">
              <span class="contact-name">
                <span class="pin-icon" v-if="s.isPinned">📌</span>
                {{ s.name }}
              </span>
              <span class="session-time">
                <span class="mute-icon" v-if="s.muted">🔕</span>
                {{ timeFor(s) }}
              </span>
            </div>
            <div class="info-bottom">
              <span class="contact-meta">{{ previewFor(s) }}</span>
              <span v-if="unreadOf(s.type, s.id)" class="unread-badge">{{ unreadOf(s.type, s.id) }}</span>
            </div>
          </div>
          <button class="friend-more" title="会话设置" @click.stop="openSessionSetting(s)">⋯</button>
        </div>
        <div class="empty" v-if="chatStore.sessions.length === 0">
          <span class="empty-icon">💬</span>
          <span>暂无会话，去添加好友开始聊天吧</span>
        </div>
      </div>

      <!-- 好友列表（按分类分组） -->
      <div class="contact-list" v-else-if="activeTab === 'friends'">
        <template v-for="group in groupedFriends" :key="group.category">
          <div class="group-header" v-if="group.friends.length">
            <span>{{ group.category }}</span>
            <span class="group-count">{{ group.friends.length }}</span>
          </div>
          <div v-for="f in group.friends" :key="f.userId"
            :class="['contact-item', { active: currentChat?.type === 'private' && currentChat.id === f.userId }]"
            @click="selectPrivateChat(f)">
            <div class="avatar-wrap">
              <Avatar :name="friendDisplayName(f)" :url="f.avatar" size="sm" />
              <span :class="['status-dot', f.isOnline ? 'online' : 'offline']"></span>
            </div>
            <div class="contact-info">
              <div class="info-top">
                <span class="contact-name">{{ friendDisplayName(f) }}</span>
                <span v-if="unreadOf('private', f.userId)" class="unread-badge">{{ unreadOf('private', f.userId) }}</span>
              </div>
              <div class="info-bottom">
                <span class="contact-meta">{{ lastMessageFor('private', f.userId) || (f.isOnline ? '在线' : '离线') }}</span>
              </div>
            </div>
            <button class="friend-more" title="备注/分类" @click.stop="openFriendSetting(f)">⋯</button>
          </div>
        </template>
        <div class="empty" v-if="friendStore.friends.length === 0">
          <span class="empty-icon">👥</span>
          <span>暂无好友，点击上方按钮添加</span>
        </div>
      </div>

      <!-- 群组列表 -->
      <div class="contact-list" v-else>
        <div v-for="g in groupStore.groups" :key="g.id"
          :class="['contact-item', { active: currentChat?.type === 'group' && currentChat.id === g.id }]"
          @click="selectGroupChat(g)">
          <div class="avatar-wrap">
            <Avatar :name="g.name" :url="g.avatar" size="sm" />
            <span class="group-badge">群</span>
          </div>
          <div class="contact-info">
            <div class="info-top">
              <span class="contact-name">{{ g.name }}</span>
              <span v-if="unreadOf('group', g.id)" class="unread-badge">{{ unreadOf('group', g.id) }}</span>
            </div>
            <div class="info-bottom">
              <span class="contact-meta">{{ lastMessageFor('group', g.id) || `${g.memberCount} 人` }}</span>
            </div>
          </div>
        </div>
        <div class="empty" v-if="groupStore.groups.length === 0">
          <span class="empty-icon">🗂️</span>
          <span>暂无群组，点击上方按钮创建</span>
        </div>
      </div>
    </aside>

    <!-- 聊天区域 -->
    <main class="chat-main" :class="{ 'is-show': mobileChatOpen }">
      <!-- 未选择会话 -->
      <div class="no-chat" v-if="!currentChat">
        <span class="empty-icon">💬</span>
        <p>选择一个会话开始聊天</p>
      </div>

      <!-- 聊天窗口 -->
      <template v-else>
        <div class="chat-header">
          <button class="back-btn" @click="backToList" title="返回">
            <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <polyline points="15 18 9 12 15 6" />
            </svg>
          </button>
          <Avatar :name="currentChat.name" :url="chatAvatar" size="sm" />
          <div class="chat-title">
            <div class="chat-title-row">
              <span class="chat-type-tag" v-if="currentChat.type === 'group'">群</span>
              <span class="chat-name">{{ currentChat.name }}</span>
            </div>
            <span class="chat-sub">{{ chatSub }}</span>
          </div>
          <div class="header-spacer"></div>
          <button v-if="currentChat.type === 'group'" class="icon-btn" @click="openMembersModal" title="群成员">
            <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
              <circle cx="9" cy="7" r="4" />
              <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
              <path d="M16 3.13a4 4 0 0 1 0 7.75" />
            </svg>
          </button>
        </div>

        <div class="chat-messages" ref="msgContainer">
          <div v-for="msg in currentMessages" :key="msg.messageId"
            :class="['msg-row', { mine: Number(msg.from) === auth.user?.id }]">
            <Avatar class="msg-avatar" :name="msg.senderName || currentChat.name" :url="msg.senderAvatar" size="sm" />
            <div class="msg-body">
              <span class="msg-sender" v-if="currentChat.type === 'group' && Number(msg.from) !== auth.user?.id">
                {{ msg.senderName }}
              </span>
              <div class="reply-preview" v-if="msg.replyContent && !msg.isDeleted">
                <span class="reply-sender">{{ msg.replySender || '引用' }}</span>
                <span class="reply-text">{{ msg.replyContent }}</span>
              </div>
              <div class="msg-bubble" :class="{ mentioned: isMentioned(msg), 'is-image': msg.messageType === 1, recalled: msg.isDeleted }">
                <span v-if="msg.isDeleted" class="msg-recalled-text">消息已撤回</span>
                <img v-else-if="msg.messageType === 1" :src="msg.content" class="msg-image" alt="图片"
                  loading="lazy" @click.stop="openLightbox(msg.content)" />
                <span v-else v-html="renderContent(msg.content)"></span>
              </div>
              <div class="msg-actions">
                <button v-if="canReply(msg)" class="msg-recall-btn" @click.stop="startReply(msg)">回复</button>
                <button v-if="canRecall(msg)" class="msg-recall-btn" @click.stop="recallMessage(msg)">撤回</button>
              </div>
              <div class="msg-meta-line">
                <span class="msg-time">{{ formatMsgTime(msg.timestamp) }}</span>
                <span v-if="isMyPrivateMessage(msg)" class="msg-status"
                  :class="{ read: chatStore.isReadByPeer(msg.messageId) }">
                  {{ chatStore.isReadByPeer(msg.messageId) ? '已读' : '未读' }}
                </span>
              </div>
            </div>
          </div>
        </div>

        <!-- 引用回复横幅 -->
        <div class="reply-bar" v-if="replyTarget">
          <span class="reply-bar-text">回复 {{ replyTarget.senderName }}：{{ replyTarget.content }}</span>
          <button class="reply-cancel" @click="replyTarget = null">✕</button>
        </div>

        <div class="chat-input-bar">
          <!-- @ 成员选择浮层 -->
          <div class="mention-panel" v-if="mentionOpen && mentionFiltered.length" @click.stop>
            <button v-for="m in mentionFiltered" :key="m.userId" class="mention-item" @click="pickMention(m)">
              <Avatar :name="m.nickname" :url="m.avatar" size="sm" />
              <span class="contact-name">{{ m.nickname }}</span>
              <span class="mention-role" v-if="m.role === 0">群主</span>
            </button>
          </div>
          <!-- 表情面板 -->
          <div class="emoji-panel" ref="emojiPanelRef" v-if="showEmojiPanel" @click.stop>
            <div class="emoji-group" v-for="g in emojiGroups" :key="g.name">
              <div class="emoji-group-title">{{ g.name }}</div>
              <div class="emoji-grid">
                <button v-for="e in g.list" :key="e" class="emoji-item" @click="insertEmoji(e)">{{ e }}</button>
              </div>
            </div>
          </div>
          <button v-if="currentChat.type === 'group'" class="emoji-btn mention-btn" @click.stop="openMentionPicker" title="提及成员">
            <span class="at-symbol">@</span>
          </button>
          <button class="emoji-btn" :disabled="sendingImage" @click="imageInputRef?.click()" title="发送图片">
            <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
              <circle cx="8.5" cy="8.5" r="1.5" />
              <polyline points="21 15 16 10 5 21" />
            </svg>
          </button>
          <input ref="imageInputRef" type="file" accept="image/*" class="hidden-file" @change="onImageSelect" />
          <button class="emoji-btn" ref="emojiBtnRef" @click.stop="toggleEmojiPanel" title="表情">
            <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <circle cx="12" cy="12" r="10" />
              <path d="M8 14s1.5 2 4 2 4-2 4-2" />
              <line x1="9" y1="9" x2="9.01" y2="9" />
              <line x1="15" y1="9" x2="15.01" y2="9" />
            </svg>
          </button>
          <input ref="inputEl" v-model="inputText" class="input" :placeholder="inputPlaceholder" @keydown="onInputKeydown" @input="onInputChange" />
          <button class="send-btn" @click="send" :disabled="!inputText.trim()">发送</button>
        </div>
        <p class="send-hint" v-if="sendHint">{{ sendHint }}</p>
      </template>
    </main>

    <!-- 好友设置弹窗（备注/分类） -->
    <div class="modal-overlay" v-if="showFriendSetting" @click.self="showFriendSetting = false">
      <div class="modal">
        <h3>好友设置</h3>
        <div class="friend-setting-head">
          <Avatar :name="friendSetting?.nickname || ''" :url="friendSetting?.avatar" size="sm" />
          <div class="friend-setting-names">
            <span class="request-name">{{ friendSetting?.nickname }}</span>
            <span class="request-meta" v-if="friendSetting?.remark">当前备注：{{ friendSetting.remark }}</span>
            <span class="request-meta">账号 {{ friendSetting?.userId }}</span>
          </div>
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
        <button class="btn btn-ghost" @click="showFriendSetting = false">关闭</button>
      </div>
    </div>

    <!-- 群成员面板 -->
    <div class="modal-overlay" v-if="showMembersModal" @click.self="showMembersModal = false">
      <div class="modal">
        <h3>{{ currentChat?.name }} · 群成员 ({{ groupStore.members.length }})</h3>
        <div class="invite-bar" v-if="canInvite">
          <button class="btn btn-primary btn-sm" @click="openInviteModal">+ 邀请好友</button>
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
              <span class="role-tag" :class="'role-' + m.role">{{ roleText(m.role) }}</span>
            </span>
            <span class="request-meta">
              <span :class="['status-dot', m.isOnline ? 'online' : 'offline']"></span>
              {{ m.isOnline ? '在线' : '离线' }} · 账号 {{ m.userId }}
            </span>
          </div>
          <button v-if="canKick(m)" class="btn btn-sm btn-ghost kick-btn" @click="kickGroupMember(m)">踢出</button>
        </div>
        <button class="btn btn-ghost" @click="showMembersModal = false">关闭</button>
      </div>
    </div>

    <!-- 邀请好友弹窗 -->
    <div class="modal-overlay" v-if="showInviteModal" @click.self="showInviteModal = false">
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
        <button class="btn btn-ghost" @click="showInviteModal = false">关闭</button>
      </div>
    </div>

    <!-- 个人资料弹窗 -->
    <div class="modal-overlay" v-if="showProfileModal" @click.self="showProfileModal = false">
      <div class="modal">
        <h3>个人信息</h3>
        <div class="profile-avatar">
          <Avatar :name="auth.user?.nickname || ''" :url="auth.user?.avatar" size="lg" />
          <button class="btn btn-sm btn-ghost" @click="triggerAvatarInput">更换头像</button>
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
          <input type="checkbox" :checked="notifySoundEnabled" @change="toggleNotifySound" />
        </label>
        <p class="modal-error" v-if="profileError">{{ profileError }}</p>
        <p class="modal-success" v-if="profileSuccess">{{ profileSuccess }}</p>
        <button class="btn btn-ghost" @click="showProfileModal = false">关闭</button>
      </div>
    </div>

    <!-- 好友申请弹窗 -->
    <div class="modal-overlay" v-if="showRequestsModal" @click.self="showRequestsModal = false">
      <div class="modal">
        <h3>好友申请</h3>
        <div class="empty" v-if="friendStore.pendingRequests.length === 0">
          <span class="empty-icon">📭</span>
          <span>暂无待处理的申请</span>
        </div>
        <div v-for="r in friendStore.pendingRequests" :key="r.id" class="request-item">
          <Avatar :name="r.nickname" :url="r.avatar" size="sm" />
          <div class="request-info">
            <span class="request-name">{{ r.nickname }}</span>
            <span class="request-meta">账号 {{ r.userId }}</span>
          </div>
          <div class="request-actions">
            <button class="btn btn-sm btn-primary" :disabled="handlingRequestId === r.id" @click="acceptRequest(r)">接受</button>
            <button class="btn btn-sm btn-ghost" :disabled="handlingRequestId === r.id" @click="rejectRequest(r)">拒绝</button>
          </div>
        </div>
        <p class="modal-error" v-if="requestError">{{ requestError }}</p>
        <button class="btn btn-ghost" @click="showRequestsModal = false">关闭</button>
      </div>
    </div>

    <!-- 添加弹窗 -->
    <div class="modal-overlay" v-if="showAddModal" @click.self="showAddModal = false">
      <div class="modal">
        <h3>{{ activeTab === 'friends' ? '添加好友' : '创建群组' }}</h3>
        <template v-if="activeTab === 'friends'">
          <input v-model="addFriendAccount" class="input" type="text" inputmode="numeric" placeholder="输入对方账号 ID" @keyup.enter="addFriend" />
          <button class="btn btn-primary" @click="addFriend">发送申请</button>
        </template>
        <template v-else>
          <input v-model="newGroupName" class="input" placeholder="输入群组名称" @keyup.enter="createGroup" />
          <button class="btn btn-primary" @click="createGroup">创建</button>
        </template>
        <p class="modal-error" v-if="modalError">{{ modalError }}</p>
        <p class="modal-success" v-if="modalSuccess">{{ modalSuccess }}</p>
        <button class="btn btn-ghost" @click="showAddModal = false">关闭</button>
      </div>
    </div>

    <!-- 会话设置弹窗（置顶/免打扰） -->
    <div class="modal-overlay" v-if="showSessionSetting" @click.self="showSessionSetting = false">
      <div class="modal">
        <h3>会话设置</h3>
        <div class="friend-setting-head">
          <Avatar :name="sessionSettingTarget?.name || ''" :url="sessionSettingTarget?.avatar" size="sm" />
          <div class="friend-setting-names">
            <span class="request-name">{{ sessionSettingTarget?.name }}</span>
            <span class="request-meta">{{ sessionSettingTarget?.type === 'group' ? '群聊' : '私聊' }}</span>
          </div>
        </div>
        <label class="setting-switch">
          <span>
            <span class="set-label">置顶会话</span>
            <span class="setting-desc">固定显示在会话列表顶部</span>
          </span>
          <input type="checkbox" :checked="sessionSettingTarget?.isPinned" @change="toggleSessionPinned" />
        </label>
        <label class="setting-switch">
          <span>
            <span class="set-label">消息免打扰</span>
            <span class="setting-desc">静音后不增加未读提醒</span>
          </span>
          <input type="checkbox" :checked="sessionSettingTarget?.muted" @change="toggleSessionMuted" />
        </label>
        <p class="modal-error" v-if="sessionSettingError">{{ sessionSettingError }}</p>
        <p class="modal-success" v-if="sessionSettingSuccess">{{ sessionSettingSuccess }}</p>
        <button class="btn btn-ghost" @click="showSessionSetting = false">关闭</button>
      </div>
    </div>

    <!-- 图片放大预览 -->
    <div class="lightbox" v-if="lightboxUrl" @click="lightboxUrl = ''">
      <img :src="lightboxUrl" alt="图片预览" />
      <span class="lightbox-close">✕ 点击任意处关闭</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useFriendStore } from '@/stores/friend'
import { useGroupStore } from '@/stores/group'
import { useChatStore } from '@/stores/chat'
import { useWebSocketStore } from '@/stores/websocket'
import { emojiGroups } from '@/constants/emojis'
import Avatar from '@/components/Avatar.vue'
import { avatarGradient, avatarInitial } from '@/utils/avatar'
import { authApi } from '@/api/auth'
import { messageApi } from '@/api/message'
import type { FriendInfo, FriendRequestInfo, GroupMemberInfo, WsMessage, ChatType, SessionInfo } from '@/types'

const router = useRouter()
const auth = useAuthStore()
const friendStore = useFriendStore()
const groupStore = useGroupStore()
const chatStore = useChatStore()
const ws = useWebSocketStore()

const activeTab = ref<'sessions' | 'friends' | 'groups'>('friends')
const inputText = ref('')
const inputEl = ref<HTMLInputElement | null>(null)
const msgContainer = ref<HTMLElement | null>(null)
const showAddModal = ref(false)
const addFriendAccount = ref('')
const newGroupName = ref('')
const modalError = ref('')
const modalSuccess = ref('')
const showRequestsModal = ref(false)
const handlingRequestId = ref<number | null>(null)
const requestError = ref('')
const sendHint = ref('')
// 移动端：聊天窗口全屏开关
const mobileChatOpen = ref(false)
// 表情面板
const showEmojiPanel = ref(false)
const emojiPanelRef = ref<HTMLElement | null>(null)
const emojiBtnRef = ref<HTMLElement | null>(null)
// 图片消息
const imageInputRef = ref<HTMLInputElement | null>(null)
const sendingImage = ref(false)
const lightboxUrl = ref('')
// 会话设置
const showSessionSetting = ref(false)
const sessionSettingTarget = ref<SessionInfo | null>(null)
const sessionSettingError = ref('')
const sessionSettingSuccess = ref('')
// 消息提示音
const notifySoundEnabled = ref(localStorage.getItem('notifySound') !== '0')
let notifyAudioCtx: AudioContext | null = null

function toggleNotifySound(e: Event) {
  notifySoundEnabled.value = (e.target as HTMLInputElement).checked
  localStorage.setItem('notifySound', notifySoundEnabled.value ? '1' : '0')
}

/** 播放新消息提示音（Web Audio 合成，无需音频文件） */
function playNotifySound() {
  if (!notifySoundEnabled.value) return
  try {
    notifyAudioCtx = notifyAudioCtx || new AudioContext()
    const ctx = notifyAudioCtx
    if (ctx.state === 'suspended') ctx.resume()
    const now = ctx.currentTime
    const notes = [880, 660]
    notes.forEach((freq, i) => {
      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = 'sine'
      osc.frequency.value = freq
      const t = now + i * 0.15
      gain.gain.setValueAtTime(0.001, t)
      gain.gain.exponentialRampToValueAtTime(0.1, t + 0.02)
      gain.gain.exponentialRampToValueAtTime(0.001, t + 0.12)
      osc.connect(gain).connect(ctx.destination)
      osc.start(t)
      osc.stop(t + 0.14)
    })
  } catch { /* 忽略音频错误（如浏览器策略限制） */ }
}
// 账号 ID 复制提示
const copyTip = ref('')
let copyTipTimer: number | null = null
// 群成员管理
const showMembersModal = ref(false)
const showInviteModal = ref(false)
const selectedInviteIds = ref<number[]>([])
const inviting = ref(false)
const inviteError = ref('')
const inviteSuccess = ref('')
// @ 提及
const mentionOpen = ref(false)
const mentionQuery = ref('')
// 好友设置（备注/分类）
const showFriendSetting = ref(false)
const friendSetting = ref<FriendInfo | null>(null)
const friendRemark = ref('')
const friendCategory = ref('')
const savingFriendTag = ref(false)
const friendTagError = ref('')
const friendTagSuccess = ref('')
const presetCategories = ['家人', '朋友', '同事', '同学', '客户', '其他']
// 个人资料
const showProfileModal = ref(false)
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
let emailCountTimer: number | null = null

const currentChat = ref<{ type: ChatType; id: number; name: string } | null>(null)

const currentMessages = computed(() => {
  if (!currentChat.value) return []
  const key = `${currentChat.value.type}_${currentChat.value.id}`
  return chatStore.messages.get(key) || []
})

// 会话列表：置顶优先，再按最后消息时间倒序
const sortedSessions = computed(() =>
  [...chatStore.sessions].sort((a, b) => {
    if (a.isPinned !== b.isPinned) return a.isPinned ? -1 : 1
    return new Date(b.lastTime).getTime() - new Date(a.lastTime).getTime()
  })
)

// 聊天窗口副标题：私聊显示原昵称+在线状态（有备注时），群聊显示人数
const chatSub = computed(() => {
  if (!currentChat.value) return ''
  if (currentChat.value.type === 'private') {
    const f = friendStore.friends.find(x => x.userId === currentChat.value!.id)
    if (!f) return ''
    const status = f.isOnline ? '在线' : '离线'
    return f.remark ? `${f.nickname} · ${status}` : status
  }
  const g = groupStore.groups.find(x => x.id === currentChat.value!.id)
  return g ? `${g.memberCount} 人` : ''
})

// 当前用户是否可管理群组（群主或管理员）
const canInvite = computed(() => {
  if (!currentChat.value || currentChat.value.type !== 'group') return false
  const g = groupStore.groups.find(x => x.id === currentChat.value!.id)
  if (!g) return false
  if (g.ownerId === auth.user?.id) return true
  const me = groupStore.members.find(m => m.userId === auth.user?.id)
  return me ? me.role <= 1 : false
})

// 可邀请的好友：我的好友中不在当前群成员里的
const invitableFriends = computed(() => {
  const memberIds = new Set(groupStore.members.map(m => m.userId))
  return friendStore.friends.filter(f => !memberIds.has(f.userId))
})

// @ 成员选择：按输入过滤
const mentionFiltered = computed(() => {
  const q = mentionQuery.value.trim().toLowerCase()
  const list = groupStore.members.filter(m => !q || m.nickname.toLowerCase().includes(q))
  return list.slice(0, 8)
})

// 输入框提示：仅群聊提示 @ 提及
const inputPlaceholder = computed(() =>
  currentChat.value?.type === 'group' ? '输入消息… 输入 @ 可提及成员' : '输入消息…'
)

// 当前会话头像（私聊=好友头像，群聊=群头像）
const chatAvatar = computed(() => {
  if (!currentChat.value) return null
  if (currentChat.value.type === 'private') {
    return friendStore.friends.find(x => x.userId === currentChat.value!.id)?.avatar ?? null
  }
  return groupStore.groups.find(x => x.id === currentChat.value!.id)?.avatar ?? null
})

// 好友显示名：备注优先，其次昵称
function friendDisplayName(f: FriendInfo): string {
  return f.remark || f.nickname
}

// 好友按分类分组（未分组放最后，其余按分类名排序）
const groupedFriends = computed(() => {
  const map = new Map<string, FriendInfo[]>()
  for (const f of friendStore.friends) {
    const cat = f.category || '未分组'
    const list = map.get(cat) || []
    list.push(f)
    map.set(cat, list)
  }
  const groups = [...map.entries()]
    .map(([category, friends]) => ({
      category,
      friends: [...friends].sort((a, b) => Number(b.isOnline) - Number(a.isOnline) || a.nickname.localeCompare(b.nickname))
    }))
    .sort((a, b) => {
      if (a.category === '未分组') return 1
      if (b.category === '未分组') return -1
      return a.category.localeCompare(b.category)
    })
  return groups
})

// ==================== 时间 ====================
function pad(n: number): string {
  return String(n).padStart(2, '0')
}

function formatMsgTime(ts: number): string {
  if (!ts) return ''
  const d = new Date(ts)
  const now = new Date()
  const hm = `${pad(d.getHours())}:${pad(d.getMinutes())}`
  if (d.toDateString() === now.toDateString()) return hm
  if (d.getFullYear() === now.getFullYear()) return `${d.getMonth() + 1}/${d.getDate()} ${hm}`
  return `${d.getFullYear()}/${d.getMonth() + 1}/${d.getDate()} ${hm}`
}

// 主题（深色/浅色）
const isDark = ref(false)

function applyTheme(dark: boolean) {
  isDark.value = dark
  document.documentElement.dataset.theme = dark ? 'dark' : 'light'
  localStorage.setItem('theme', dark ? 'dark' : 'light')
}

onMounted(async () => {
  // 初始化主题（记忆偏好）
  applyTheme(localStorage.getItem('theme') === 'dark')
  if (!auth.isLoggedIn) {
    router.push('/login')
    return
  }
  await auth.fetchUser()
  copyTip.value = auth.user ? `ID: ${auth.user.id}` : ''

  // 先注册回调，再建立连接，避免漏掉连接期间的消息
  ws.onMessage((msg) => {
    handleWsMessage(msg)
  })

  ws.onStatusChange((online, userId) => {
    friendStore.updateOnlineStatus(userId, online)
  })

  ws.connect(auth.token)
  await Promise.all([
    friendStore.fetchFriends(),
    friendStore.fetchPendingRequests(),
    groupStore.fetchGroups(),
    chatStore.fetchSessions()
  ])

  // 进入主页：有会话时默认显示会话列表，否则显示好友 Tab 引导添加
  if (chatStore.sessions.length > 0) {
    activeTab.value = 'sessions'
  }

  // 拉取离线消息并计入未读角标
  try {
    if (auth.user) {
      const counts = await chatStore.loadOfflineMessages(auth.user.id)
      for (const [key, count] of counts) {
        chatStore.setUnreadCount(key, count)
      }
    }
  } catch { /* 离线消息拉取失败不阻塞界面 */ }

  // 点击面板/按钮外部时关闭表情面板
  document.addEventListener('click', onDocClick)
})

onUnmounted(() => {
  document.removeEventListener('click', onDocClick)
  if (copyTipTimer) clearTimeout(copyTipTimer)
  if (emailCountTimer) clearInterval(emailCountTimer)
})

function onDocClick(e: MouseEvent) {
  const target = e.target as HTMLElement
  if (!emojiPanelRef.value?.contains(target) && !emojiBtnRef.value?.contains(target)) {
    showEmojiPanel.value = false
  }
}

// ==================== 表情 ====================
function toggleEmojiPanel() {
  showEmojiPanel.value = !showEmojiPanel.value
}

/** 在输入框光标位置插入表情，并恢复焦点与光标 */
function insertEmoji(emoji: string) {
  const el = inputEl.value
  if (el) {
    const start = el.selectionStart ?? inputText.value.length
    const end = el.selectionEnd ?? inputText.value.length
    inputText.value = inputText.value.slice(0, start) + emoji + inputText.value.slice(end)
    nextTick(() => {
      el.focus()
      const pos = start + emoji.length
      el.setSelectionRange(pos, pos)
    })
  } else {
    inputText.value += emoji
  }
}

/** 回车发送（中文输入法组词回车不误发） */
function onInputEnter(e: KeyboardEvent) {
  if (e.isComposing) return
  send()
}

// ==================== 图片消息 ====================
/** 选择图片后上传并作为图片消息发送 */
async function onImageSelect(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (!currentChat.value || !auth.user) return
  if (!ws.connected) {
    sendHint.value = '连接已断开，正在重连，请稍候…'
    return
  }
  if (!file.type.startsWith('image/')) {
    sendHint.value = '请选择图片文件'
    return
  }

  sendingImage.value = true
  sendHint.value = ''
  try {
    const res = await messageApi.uploadImage(file)
    if (!res.success || !res.data?.url) {
      sendHint.value = res.message || '图片上传失败'
      return
    }
    const msg: WsMessage = {
      type: currentChat.value.type === 'private' ? 'private_message' : 'group_message',
      from: String(auth.user.id),
      to: String(currentChat.value.id),
      content: res.data.url,
      timestamp: Date.now(),
      messageId: genId(),
      messageType: 1, // 图片
      senderName: auth.user.nickname,
      senderAvatar: auth.user.avatar
    }
    chatStore.addMessage(msg, auth.user.id) // 乐观插入
    ws.sendMessage(msg)
    scrollToBottom()
  } catch (err: any) {
    sendHint.value = err?.message || '图片发送失败'
  } finally {
    sendingImage.value = false
  }
}

function openLightbox(url: string) {
  lightboxUrl.value = url
}

// ==================== 消息撤回 ====================
/** 自己发出的、未被撤回的消息可撤回（服务端限 2 分钟内） */
function canRecall(msg: WsMessage): boolean {
  if (msg.isDeleted) return false
  return Number(msg.from) === auth.user?.id
}

/** 引用回复目标 */
const replyTarget = ref<{ messageId: string; content: string; senderName: string } | null>(null)

function canReply(msg: WsMessage): boolean {
  return !msg.isDeleted
}

function startReply(msg: WsMessage) {
  replyTarget.value = {
    messageId: msg.messageId,
    content: (msg.content || '').slice(0, 50),
    senderName: msg.senderName || '对方'
  }
  inputEl.value?.focus()
}

function recallMessage(msg: WsMessage) {
  if (!currentChat.value || !auth.user) return
  if (!window.confirm('确定撤回这条消息？')) return
  ws.sendMessage({
    type: 'message_recalled',
    from: String(auth.user.id),
    to: String(currentChat.value.id),
    content: msg.messageId,
    timestamp: Date.now(),
    messageId: msg.messageId,
    messageType: 0,
    senderName: '',
    senderAvatar: null
  })
  // 本地乐观标记
  chatStore.markMessageRecalled(
    chatStore.sessionKey(currentChat.value.type, currentChat.value.id),
    msg.messageId
  )
}

// ==================== 会话设置（置顶/免打扰） ====================
function openSessionSetting(s: SessionInfo) {
  sessionSettingTarget.value = s
  sessionSettingError.value = ''
  sessionSettingSuccess.value = ''
  showSessionSetting.value = true
}

async function toggleSessionPinned(e: Event) {
  if (!sessionSettingTarget.value) return
  const checked = (e.target as HTMLInputElement).checked
  await applySessionSetting({ isPinned: checked })
}

async function toggleSessionMuted(e: Event) {
  if (!sessionSettingTarget.value) return
  const checked = (e.target as HTMLInputElement).checked
  await applySessionSetting({ muted: checked })
}

async function applySessionSetting(patch: { isPinned?: boolean; muted?: boolean }) {
  const target = sessionSettingTarget.value
  if (!target) return
  sessionSettingError.value = ''
  sessionSettingSuccess.value = ''
  const res = await chatStore.updateSessionSetting(target.type, target.id, patch)
  if (res.success) {
    // 同步本地目标对象（开关状态回显）
    if (patch.isPinned !== undefined) target.isPinned = patch.isPinned
    if (patch.muted !== undefined) target.muted = patch.muted
    sessionSettingSuccess.value = '设置已保存'
  } else {
    sessionSettingError.value = res.message
  }
}

// ==================== @ 提及 ====================
/** 输入框按键：@ 键直接打开成员面板（双保险，避免 input 事件异常），Enter 发送 */
function onInputKeydown(e: KeyboardEvent) {
  if (e.key === 'Enter') {
    onInputEnter(e)
    return
  }
  if ((e.key === '@' || e.key === '＠') && currentChat.value?.type === 'group') {
    mentionOpen.value = true
    mentionQuery.value = ''
    if (groupStore.members.length === 0) {
      groupStore.fetchMembers(currentChat.value.id)
    }
  }
}

/** 输入变化时检测 @ 触发成员选择（仅群聊），兼容全角 ＠ */
function onInputChange() {
  const el = inputEl.value
  if (!el || currentChat.value?.type !== 'group') {
    mentionOpen.value = false
    return
  }
  const text = el.value
  const caret = el.selectionStart ?? text.length
  // 向前找最近的非邮箱 @（前面是空白或开头；兼容全角 ＠）
  const atIdx = Math.max(
    text.lastIndexOf('@', caret - 1),
    text.lastIndexOf('＠', caret - 1)
  )
  if (atIdx >= 0 && (atIdx === 0 || /\s/.test(text[atIdx - 1]))) {
    const token = text.slice(atIdx + 1, caret)
    if (!token.includes(' ')) {
      mentionOpen.value = true
      mentionQuery.value = token
      // 成员列表尚未加载时兜底拉取（进入群聊时通常已加载；拉取幂等）
      if (groupStore.members.length === 0) {
        groupStore.fetchMembers(currentChat.value.id)
      }
      return
    }
  }
  mentionOpen.value = false
}

/** 选择成员：替换 @token 为 @昵称，保留光标 */
function pickMention(m: GroupMemberInfo) {
  const el = inputEl.value
  if (!el) return
  const text = el.value
  const caret = el.selectionStart ?? text.length
  const atIdx = Math.max(text.lastIndexOf('@', caret - 1), text.lastIndexOf('＠', caret - 1))
  const insert = `@${m.nickname} `
  const start = atIdx >= 0 ? atIdx : caret
  inputText.value = text.slice(0, start) + insert + text.slice(caret)
  mentionOpen.value = false
  nextTick(() => {
    el.focus()
    const pos = start + insert.length
    el.setSelectionRange(pos, pos)
  })
}

/** @ 快捷按钮：光标处插入 @ 并打开成员面板（不依赖键盘输入） */
function openMentionPicker() {
  if (!currentChat.value || currentChat.value.type !== 'group') return
  if (groupStore.members.length === 0) {
    groupStore.fetchMembers(currentChat.value.id)
  }
  const el = inputEl.value
  if (el) {
    const caret = el.selectionStart ?? inputText.value.length
    const end = el.selectionEnd ?? caret
    inputText.value = inputText.value.slice(0, caret) + '@' + inputText.value.slice(end)
    nextTick(() => {
      el.focus()
      const pos = caret + 1
      el.setSelectionRange(pos, pos)
    })
  }
  mentionOpen.value = true
  mentionQuery.value = ''
}

/** 解析文本中的 @昵称 为成员账号 ID 列表 */
function parseMentions(text: string): number[] {
  if (currentChat.value?.type !== 'group') return []
  const ids: number[] = []
  for (const m of groupStore.members) {
    if (text.includes(`@${m.nickname}`) && !ids.includes(m.userId)) {
      ids.push(m.userId)
    }
  }
  return ids
}

/** 渲染消息内容：转义 HTML 后高亮 @提及（防 XSS） */
function renderContent(content: string): string {
  const esc = content
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
  return esc.replace(/@([^\s@，。！？!?,]+)/g, '<span class="mention">@$1</span>')
}

/** 该消息是否提及了当前用户 */
function isMentioned(msg: WsMessage): boolean {
  if (!msg.mentions?.length || !auth.user) return false
  return msg.mentions.includes(auth.user.id)
}

// 诊断辅助（排障用）：浏览器控制台执行 window.__mentionDebug() 查看 @ 功能状态
;(window as unknown as Record<string, unknown>).__mentionDebug = () => ({
  path: location.pathname,
  chatType: currentChat.value?.type,
  chatId: currentChat.value?.id,
  memberCount: groupStore.members.length,
  members: groupStore.members.map(m => m.nickname),
  mentionOpen: mentionOpen.value,
  mentionQuery: mentionQuery.value,
  inputValue: inputEl.value?.value ?? null,
  hasInputEvents: true
})

function scrollToBottom() {
  nextTick(() => {
    if (msgContainer.value) {
      msgContainer.value.scrollTop = msgContainer.value.scrollHeight
    }
  })
}

/** 会话最后一条消息预览 */
function lastMessageFor(type: ChatType, id: number): string {
  const key = chatStore.sessionKey(type, id)
  const list = chatStore.messages.get(key)
  const last = list && list.length ? list[list.length - 1] : null
  if (!last) return ''
  const prefix = type === 'group' && Number(last.from) !== auth.user?.id ? `${last.senderName}: ` : ''
  return prefix + (last.content || '')
}

function unreadOf(type: ChatType, id: number): number {
  return chatStore.unreadCounts.get(chatStore.sessionKey(type, id)) || 0
}

function selectPrivateChat(friend: { userId: number; nickname: string }) {
  currentChat.value = { type: 'private', id: friend.userId, name: friend.nickname }
  chatStore.setCurrentSession('private', friend.userId, friend.nickname)
  chatStore.loadHistory('private', friend.userId, 1, auth.user?.id)
  chatStore.markSessionRead('private', friend.userId)
  // 通知对方：该会话已读
  sendReadReceipt(friend.userId)
  mobileChatOpen.value = true
  scrollToBottom()
}

/** 判断是否是我发出的私聊消息（展示已读状态） */
function isMyPrivateMessage(msg: WsMessage): boolean {
  return currentChat.value?.type === 'private' && Number(msg.from) === auth.user?.id
}

/** 发送已读回执（to = 对方） */
function sendReadReceipt(peerId: number) {
  if (!auth.user || !ws.connected) return
  ws.sendMessage({
    type: 'read_receipt',
    from: String(auth.user.id),
    to: String(peerId),
    content: 'all',
    timestamp: Date.now(),
    messageId: '',
    messageType: 0,
    senderName: '',
    senderAvatar: null
  })
}

function selectGroupChat(group: { id: number; name: string }) {
  currentChat.value = { type: 'group', id: group.id, name: group.name }
  chatStore.setCurrentSession('group', group.id, group.name)
  chatStore.loadHistory('group', group.id)
  chatStore.markSessionRead('group', group.id)
  // 预加载群成员（@ 选择器与成员面板共用）
  groupStore.fetchMembers(group.id)
  mobileChatOpen.value = true
  scrollToBottom()
}

function selectSession(s: SessionInfo) {
  if (s.type === 'private') {
    selectPrivateChat({ userId: s.id, nickname: s.name })
  } else {
    selectGroupChat({ id: s.id, name: s.name })
  }
}

/** 移动端：返回会话列表 */
function backToList() {
  mobileChatOpen.value = false
}

/** 会话预览：优先本地最新消息，否则用服务端会话数据 */
function previewFor(s: SessionInfo): string {
  const local = lastMessageFor(s.type, s.id)
  return local || s.lastMessage || (s.type === 'group' ? '群组会话' : '暂无消息')
}

/** 会话时间：本地消息用毫秒时间戳，服务端数据用 ISO 字符串 */
function timeFor(s: SessionInfo): string {
  const key = chatStore.sessionKey(s.type, s.id)
  const list = chatStore.messages.get(key)
  const last = list && list.length ? list[list.length - 1] : null
  const t = last ? last.timestamp : s.lastTime ? new Date(s.lastTime).getTime() : 0
  if (!t) return ''
  const d = new Date(t)
  const now = new Date()
  const sameDay = d.toDateString() === now.toDateString()
  return sameDay
    ? `${pad(d.getHours())}:${pad(d.getMinutes())}`
    : `${d.getMonth() + 1}/${d.getDate()}`
}

function handleWsMessage(msg: WsMessage) {
  // 新好友申请：刷新申请列表
  if (msg.type === 'friend_request') {
    friendStore.fetchPendingRequests()
    return
  }
  // 好友申请被接受/拒绝：双方刷新好友与申请列表
  if (msg.type === 'friend_accepted' || msg.type === 'friend_rejected') {
    friendStore.fetchFriends()
    friendStore.fetchPendingRequests()
    chatStore.fetchSessions()
    return
  }
  // 被邀请加入群组：刷新群列表与会话
  if (msg.type === 'group_invited') {
    groupStore.fetchGroups()
    chatStore.fetchSessions()
    return
  }
  // 在线状态已由 onStatusChange 处理
  if (msg.type === 'online_status') return

  // 已读回执：对方读了我发出的私聊消息
  if (msg.type === 'read_receipt') {
    if (msg.from) {
      chatStore.markSessionReadByPeer(chatStore.sessionKey('private', Number(msg.from)))
    }
    return
  }

  // 消息撤回：标记本地对应消息
  if (msg.type === 'message_recalled') {
    const me = auth.user?.id
    const targetId = msg.content || msg.messageId
    if (me && targetId) {
      // 群聊优先（to 为群 ID），否则按私聊对方
      const gKey = chatStore.sessionKey('group', Number(msg.to))
      if (chatStore.messages.get(gKey)) {
        chatStore.markMessageRecalled(gKey, targetId)
      } else {
        const peer = Number(msg.from) === me ? Number(msg.to) : Number(msg.from)
        chatStore.markMessageRecalled(chatStore.sessionKey('private', peer), targetId)
      }
    }
    return
  }

  const { key, isNew } = chatStore.addMessage(msg, auth.user?.id)
  const currentKey = currentChat.value ? chatStore.sessionKey(currentChat.value.type, currentChat.value.id) : ''
  if (key === currentKey) {
    // 当前会话：清未读、标记已读、滚到底部；对方发来的消息即时回已读回执
    const chat = currentChat.value
    if (chat) {
      chatStore.markSessionRead(chat.type, chat.id)
      if (msg.type === 'private_message' && chat.type === 'private') {
        sendReadReceipt(chat.id)
      }
    }
    scrollToBottom()
  } else if (isNew) {
    // 免打扰会话不增加未读提醒、不播放提示音
    const sep = key.indexOf('_')
    const sType = key.slice(0, sep) as ChatType
    const sId = Number(key.slice(sep + 1))
    if (!chatStore.isSessionMuted(sType, sId)) {
      chatStore.bumpUnread(key)
      playNotifySound()
    }
  }
}

function genId(): string {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) return crypto.randomUUID()
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`
}

function send() {
  if (!currentChat.value || !auth.user) return
  const text = inputText.value.trim()
  if (!text) return
  if (!ws.connected) {
    sendHint.value = '连接已断开，正在重连，请稍候…'
    return
  }
  sendHint.value = ''
  const msg: WsMessage = {
    type: currentChat.value.type === 'private' ? 'private_message' : 'group_message',
    from: String(auth.user.id),
    to: String(currentChat.value.id),
    content: text,
    timestamp: Date.now(),
    messageId: genId(), // 客户端 ID，服务端回显时保留，用于去重
    messageType: 0,
    senderName: auth.user.nickname,
    senderAvatar: auth.user.avatar,
    mentions: parseMentions(text), // 群聊 @ 提及
    replyTo: replyTarget.value?.messageId,
    replyContent: replyTarget.value?.content,
    replySender: replyTarget.value?.senderName
  }
  chatStore.addMessage(msg, auth.user.id) // 乐观插入，回显到达后自动去重
  ws.sendMessage(msg)
  inputText.value = ''
  replyTarget.value = null // 发送后清除引用
  scrollToBottom()
}

function openAddModal() {
  modalError.value = ''
  modalSuccess.value = ''
  showAddModal.value = true
}

function openRequestsModal() {
  requestError.value = ''
  showRequestsModal.value = true
}

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

async function acceptRequest(r: FriendRequestInfo) {
  handlingRequestId.value = r.id
  requestError.value = ''
  try {
    const res = await friendStore.acceptRequest(r.id)
    if (!res.success) requestError.value = res.message
  } finally {
    handlingRequestId.value = null
  }
}

async function rejectRequest(r: FriendRequestInfo) {
  handlingRequestId.value = r.id
  requestError.value = ''
  try {
    const res = await friendStore.rejectRequest(r.id)
    if (!res.success) requestError.value = res.message
  } finally {
    handlingRequestId.value = null
  }
}

// ==================== 群成员管理 ====================
function roleText(role: number): string {
  return role === 0 ? '群主' : role === 1 ? '管理员' : ''
}

function canKick(m: GroupMemberInfo): boolean {
  if (!canInvite.value) return false
  if (m.userId === auth.user?.id) return false
  if (m.role === 0) return false // 不能踢群主
  const me = groupStore.members.find(x => x.userId === auth.user?.id)
  if (me && me.role === 1 && m.role === 1) return false // 管理员不能踢管理员
  return true
}

function openMembersModal() {
  if (!currentChat.value) return
  inviteError.value = ''
  inviteSuccess.value = ''
  showInviteModal.value = false
  groupStore.fetchMembers(currentChat.value.id)
  showMembersModal.value = true
}

function openInviteModal() {
  selectedInviteIds.value = []
  inviteError.value = ''
  inviteSuccess.value = ''
  showInviteModal.value = true
}

async function doInvite() {
  if (!currentChat.value || selectedInviteIds.value.length === 0) return
  inviting.value = true
  inviteError.value = ''
  inviteSuccess.value = ''
  try {
    const res = await groupStore.inviteMembers(currentChat.value.id, selectedInviteIds.value)
    if (res.success) {
      inviteSuccess.value = res.message
      selectedInviteIds.value = []
      await groupStore.fetchMembers(currentChat.value.id)
      groupStore.fetchGroups()
    } else {
      inviteError.value = res.message
    }
  } finally {
    inviting.value = false
  }
}

async function kickGroupMember(m: GroupMemberInfo) {
  if (!currentChat.value) return
  if (!window.confirm(`确定将 ${m.nickname} 踢出群组？`)) return
  const res = await groupStore.kickMember(currentChat.value.id, m.userId)
  if (res.success) {
    await groupStore.fetchMembers(currentChat.value.id)
    groupStore.fetchGroups()
  } else {
    inviteError.value = res.message
  }
}

/** 点击账号 ID 复制到剪贴板 */
async function copyAccountId() {
  if (!auth.user) return
  try {
    await navigator.clipboard.writeText(String(auth.user.id))
    copyTip.value = '已复制 ✅'
  } catch {
    copyTip.value = 'ID: ' + auth.user.id
  }
  if (copyTipTimer) clearTimeout(copyTipTimer)
  copyTipTimer = window.setTimeout(() => {
    copyTip.value = auth.user ? `ID: ${auth.user.id}` : ''
  }, 2000)
}

// ==================== 好友设置（备注/分类） ====================
function openFriendSetting(f: FriendInfo) {
  friendSetting.value = f
  friendRemark.value = f.remark || ''
  friendCategory.value = f.category || ''
  friendTagError.value = ''
  friendTagSuccess.value = ''
  showFriendSetting.value = true
}

async function saveFriendTag() {
  if (!friendSetting.value) return
  savingFriendTag.value = true
  friendTagError.value = ''
  friendTagSuccess.value = ''
  try {
    const remarkRes = await friendStore.setRemark(friendSetting.value.userId, friendRemark.value)
    if (!remarkRes.success) {
      friendTagError.value = remarkRes.message
      return
    }
    const catRes = await friendStore.setCategory(friendSetting.value.userId, friendCategory.value)
    if (!catRes.success) {
      friendTagError.value = catRes.message
      return
    }
    friendTagSuccess.value = '已保存'
    // 若正在与该好友聊天，更新会话显示名
    if (currentChat.value?.type === 'private' && currentChat.value.id === friendSetting.value.userId) {
      currentChat.value.name = friendDisplayName(friendSetting.value)
    }
  } finally {
    savingFriendTag.value = false
  }
}

function handleLogout() {
  ws.disconnect()
  auth.logout()
  router.push('/login')
}

// ==================== 个人资料 ====================
function openProfileModal() {
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
  showProfileModal.value = true
}

function triggerAvatarInput() {
  avatarInputRef.value?.click()
}

async function onAvatarChange(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  profileError.value = ''
  profileSuccess.value = ''
  try {
    const res = await auth.uploadAvatar(file)
    if (res.success) profileSuccess.value = res.message || '头像修改成功'
    else profileError.value = res.message
  } catch (err: any) {
    profileError.value = err?.message || '头像上传失败'
  } finally {
    input.value = ''
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

watch(activeTab, () => {
  if (activeTab.value === 'friends') friendStore.fetchFriends()
  else if (activeTab.value === 'groups') groupStore.fetchGroups()
  else chatStore.fetchSessions()
})
</script>

<style scoped>
.chat-layout {
  display: flex;
  height: 100vh;
  height: 100dvh;
}

/* ==================== 侧边栏 ==================== */
.sidebar {
  width: 320px;
  background: var(--bg-white);
  border-right: 1px solid var(--border);
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
}

.sidebar-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 16px;
}

.user-info {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
  cursor: pointer;
  padding: 4px 6px;
  margin: -4px -6px;
  border-radius: 10px;
  transition: background 0.15s;
}

.user-info:hover {
  background: var(--bg-hover);
}

.nickname {
  font-weight: 600;
  font-size: 15px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.user-text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.account-id {
  font-size: 12px;
  color: var(--text-secondary);
  cursor: pointer;
  user-select: none;
  line-height: 1.4;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  transition: color 0.15s;
}

.account-id:hover {
  color: var(--primary);
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 2px;
}

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
}

.icon-btn:hover {
  background: var(--bg-hover);
  color: var(--text);
}

.badge {
  position: absolute;
  top: 2px;
  right: 2px;
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  border-radius: 8px;
  background: var(--danger);
  color: white;
  font-size: 10.5px;
  line-height: 16px;
  text-align: center;
  border: 1.5px solid var(--bg-white);
}

/* Tab 切换 */
.tabs {
  display: flex;
  gap: 4px;
  padding: 2px 12px 10px;
}

.tab {
  flex: 1;
  padding: 8px 0;
  border: none;
  border-radius: 10px;
  background: transparent;
  cursor: pointer;
  font-size: 14px;
  font-weight: 500;
  color: var(--text-secondary);
  transition: all 0.2s;
}

.tab:hover {
  color: var(--text);
  background: var(--bg-hover);
}

.tab.active {
  background: var(--active-bg);
  color: var(--primary);
  font-weight: 600;
}

/* 操作栏 */
.action-bar {
  padding: 0 12px 12px;
}

.action-bar .btn {
  width: 100%;
  background: var(--mine-bubble);
  box-shadow: 0 4px 12px rgba(91, 108, 255, 0.28);
}

.action-bar .btn:hover {
  filter: brightness(1.06);
}

/* 列表 */
.contact-list {
  flex: 1;
  overflow-y: auto;
  padding-bottom: 8px;
}

.contact-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 14px;
  margin: 2px 10px;
  border-radius: 12px;
  cursor: pointer;
  transition: background 0.15s;
}

.contact-item:hover {
  background: var(--bg-hover);
}

.contact-item.active {
  background: var(--active-bg);
}

.avatar-wrap {
  position: relative;
  flex-shrink: 0;
}

/* 群聊标识：头像右下角徽标 */
.group-badge {
  position: absolute;
  right: -2px;
  bottom: -2px;
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  border-radius: 5px;
  background: var(--primary);
  color: white;
  font-size: 10px;
  font-weight: 600;
  line-height: 16px;
  text-align: center;
  border: 1.5px solid var(--bg-white);
}

/* 聊天标题行 */
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

.status-dot {
  position: absolute;
  right: -1px;
  bottom: -1px;
  width: 11px;
  height: 11px;
  border-radius: 50%;
  border: 2px solid var(--bg-white);
}

.status-dot.online { background: var(--online); }
.status-dot.offline { background: var(--offline); }

.contact-info {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.info-top,
.info-bottom {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.contact-name {
  font-weight: 600;
  font-size: 15px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.contact-meta {
  font-size: 12.5px;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.session-time {
  font-size: 11.5px;
  color: var(--text-secondary);
  flex-shrink: 0;
}

.unread-badge {
  min-width: 18px;
  height: 18px;
  padding: 0 5px;
  border-radius: 9px;
  background: var(--danger);
  color: white;
  font-size: 11px;
  line-height: 18px;
  text-align: center;
  flex-shrink: 0;
}

/* 好友分组 */
.group-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 14px 16px 4px;
  font-size: 12px;
  font-weight: 600;
  color: var(--text-secondary);
}

.group-count {
  font-weight: 400;
  font-size: 11px;
  background: var(--bg-hover);
  border-radius: 8px;
  padding: 0 6px;
  line-height: 16px;
}

.friend-more {
  width: 26px;
  height: 26px;
  border: none;
  border-radius: 50%;
  background: transparent;
  color: var(--text-secondary);
  font-size: 16px;
  line-height: 1;
  cursor: pointer;
  flex-shrink: 0;
  opacity: 0;
  transition: all 0.15s;
}

.contact-item:hover .friend-more,
.friend-more:focus-visible {
  opacity: 1;
}

.friend-more:hover {
  background: var(--bg-hover);
  color: var(--text);
}

/* 好友设置弹窗 */
.friend-setting-head {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 4px 0 8px;
}

.friend-setting-names {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.friend-setting-head .request-name {
  font-size: 15px;
}

.set-label {
  font-size: 13px;
  color: var(--text-secondary);
  font-weight: 500;
}

.cat-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

/* 会话项图标 */
.pin-icon,
.mute-icon {
  font-size: 11px;
  margin-right: 2px;
}

/* 会话设置开关 */
.setting-switch {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 10px 4px;
  border-bottom: 1px solid var(--border);
  cursor: pointer;
}

.setting-switch:last-of-type {
  border-bottom: none;
}

.setting-switch > span {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.setting-desc {
  font-size: 12px;
  color: var(--text-secondary);
}

.setting-switch input[type='checkbox'] {
  width: 18px;
  height: 18px;
  accent-color: var(--primary);
  flex-shrink: 0;
  cursor: pointer;
}

.chip {
  padding: 5px 14px;
  border: 1px solid var(--border);
  border-radius: 16px;
  background: var(--bg-white);
  color: var(--text-secondary);
  font-size: 13px;
  cursor: pointer;
  transition: all 0.15s;
}

.chip:hover {
  border-color: var(--primary-light);
  color: var(--primary);
}

.chip.active {
  background: var(--active-bg);
  border-color: var(--primary);
  color: var(--primary);
  font-weight: 500;
}

.empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  padding: 48px 20px;
  color: var(--text-secondary);
  font-size: 13.5px;
  text-align: center;
}

.empty-icon {
  font-size: 40px;
  opacity: 0.55;
}

/* ==================== 聊天区域 ==================== */
.chat-main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  background: var(--bg);
}

.no-chat {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
  color: var(--text-secondary);
  font-size: 15px;
}

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

.chat-messages {
  flex: 1;
  overflow-y: auto;
  padding: 18px 20px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  background: var(--chat-bg);
}

/* 消息行 */
.msg-row {
  display: flex;
  gap: 10px;
  align-items: flex-start;
  max-width: 78%;
}

.msg-row.mine {
  align-self: flex-end;
  flex-direction: row-reverse;
}

.msg-avatar {
  width: 34px;
  height: 34px;
  font-size: 14px;
}

.msg-row.mine .msg-avatar {
  display: none;
}

.msg-body {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 3px;
  min-width: 0;
}

.msg-row.mine .msg-body {
  align-items: flex-end;
}

.msg-sender {
  font-size: 12px;
  color: var(--text-secondary);
  margin-left: 2px;
}

.msg-bubble {
  padding: 9px 13px;
  border-radius: 14px;
  font-size: 14.5px;
  line-height: 1.55;
  word-break: break-word;
  box-shadow: var(--shadow-sm);
}

.msg-row:not(.mine) .msg-bubble {
  background: var(--bg-white);
  border-top-left-radius: 4px;
}

.msg-row.mine .msg-bubble {
  background: var(--mine-bubble);
  color: white;
  border-top-right-radius: 4px;
}

.msg-time {
  font-size: 11px;
  color: var(--text-secondary);
  padding: 0 4px;
}

/* 已读状态 */
.msg-meta-line {
  display: flex;
  align-items: center;
  gap: 2px;
}

.msg-status {
  font-size: 11px;
  color: var(--text-secondary);
  padding: 0 4px;
}

.msg-status.read {
  color: var(--primary);
  font-weight: 500;
}

/* 撤回 */
.msg-bubble.recalled {
  background: var(--bg-hover);
  color: var(--text-secondary);
  box-shadow: none;
  font-size: 13px;
  font-style: italic;
}

.msg-recalled-text {
  padding: 0 6px;
}

.msg-recall-btn {
  display: none;
  border: none;
  background: var(--bg-hover);
  color: var(--text-secondary);
  font-size: 11.5px;
  padding: 3px 10px;
  border-radius: 8px;
  cursor: pointer;
  align-self: flex-end;
  margin-top: -6px;
}

.msg-row:hover .msg-recall-btn {
  display: inline-block;
}

.msg-recall-btn:hover {
  background: var(--border);
  color: var(--text);
}

/* 引用回复 */
.reply-preview {
  display: flex;
  flex-direction: column;
  gap: 1px;
  max-width: 100%;
  padding: 4px 10px;
  border-left: 3px solid var(--primary-light);
  background: var(--bg-hover);
  border-radius: 8px 8px 0 0;
  margin-bottom: 2px;
}

.reply-sender {
  font-size: 11.5px;
  color: var(--primary);
  font-weight: 500;
}

.reply-text {
  font-size: 12px;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 260px;
}

.msg-actions {
  display: none;
  gap: 6px;
  align-self: flex-end;
  margin-top: -6px;
}

.msg-row:hover .msg-actions {
  display: inline-flex;
}

.reply-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 16px;
  background: var(--bg-hover);
  border-top: 1px solid var(--border);
  font-size: 12.5px;
  color: var(--text-secondary);
}

.reply-bar-text {
  flex: 1;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.reply-cancel {
  border: none;
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  font-size: 13px;
  flex-shrink: 0;
}

.reply-cancel:hover {
  color: var(--danger);
}

/* @ 提及高亮 */
.mention {
  color: var(--primary);
  font-weight: 600;
}

/* 图片消息 */
.msg-bubble.is-image {
  padding: 4px;
  background: transparent;
  border: none;
  box-shadow: none;
}

.msg-image {
  display: block;
  max-width: 260px;
  max-height: 260px;
  border-radius: 12px;
  cursor: zoom-in;
  box-shadow: var(--shadow-sm);
  object-fit: cover;
}

@media (max-width: 720px) {
  .msg-image {
    max-width: 220px;
    max-height: 220px;
  }
}

/* 图片放大预览 */
.lightbox {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.85);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 200;
  cursor: zoom-out;
  animation: fade-in 0.2s;
}

.lightbox img {
  max-width: 92vw;
  max-height: 88vh;
  border-radius: 8px;
  object-fit: contain;
}

.lightbox-close {
  position: absolute;
  bottom: 24px;
  left: 50%;
  transform: translateX(-50%);
  color: rgba(255, 255, 255, 0.7);
  font-size: 13px;
}

/* 被 @ 的消息：主色描边标记 */
.msg-bubble.mentioned {
  outline: 2px solid rgba(91, 108, 255, 0.5);
  outline-offset: -2px;
}

/* @ 成员选择浮层 */
.mention-panel {
  position: absolute;
  bottom: calc(100% + 10px);
  left: 12px;
  width: 260px;
  max-width: calc(100vw - 24px);
  max-height: 280px;
  overflow-y: auto;
  background: var(--bg-white);
  border: 1px solid var(--border);
  border-radius: 12px;
  box-shadow: var(--shadow);
  padding: 6px;
  z-index: 20;
  animation: modal-in 0.15s;
}

.mention-item {
  display: flex;
  align-items: center;
  gap: 10px;
  width: 100%;
  padding: 7px 8px;
  border: none;
  background: transparent;
  border-radius: 8px;
  cursor: pointer;
  font-size: 13px;
  text-align: left;
  transition: background 0.12s;
}

.mention-item:hover {
  background: var(--bg-hover);
}

.mention-item .avatar.small {
  width: 28px;
  height: 28px;
  font-size: 12px;
}

.mention-role {
  font-size: 11px;
  color: var(--primary);
  margin-left: auto;
  flex-shrink: 0;
}

/* @ 快捷按钮 */
.mention-btn:hover {
  color: var(--primary);
}

.at-symbol {
  font-size: 20px;
  font-weight: 700;
  line-height: 1;
}

/* 输入区 */
.chat-input-bar {
  position: relative;
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 12px 16px calc(12px + env(safe-area-inset-bottom));
  background: var(--bg-white);
  border-top: 1px solid var(--border);
}

.emoji-btn {
  width: 38px;
  height: 38px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: none;
  border-radius: 50%;
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  transition: all 0.2s;
  flex-shrink: 0;
}

.emoji-btn:hover {
  background: var(--bg-hover);
  color: #f7b731;
}

.emoji-btn:active {
  transform: scale(0.92);
}

/* 表情面板 */
.emoji-panel {
  position: absolute;
  bottom: calc(100% + 10px);
  left: 12px;
  width: 340px;
  max-width: calc(100vw - 24px);
  max-height: 280px;
  overflow-y: auto;
  background: var(--bg-white);
  border: 1px solid var(--border);
  border-radius: 14px;
  box-shadow: var(--shadow);
  padding: 10px 12px 12px;
  z-index: 20;
  animation: modal-in 0.18s;
}

.emoji-group-title {
  font-size: 12px;
  color: var(--text-secondary);
  padding: 8px 2px 4px;
  font-weight: 500;
}

.emoji-group:first-child .emoji-group-title {
  padding-top: 2px;
}

.emoji-grid {
  display: grid;
  grid-template-columns: repeat(8, 1fr);
  gap: 2px;
}

.emoji-item {
  width: 100%;
  aspect-ratio: 1;
  border: none;
  background: transparent;
  border-radius: 8px;
  font-size: 21px;
  line-height: 1;
  cursor: pointer;
  transition: background 0.12s, transform 0.12s;
  display: flex;
  align-items: center;
  justify-content: center;
}

.emoji-item:hover {
  background: var(--bg-hover);
  transform: scale(1.15);
}

.emoji-item:active {
  transform: scale(0.92);
}

.chat-input-bar .input {
  flex: 1;
  border-radius: 20px;
  background: var(--bg-hover);
  border-color: transparent;
}

.chat-input-bar .input:focus {
  background: var(--bg-white);
}

.send-btn {
  padding: 9px 22px;
  border: none;
  border-radius: 20px;
  background: var(--mine-bubble);
  color: white;
  font-weight: 600;
  font-size: 14px;
  cursor: pointer;
  box-shadow: 0 4px 12px rgba(91, 108, 255, 0.35);
  transition: all 0.2s;
  flex-shrink: 0;
}

.send-btn:hover:not(:disabled) {
  filter: brightness(1.08);
}

.send-btn:disabled {
  opacity: 0.45;
  cursor: not-allowed;
  box-shadow: none;
}

.send-hint {
  padding: 0 20px 10px;
  font-size: 12px;
  color: var(--danger);
  background: var(--bg-white);
}

/* ==================== 弹窗 ==================== */
.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(15, 18, 26, 0.45);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 100;
  animation: fade-in 0.2s;
}

.modal {
  background: var(--bg-white);
  padding: 24px;
  border-radius: 16px;
  width: 420px;
  max-width: calc(100vw - 40px);
  max-height: 72vh;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 14px;
  box-shadow: var(--shadow);
  animation: modal-in 0.25s;
}

.modal h3 { font-size: 18px; }

.modal-error { color: var(--danger); font-size: 13px; }
.modal-success { color: var(--success); font-size: 13px; }

/* 好友申请列表 */
.request-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 0;
  border-bottom: 1px solid var(--border);
}

.request-item:last-of-type {
  border-bottom: none;
}

.request-info {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.request-name {
  font-weight: 600;
  font-size: 14px;
  display: flex;
  align-items: center;
  gap: 6px;
}

.request-meta {
  font-size: 12px;
  color: var(--text-secondary);
  display: flex;
  align-items: center;
  gap: 5px;
}

.request-meta .status-dot {
  position: static;
  width: 8px;
  height: 8px;
  border: none;
}

.role-tag {
  font-size: 11px;
  font-weight: 500;
  padding: 1px 7px;
  border-radius: 8px;
  flex-shrink: 0;
}

.role-tag.role-0 {
  background: var(--active-bg);
  color: var(--primary);
}

.role-tag.role-1 {
  background: #f0fdf4;
  color: #16a34a;
}

/* 群成员管理 */
.invite-bar {
  padding: 2px 0 4px;
}

.invite-bar .btn {
  width: 100%;
}

.kick-btn {
  color: var(--danger);
  flex-shrink: 0;
}

.kick-btn:hover {
  background: rgba(245, 108, 108, 0.1);
  color: var(--danger);
}

.friend-check {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 10px;
  border-radius: 10px;
  cursor: pointer;
  transition: background 0.15s;
}

.friend-check:hover {
  background: var(--bg-hover);
}

.friend-check input[type='checkbox'] {
  width: 16px;
  height: 16px;
  accent-color: var(--primary);
  flex-shrink: 0;
}

/* 个人资料弹窗 */
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

.request-actions {
  display: flex;
  gap: 8px;
}

.request-actions .btn {
  padding: 6px 14px;
  font-size: 12px;
}

/* ==================== 头像 ==================== */
.avatar {
  width: 44px;
  height: 44px;
  border-radius: 50%;
  color: white;
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: 600;
  font-size: 18px;
  flex-shrink: 0;
  user-select: none;
  box-shadow: inset 0 -2px 6px rgba(0, 0, 0, 0.08);
}

.avatar.small {
  width: 38px;
  height: 38px;
  font-size: 15px;
}

/* ==================== 移动端适配 ==================== */
@media (max-width: 720px) {
  .sidebar {
    width: 100%;
  }

  .sidebar.is-hidden {
    display: none;
  }

  .chat-main {
    display: none;
    width: 100%;
  }

  .chat-main.is-show {
    display: flex;
  }

  .back-btn {
    display: flex;
  }

  .msg-row {
    max-width: 85%;
  }

  .chat-messages {
    padding: 14px 12px;
  }

  .emoji-panel {
    left: 8px;
    right: 8px;
    width: auto;
  }

  .emoji-grid {
    grid-template-columns: repeat(7, 1fr);
  }

  .modal {
    padding: 20px;
  }
}

@media (min-width: 721px) {
  .back-btn {
    display: none;
  }
}
</style>
