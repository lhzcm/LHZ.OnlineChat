<script setup lang="ts">
import { ref, computed } from 'vue'
import { groupApi } from '@/api/group'
import { useGroupStore } from '@/stores/group'
import { useAuthStore } from '@/stores/auth'
import { formatMsgTime } from '@/utils/format'

const props = defineProps<{ groupId: number }>()
const emit = defineEmits<{ close: []; saved: [] }>()
const groupStore = useGroupStore()
const auth = useAuthStore()

const group = computed(() => groupStore.groups.find(g => g.id === props.groupId) ?? null)
const announcement = computed(() => group.value?.announcement || '')
const announcementAt = computed(() => group.value?.announcementAt || '')
/** 我是否可编辑公告（群主或管理员） */
const canManage = computed(() => !!group.value && group.value.myRole <= 1)

const editing = ref(false)
const draft = ref('')
const saving = ref(false)
const error = ref('')
const success = ref('')

function startEdit() {
  draft.value = announcement.value
  error.value = ''
  success.value = ''
  editing.value = true
}

function cancelEdit() {
  editing.value = false
}

async function save() {
  saving.value = true
  error.value = ''
  success.value = ''
  try {
    const res = await groupApi.setAnnouncement(props.groupId, draft.value.trim())
    if (res.success) {
      success.value = res.message || '公告已更新'
      editing.value = false
      await groupStore.fetchGroups() // 刷新公告横幅
      emit('saved')
    } else {
      error.value = res.message
    }
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="modal-overlay" @click.self="emit('close')">
    <div class="modal">
      <h3>📢 群公告</h3>
      <p class="announce-meta" v-if="announcementAt">
        {{ group?.name }} · 更新于 {{ formatMsgTime(new Date(announcementAt).getTime()) }}
      </p>
      <p class="announce-full" v-if="!editing">{{ announcement || '暂无公告' }}</p>
      <textarea v-if="editing" v-model="draft" class="input announce-input" rows="5"
        maxlength="2000" placeholder="输入群公告内容…"></textarea>
      <div class="modal-actions" v-if="canManage">
        <button v-if="!editing" class="btn btn-sm btn-primary" @click="startEdit">编辑公告</button>
        <template v-else>
          <button class="btn btn-sm btn-primary" :disabled="saving" @click="save">
            {{ saving ? '保存中…' : '保存' }}
          </button>
          <button class="btn btn-sm btn-ghost" @click="cancelEdit">取消</button>
        </template>
      </div>
      <p class="modal-error" v-if="error">{{ error }}</p>
      <p class="modal-success" v-if="success">{{ success }}</p>
      <button class="btn btn-ghost" @click="emit('close')">关闭</button>
    </div>
  </div>
</template>

<style scoped>
.announce-meta {
  font-size: 12px;
  color: var(--text-secondary);
  margin-bottom: 10px;
}
.announce-full {
  white-space: pre-wrap;
  word-break: break-word;
  font-size: 14px;
  color: var(--text);
  max-height: 280px;
  overflow-y: auto;
  margin-bottom: 8px;
  line-height: 1.7;
}
.announce-input {
  width: 100%;
  resize: vertical;
  font-family: inherit;
}
</style>
