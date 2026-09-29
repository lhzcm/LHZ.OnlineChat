<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { adminApi, type AdminInfo } from '@/api/admin'
import { useToast } from '@/composables/useToast'

const { toast } = useToast()
const admins = ref<AdminInfo[]>([])
const showCreate = ref(false)
const form = ref({ username: '', password: '', role: 1 })

async function load() {
  const res = await adminApi.listAdmins()
  if (res.success) admins.value = res.data
  else toast(res.message)
}

async function create() {
  if (!form.value.username.trim() || form.value.password.length < 6) {
    toast('账号至少 2 位、密码至少 6 位')
    return
  }
  const res = await adminApi.createAdmin(form.value.username.trim(), form.value.password, form.value.role)
  toast(res.message)
  if (res.success) {
    showCreate.value = false
    form.value = { username: '', password: '', role: 1 }
    await load()
  }
}

async function toggleStatus(a: AdminInfo) {
  const res = await adminApi.updateAdmin(a.id, { status: a.status === 1 ? 0 : 1 })
  toast(res.message)
  if (res.success) await load()
}

async function toggleRole(a: AdminInfo) {
  if (!confirm(`将「${a.username}」的角色切换为${a.role === 0 ? '运营管理员' : '超级管理员'}？`)) return
  const res = await adminApi.updateAdmin(a.id, { role: a.role === 0 ? 1 : 0 })
  toast(res.message)
  if (res.success) await load()
}

async function remove(a: AdminInfo) {
  if (!confirm(`确定删除管理员「${a.username}」？`)) return
  const res = await adminApi.deleteAdmin(a.id)
  toast(res.message)
  if (res.success) await load()
}

function fmtTime(t: string | null | undefined): string {
  return t ? new Date(t).toLocaleString('zh-CN', { hour12: false }) : '从未登录'
}

onMounted(load)
</script>

<template>
  <div>
    <h2 class="page-title">管理员管理</h2>
    <div class="toolbar">
      <div class="spacer"></div>
      <button class="btn btn-primary btn-sm" @click="showCreate = true">+ 创建管理员</button>
    </div>

    <div class="table-wrap">
      <table class="admin-table">
        <thead>
          <tr>
            <th>ID</th>
            <th>账号</th>
            <th>角色</th>
            <th>状态</th>
            <th>最近登录</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="a in admins" :key="a.id">
            <td>{{ a.id }}</td>
            <td>{{ a.username }}</td>
            <td><span class="tag" :class="a.role === 0 ? 'tag-info' : 'tag-ok'">{{ a.role === 0 ? '超级管理员' : '运营管理员' }}</span></td>
            <td><span class="tag" :class="a.status === 1 ? 'tag-ok' : 'tag-warn'">{{ a.status === 1 ? '启用' : '停用' }}</span></td>
            <td>{{ fmtTime(a.lastLoginAt) }}</td>
            <td>
              <button class="btn btn-sm" @click="toggleRole(a)">{{ a.role === 0 ? '设为运营' : '设为超管' }}</button>
              <button class="btn btn-sm" @click="toggleStatus(a)">{{ a.status === 1 ? '停用' : '启用' }}</button>
              <button class="btn btn-sm btn-danger" @click="remove(a)">删除</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div class="modal-overlay" v-if="showCreate" @click.self="showCreate = false">
      <div class="modal">
        <h3>创建管理员</h3>
        <div class="row">
          <label>账号</label>
          <input v-model="form.username" class="input" placeholder="2-50 个字符" />
        </div>
        <div class="row">
          <label>初始密码</label>
          <input v-model="form.password" class="input" type="text" placeholder="至少 6 位" />
        </div>
        <div class="row">
          <label>角色</label>
          <select v-model.number="form.role" class="input">
            <option :value="1">运营管理员（用户管理）</option>
            <option :value="0">超级管理员（全部权限）</option>
          </select>
        </div>
        <div class="actions">
          <button class="btn" @click="showCreate = false">取消</button>
          <button class="btn btn-primary" @click="create">创建</button>
        </div>
      </div>
    </div>
  </div>
</template>
