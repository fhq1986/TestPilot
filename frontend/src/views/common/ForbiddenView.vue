<template>
  <div class="forbidden">
    <el-result icon="warning" title="403" sub-title="当前账号无权访问该页面">
      <template #extra>
        <p class="hint">
          你的角色是「{{ auth.roleName || '未登录' }}」，缺少进入此页面所需的权限。
          如需访问，请联系管理员在「用户管理」中调整你的角色。
        </p>
        <el-button type="primary" @click="router.replace('/dashboard')">返回仪表盘</el-button>
        <el-button v-if="from" @click="router.back()">返回上一页</el-button>
      </template>
    </el-result>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const from = computed(() => (route.query.from as string) || '')
</script>

<style scoped>
.forbidden {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 60vh;
}

.hint {
  color: #7a8290;
  margin-bottom: 16px;
  line-height: 1.7;
}
</style>
