<template>
  <div class="login-page">
    <el-card class="login-card">
      <template #header>
        <div class="login-title">AI 自动化测试平台</div>
      </template>
      <el-form ref="formRef" :model="form" :rules="rules" label-width="0" @keyup.enter="handleLogin">
        <el-form-item prop="username">
          <el-input v-model="form.username" placeholder="用户名" :prefix-icon="User" />
        </el-form-item>
        <el-form-item prop="password">
          <el-input v-model="form.password" type="password" placeholder="密码" show-password
            :prefix-icon="Lock" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" class="login-btn" :loading="loading" @click="handleLogin">
            登 录
          </el-button>
        </el-form-item>
      </el-form>
      <template v-if="ssoProviders.length > 0">
        <el-divider>
          <span class="sso-divider-text">或使用以下方式登录</span>
        </el-divider>
        <div class="sso-buttons">
          <el-button
            v-for="p in ssoProviders"
            :key="p.id"
            class="sso-btn"
            plain
            @click="handleSso(p.authorizeUrl)"
          >
            {{ p.displayName }}
          </el-button>
        </div>
      </template>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { ElMessage, type FormInstance, type FormRules } from 'element-plus'
import { User, Lock } from '@element-plus/icons-vue'
import { useAuthStore } from '@/stores/auth'
import { ssoProvidersApi } from '@/api/auth'
import type { SsoProviderInfo } from '@/types/auth'

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()

const formRef = ref<FormInstance>()
const loading = ref(false)

const form = reactive({
  username: '',
  password: '',
})

const rules: FormRules = {
  username: [{ required: true, message: '请输入用户名', trigger: 'blur' }],
  password: [{ required: true, message: '请输入密码', trigger: 'blur' }],
}

// SSO：仅展示后端配置启用的方式；接口失败静默降级为纯密码登录
const ssoProviders = ref<SsoProviderInfo[]>([])
onMounted(async () => {
  try {
    ssoProviders.value = await ssoProvidersApi()
  } catch {
    ssoProviders.value = []
  }
})

const handleSso = (authorizeUrl: string) => {
  // 跳后端 /api/auth/sso/{provider}/authorize → 302 到企业扫码页 → 回调 /login/sso
  window.location.href = authorizeUrl
}

const handleLogin = async () => {
  try {
    await formRef.value?.validate()
  } catch {
    return
  }

  loading.value = true
  try {
    await authStore.login(form.username, form.password)
    ElMessage.success('登录成功')
    router.push((route.query.redirect as string) || '/')
  } catch {
    ElMessage.error('用户名或密码错误')
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.login-page {
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #1f3b73 0%, #2d5aa8 50%, #3f7fd4 100%);
}

.login-card {
  width: 380px;
}

.login-title {
  text-align: center;
  font-size: 20px;
  font-weight: 600;
}

.login-btn {
  width: 100%;
}

.sso-divider-text {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.sso-buttons {
  display: flex;
  gap: 12px;
}

.sso-btn {
  flex: 1;
}
</style>
