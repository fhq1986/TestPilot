<template>
  <div class="profile-page">
    <el-card class="panel">
      <template #header>
        <div class="panel-header">
          <span class="panel-title">个人中心</span>
          <span class="panel-sub">当前账号：{{ auth.user?.displayName }}（{{ auth.user?.username }}）</span>
        </div>
      </template>

      <el-tabs v-model="activeTab">
        <!-- ------------------------------ 修改密码 ------------------------------ -->
        <el-tab-pane label="修改密码" name="password">
          <el-form ref="formRef" :model="form" :rules="rules" label-width="100px" class="form">
            <el-form-item label="当前密码" prop="oldPassword">
              <el-input v-model="form.oldPassword" type="password" show-password placeholder="请输入当前密码" />
            </el-form-item>
            <el-form-item label="新密码" prop="newPassword">
              <el-input v-model="form.newPassword" type="password" show-password
                :placeholder="`至少 ${MIN_PASSWORD_LENGTH} 位，含字母/数字/符号至少两类`" />
            </el-form-item>
            <el-form-item label="确认新密码" prop="confirmPassword">
              <el-input v-model="form.confirmPassword" type="password" show-password
                placeholder="请再次输入新密码" />
            </el-form-item>

            <el-alert type="warning" :closable="false" class="tip">
              <template #title>
                修改成功后，包括当前设备在内的所有登录会话都会失效，需要重新登录。
              </template>
            </el-alert>

            <el-form-item class="actions">
              <el-button type="primary" :loading="saving" @click="handleSubmit">确认修改</el-button>
              <el-button @click="resetForm">重置</el-button>
            </el-form-item>
          </el-form>
        </el-tab-pane>

        <!-- ------------------------------ 账号绑定（SSO） ------------------------------ -->
        <el-tab-pane label="账号绑定" name="sso">
          <div v-if="ssoBoundName" class="sso-bound">
            <el-tag type="success">{{ ssoBoundName }}</el-tag>
            <span class="sso-bound-text">已绑定，扫码登录将直接进入本账号</span>
          </div>
          <template v-else>
            <p class="sso-hint">尚未绑定企业身份。选择一种方式扫码完成绑定：</p>
            <div v-if="ssoProviders.length > 0" class="sso-actions">
              <el-button v-for="p in ssoProviders" :key="p.id" plain @click="handleBind(p)">
                绑定{{ p.displayName }}
              </el-button>
            </div>
            <el-alert v-else type="info" :closable="false"
              title="平台尚未启用任何 SSO 登录方式，请联系管理员在系统设置中开启" />
          </template>
        </el-tab-pane>
      </el-tabs>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, type FormInstance, type FormRules } from 'element-plus'
import { changeOwnPasswordApi, mySsoApi, ssoProvidersApi } from '@/api/auth'
import { useAuthStore } from '@/stores/auth'
import { SSO_PROVIDER_NAMES } from '@/types/auth'
import type { SsoProviderInfo } from '@/types/auth'

const MIN_PASSWORD_LENGTH = 8
const MIN_CHAR_CLASSES = 2

const auth = useAuthStore()
const router = useRouter()
const activeTab = ref('password')
const formRef = ref<FormInstance>()
const saving = ref(false)
const form = reactive({ oldPassword: '', newPassword: '', confirmPassword: '' })

const validateNewPassword = (_r: unknown, value: string, cb: (e?: Error) => void) => {
  if (!value || value.length < MIN_PASSWORD_LENGTH) {
    return cb(new Error(`密码至少 ${MIN_PASSWORD_LENGTH} 位`))
  }
  const classes = [/[A-Za-z]/, /\d/, /[^A-Za-z0-9]/].filter((re) => re.test(value)).length
  if (classes < MIN_CHAR_CLASSES) {
    return cb(new Error('密码需包含字母、数字、符号中的至少两类'))
  }
  if (value === form.oldPassword) {
    return cb(new Error('新密码不能与当前密码相同'))
  }
  cb()
}

const validateConfirm = (_r: unknown, value: string, cb: (e?: Error) => void) => {
  if (value !== form.newPassword) return cb(new Error('两次输入的密码不一致'))
  cb()
}

const rules: FormRules = {
  oldPassword: [{ required: true, message: '请输入当前密码', trigger: 'blur' }],
  newPassword: [{ validator: validateNewPassword, trigger: 'blur' }],
  confirmPassword: [{ validator: validateConfirm, trigger: 'blur' }],
}

function resetForm() {
  formRef.value?.clearValidate()
  Object.assign(form, { oldPassword: '', newPassword: '', confirmPassword: '' })
}

async function handleSubmit() {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return
  saving.value = true
  try {
    await changeOwnPasswordApi({ oldPassword: form.oldPassword, newPassword: form.newPassword })
    ElMessage.success('密码已修改，请使用新密码重新登录')
    auth.logout()
    router.replace({ name: 'login' })
  } finally {
    saving.value = false
  }
}

// ------------------------------ 账号绑定（SSO）
// 未启用任何 Provider 时给出提示（避免纯密码环境用户找不到绑定的入口在哪）
const ssoProviders = ref<SsoProviderInfo[]>([])
const ssoBoundProvider = ref<string | null>(null)
const ssoBoundName = computed(() =>
  ssoBoundProvider.value ? (SSO_PROVIDER_NAMES[ssoBoundProvider.value] ?? ssoBoundProvider.value) : '')

onMounted(async () => {
  try {
    const [providers, me] = await Promise.all([ssoProvidersApi(), mySsoApi()])
    ssoProviders.value = providers
    ssoBoundProvider.value = me.ssoProvider
  } catch {
    ssoProviders.value = []
  }
})

function handleBind(p: SsoProviderInfo) {
  // 跳后端 authorize（mode=bind）→ 企业扫码 → 回调 /login/sso/bind 完成绑定
  window.location.href = `${p.authorizeUrl}?mode=bind`
}
</script>

<style scoped>
.profile-page {
  display: flex;
  justify-content: center;
  padding-top: 24px;
}

.panel {
  width: 560px;
}

.panel-header {
  display: flex;
  align-items: baseline;
  gap: 12px;
}

.panel-title {
  font-size: 16px;
  font-weight: 600;
}

.panel-sub {
  color: #8a93a0;
  font-size: 13px;
}

.form {
  padding-top: 8px;
}

.tip {
  margin-bottom: 18px;
}

.actions {
  margin-bottom: 0;
}

.sso-bound {
  display: flex;
  align-items: center;
  gap: 10px;
  padding-top: 8px;
}

.sso-bound-text {
  color: var(--el-text-color-regular);
  font-size: 13px;
}

.sso-hint {
  margin: 0 0 12px;
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.sso-actions {
  display: flex;
  gap: 12px;
}
</style>
