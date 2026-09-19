<template>
  <div class="sso-callback-page">
    <el-card class="sso-callback-card">
      <div v-if="error" class="sso-result">
        <el-result icon="error" :title="isBind ? '绑定失败' : 'SSO 登录失败'" :sub-title="error">
          <template #extra>
            <el-button type="primary" @click="goBack">{{ isBind ? '返回个人中心' : '返回登录页' }}</el-button>
          </template>
        </el-result>
      </div>
      <div v-else class="sso-loading">
        <el-icon class="is-loading sso-spinner"><Loading /></el-icon>
        <p>{{ isBind ? '正在完成绑定…' : '正在完成登录…' }}</p>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Loading } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { ssoBindApi, ssoLoginApi } from '@/api/auth'
import { useAuthStore } from '@/stores/auth'

// 同一回调组件服务两种流程（后端 authorize?mode=bind 决定企业授权回跳到哪个前端地址）：
//   /login/sso       —— 登录：code+state 换平台 JWT 进系统
//   /login/sso/bind  —— 绑定：已登录用户把企业身份挂到当前账号（个人中心入口）
const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

const isBind = computed(() => route.name === 'sso-bind-callback')
const error = ref('')

const goBack = () => router.replace({ name: isBind.value ? 'profile' : 'login' })

onMounted(async () => {
  const provider = route.query.provider as string | undefined
  const code = route.query.code as string | undefined
  const state = route.query.state as string | undefined

  if (!provider || !code || !state) {
    error.value = '回调参数不完整，请重新发起'
    return
  }

  try {
    if (isBind.value) {
      if (!authStore.token) {
        error.value = '登录会话已失效，请重新登录后再绑定'
        return
      }
      await ssoBindApi(provider, { code, state })
      ElMessage.success('绑定成功')
      // 让 store 里的用户信息带上新的绑定状态（个人中心卡片展示用）
      await authStore.refreshMe()
      router.replace({ name: 'profile' })
    } else {
      const res = await ssoLoginApi(provider, { code, state })
      authStore.setSession(res.token, res.user)
      ElMessage.success('登录成功')
      const redirect = (route.query.redirect as string) || '/'
      router.replace(redirect)
    }
  } catch (e) {
    const err = e as { response?: { data?: { message?: string } } }
    error.value = err.response?.data?.message || (isBind.value ? '绑定失败，请稍后重试' : '登录失败，请稍后重试')
  }
})
</script>

<style scoped>
.sso-callback-page {
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #1f3b73 0%, #2d5aa8 50%, #3f7fd4 100%);
}

.sso-callback-card {
  width: 380px;
}

.sso-loading {
  text-align: center;
  padding: 32px 0;
  color: var(--el-text-color-secondary);
}

.sso-spinner {
  font-size: 32px;
  margin-bottom: 12px;
}
</style>
