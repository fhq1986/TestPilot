<template>
  <div class="settings-page">
    <el-alert type="info" :closable="false" class="settings-notice">
      <template #title>
        配置保存后即时生效（无需重启服务）；密钥仅显示掩码，不再返回明文；密钥输入留空表示保留原值。
      </template>
    </el-alert>

    <el-card class="settings-card">
      <template #header>
        <div class="card-header" @click="toggleCard('ai')">
          <span>AI 模型连接</span>
          <el-icon class="collapse-arrow" :class="{ 'is-collapsed': isCollapsed('ai') }"><ArrowDown /></el-icon>
        </div>
      </template>

      <el-collapse-transition>
      <div v-show="!isCollapsed('ai')">
      <el-form :model="aiForm" label-position="top" @submit.prevent>
        <el-form-item label="AI 提供商">
          <el-select v-model="providerId" class="provider-select" filterable @change="handleProviderChange">
            <el-option v-for="p in providers" :key="p.id" :label="p.name" :value="p.id" />
          </el-select>
          <span v-if="currentProvider?.keyUrl" class="provider-link">
            获取 API Key：
            <el-link type="primary" :href="currentProvider.keyUrl" target="_blank" rel="noopener">
              {{ currentProvider.keyUrl }}
            </el-link>
          </span>
          <div v-if="currentProvider?.note" class="provider-note">{{ currentProvider.note }}</div>
        </el-form-item>

        <el-form-item label="接口地址（Base URL）">
          <el-input v-model="aiForm.baseUrl" placeholder="https://api.deepseek.com" maxlength="500" />
        </el-form-item>

        <el-form-item label="模型名称">
          <el-select v-if="modelOptions.length" v-model="aiForm.model" class="model-select" filterable allow-create
            default-first-option placeholder="选择或输入模型名">
            <el-option v-for="m in modelOptions" :key="m.id" :label="m.label" :value="m.id">
              <span>{{ m.label }}</span>
              <span v-if="m.note" class="model-note">{{ m.note }}</span>
            </el-option>
          </el-select>
          <el-input v-else v-model="aiForm.model" maxlength="200" placeholder="请输入模型名，如 my-model-v1" />
        </el-form-item>

        <el-form-item label="最大 Token 数">
          <el-input-number v-model="aiForm.maxTokens" :min="256" :max="32768" />
        </el-form-item>

        <el-form-item label="API Key">
          <el-input v-model="aiForm.apiKey" type="password" show-password autocomplete="new-password"
            :placeholder="aiKeyPlaceholder" />
        </el-form-item>

        <el-form-item>
          <el-button :loading="testing" @click="handleTest">测试连接</el-button>
          <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
        </el-form-item>
      </el-form>
      </div>
      </el-collapse-transition>
    </el-card>

    <el-card class="settings-card">
      <template #header>
        <div class="card-header" @click="toggleCard('swagger')">
          <span>Swagger 导入</span>
          <el-icon class="collapse-arrow" :class="{ 'is-collapsed': isCollapsed('swagger') }"><ArrowDown /></el-icon>
        </div>
      </template>

      <el-collapse-transition>
      <div v-show="!isCollapsed('swagger')">
      <el-form :model="aiForm" label-position="top" @submit.prevent>
        <el-form-item label="允许内网地址导入 Swagger">
          <el-switch v-model="aiForm.allowPrivateNetworkImport" />
          <div class="switch-hint">
            本平台为内网测试工具，默认允许导入内网被测系统的 Swagger；关闭后 URL 导入仅允许公网地址。
          </div>
        </el-form-item>

        <el-form-item>
          <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
        </el-form-item>
      </el-form>
      </div>
      </el-collapse-transition>
    </el-card>

    <el-card class="settings-card">
      <template #header>
        <div class="card-header" @click="toggleCard('notify')">
          <span>通知渠道</span>
          <span class="card-header-hint">执行结束后推送结果摘要；Webhook 地址留空表示不启用该渠道</span>
          <el-icon class="collapse-arrow" :class="{ 'is-collapsed': isCollapsed('notify') }"><ArrowDown /></el-icon>
        </div>
      </template>

      <el-collapse-transition>
      <div v-show="!isCollapsed('notify')">

      <el-form :model="notifyForm" label-position="top" @submit.prevent>
        <el-form-item label="启用通知">
          <el-switch v-model="notifyForm.notifyEnabled" />
          <el-switch v-model="notifyForm.notifyOnFailureOnly" class="inline-switch"
            :disabled="!notifyForm.notifyEnabled"
            active-text="仅失败/错误时通知" />
          <el-switch v-model="notifyForm.notifyPlanResultEmail" class="inline-switch"
            :disabled="!notifyForm.notifyEnabled"
            active-text="计划结果邮件" />
          <div class="switch-hint">
            定时任务与 AI 回归产生的批量执行通常很多，建议开启「仅失败时通知」，避免消息轰炸。<br />
            「计划结果邮件」把测试计划的验收结果（含报告附件与在线链接）发给<b>项目的测试负责人</b>与<b>计划的负责人</b>；
            它不受「仅失败时通知」约束——达标结果同样需要留档。需先在下方配置 SMTP。
          </div>
        </el-form-item>

        <el-divider content-position="left">即时通讯机器人</el-divider>

        <el-form-item v-for="ch in CHANNELS" :key="ch.key" :label="ch.label">
          <div class="channel-row">
            <el-input v-model="notifyForm[ch.key]" :placeholder="ch.placeholder" :disabled="!notifyForm.notifyEnabled" />
            <el-tag v-if="channelConfigured(ch.key)" type="success" class="channel-tag">已配置</el-tag>
            <el-button v-if="channelConfigured(ch.key)" link type="danger"
              :disabled="!notifyForm.notifyEnabled" @click="clearChannel(ch.key)">清除</el-button>
          </div>
          <div class="switch-hint">{{ ch.hint }}</div>
        </el-form-item>

        <el-divider content-position="left">邮件（SMTP）</el-divider>

        <div class="smtp-grid">
          <el-form-item label="SMTP 服务器">
            <el-input v-model="notifyForm.smtpHost" placeholder="smtp.example.com" :disabled="!notifyForm.notifyEnabled" />
          </el-form-item>
          <el-form-item label="端口">
            <el-input-number v-model="notifyForm.smtpPort" :min="1" :max="65535"
              :disabled="!notifyForm.notifyEnabled" controls-position="right" />
          </el-form-item>
          <el-form-item label="SSL">
            <el-switch v-model="notifyForm.smtpUseSsl" :disabled="!notifyForm.notifyEnabled" />
          </el-form-item>
          <el-form-item label="账号">
            <el-input v-model="notifyForm.smtpUser" placeholder="发件邮箱账号" :disabled="!notifyForm.notifyEnabled" />
          </el-form-item>
          <el-form-item label="密码 / 授权码">
            <el-input v-model="notifyForm.smtpPassword" type="password" show-password autocomplete="new-password"
              :placeholder="smtpPasswordPlaceholder" :disabled="!notifyForm.notifyEnabled" />
          </el-form-item>
          <el-form-item label="收件人">
            <el-input v-model="notifyForm.mailTo" placeholder="多个地址用英文逗号分隔"
              :disabled="!notifyForm.notifyEnabled" />
          </el-form-item>
        </div>

        <el-form-item>
          <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
          <el-button :loading="testingNotify" :disabled="!notifyForm.notifyEnabled" @click="handleTestNotify()">
            发送测试消息
          </el-button>
        </el-form-item>

        <el-divider content-position="left">测试邮件</el-divider>
        <div class="switch-hint mail-intro">
          「测试消息」只验证通道通不通（一句话、纯文本、不带附件），看不出收件人实际会收到什么。<br />
          这里发送一封 <b>真实形态</b> 的验收邮件：HTML 正文 + 在线查看/下载链接 + xlsx 报告附件，
          数据取自所选计划的最近一轮，用来在配置阶段就把整条链路确认下来。发送前请先保存 SMTP 设置。
        </div>
        <el-form-item label="收件邮箱">
          <el-input v-model="testMailForm.to" placeholder="留空则用上面的「收件人」" :disabled="!notifyForm.notifyEnabled" />
        </el-form-item>
        <el-form-item label="数据来源计划">
          <el-select v-model="testMailForm.planId" filterable remote reserve-keyword
            :remote-method="onTestMailPlanSearch" :loading="testMailPlanLoading" clearable
            :disabled="!notifyForm.notifyEnabled" placeholder="留空则自动取最近跑过轮次的计划"
            class="mail-plan-select">
            <!-- 与其它远程搜索下拉同一套做法：选中项置顶保留并标「当前」，
                 否则一搜索它就从列表消失，el-select 会把 id 当标签显示出来 -->
            <el-option v-for="p in testMailPlanOptions" :key="p.id" :label="p.name" :value="p.id">
              <span>{{ p.name }}</span>
              <span class="plan-option-meta">
                <el-tag v-if="p.id === testMailForm.planId" size="small" effect="plain"
                  type="primary">当前</el-tag>
                {{ planStatusText(p.status) }} · {{ p.caseCount }} 条用例
              </span>
            </el-option>
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button :loading="sendingTestMail" :disabled="!notifyForm.notifyEnabled"
            @click="handleSendTestMail">
            发送测试邮件
          </el-button>
          <span v-if="testMailResult" class="field-hint mail-result"
            :class="testMailResult.ok ? 'mail-ok' : 'mail-fail'">
            {{ testMailResult.message }}
          </span>
        </el-form-item>
      </el-form>
      </div>
      </el-collapse-transition>
    </el-card>

    <el-card class="settings-card">
      <template #header>
        <div class="card-header" @click="toggleCard('ci')">
          <span>CI 集成</span>
          <span class="card-header-hint">流水线里调用以下地址即可触发回归并阻塞等待结果</span>
          <el-icon class="collapse-arrow" :class="{ 'is-collapsed': isCollapsed('ci') }"><ArrowDown /></el-icon>
        </div>
      </template>

      <el-collapse-transition>
      <div v-show="!isCollapsed('ci')">

      <el-descriptions :column="1" border class="ci-desc">
        <el-descriptions-item label="触发地址">
          <code class="ci-code">POST {{ ciEndpoint }}</code>
          <el-button link type="primary" @click="copy(ciEndpoint)">复制</el-button>
        </el-descriptions-item>
        <el-descriptions-item label="认证请求头">
          <code class="ci-code">X-Webhook-Token: &lt;Webhook Token&gt;</code>
          <span class="field-hint">（在下方「Webhook 配置」中维护）</span>
        </el-descriptions-item>
        <el-descriptions-item label="同步等待">
          <code class="ci-code">?wait=true&amp;timeoutSeconds=600</code>
          <span class="field-hint">响应中的 success 即本次回归是否全绿</span>
        </el-descriptions-item>
      </el-descriptions>

      <el-tabs v-model="ciTab" class="ci-tabs">
        <el-tab-pane label="curl" name="curl">
          <pre class="ci-snippet">{{ ciSnippets.curl }}</pre>
          <el-button link type="primary" @click="copy(ciSnippets.curl)">复制</el-button>
        </el-tab-pane>
        <el-tab-pane label="GitHub Actions" name="github">
          <pre class="ci-snippet">{{ ciSnippets.github }}</pre>
          <el-button link type="primary" @click="copy(ciSnippets.github)">复制</el-button>
        </el-tab-pane>
        <el-tab-pane label="GitLab CI" name="gitlab">
          <pre class="ci-snippet">{{ ciSnippets.gitlab }}</pre>
          <el-button link type="primary" @click="copy(ciSnippets.gitlab)">复制</el-button>
        </el-tab-pane>
        <el-tab-pane label="Jenkins" name="jenkins">
          <pre class="ci-snippet">{{ ciSnippets.jenkins }}</pre>
          <el-button link type="primary" @click="copy(ciSnippets.jenkins)">复制</el-button>
        </el-tab-pane>
      </el-tabs>
      </div>
      </el-collapse-transition>
    </el-card>

    <el-card class="settings-card">
      <template #header>
        <div class="card-header" @click="toggleCard('webhook')">
          <span>Webhook 配置</span>
          <el-icon class="collapse-arrow" :class="{ 'is-collapsed': isCollapsed('webhook') }"><ArrowDown /></el-icon>
        </div>
      </template>

      <el-collapse-transition>
      <div v-show="!isCollapsed('webhook')">
      <el-form :model="webhookForm" label-position="top" @submit.prevent>
        <el-form-item label="Webhook Token">
          <el-input v-model="webhookForm.token" type="password" show-password autocomplete="new-password"
            :placeholder="webhookTokenPlaceholder" />
        </el-form-item>

        <el-form-item>
          <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
        </el-form-item>
      </el-form>
      </div>
      </el-collapse-transition>
    </el-card>

    <el-card class="settings-card">
      <template #header>
        <div class="card-header" @click="toggleCard('sso')">
          <span>单点登录（SSO）</span>
          <span class="card-header-hint">扫码登录配置保存后立即生效，无需重启；密钥回显仅掩码，留空表示保留原值</span>
          <el-icon class="collapse-arrow" :class="{ 'is-collapsed': isCollapsed('sso') }"><ArrowDown /></el-icon>
        </div>
      </template>

      <el-collapse-transition>
      <div v-show="!isCollapsed('sso')">
        <el-form :model="ssoForm" label-position="top" @submit.prevent>
          <el-form-item label="前端站点根地址（FrontendBaseUrl）">
            <el-input v-model="ssoForm.frontendBaseUrl" placeholder="http://localhost:3000" />
            <div class="switch-hint">企业授权完成后回调到 {该地址}/login/sso，由前端回调页提交授权码换取平台登录态</div>
          </el-form-item>
          <el-form-item label="自动开通账号">
            <el-switch v-model="ssoForm.autoProvision" />
            <div class="switch-hint">
              开启后未绑定的企业用户新扫码会自动创建 Viewer 角色账号；关闭时将拒绝并提示联系管理员绑定。
            </div>
          </el-form-item>

          <el-divider content-position="left">企业微信（自建应用扫码）</el-divider>
          <el-form-item label="启用企业微信扫码登录">
            <el-switch v-model="ssoForm.wecomEnabled" />
          </el-form-item>
          <div class="smtp-grid">
            <el-form-item label="CorpId">
              <el-input v-model="ssoForm.wecomCorpId" :disabled="!ssoForm.wecomEnabled" placeholder="企业 MyCorpId" />
            </el-form-item>
            <el-form-item label="AgentId">
              <el-input v-model="ssoForm.wecomAgentId" :disabled="!ssoForm.wecomEnabled" placeholder="自建应用 AgentId" />
            </el-form-item>
            <el-form-item label="Secret">
              <el-input v-model="ssoForm.wecomSecret" type="password" show-password autocomplete="new-password"
                :placeholder="ssoWecomSecretPlaceholder" :disabled="!ssoForm.wecomEnabled" />
            </el-form-item>
          </div>

          <el-divider content-position="left">钉钉（扫码登录）</el-divider>
          <el-form-item label="启用钉钉扫码登录">
            <el-switch v-model="ssoForm.dingtalkEnabled" />
          </el-form-item>
          <div class="smtp-grid">
            <el-form-item label="ClientId（AppKey）">
              <el-input v-model="ssoForm.dingtalkClientId" :disabled="!ssoForm.dingtalkEnabled" />
            </el-form-item>
            <el-form-item label="ClientSecret（AppSecret）">
              <el-input v-model="ssoForm.dingtalkClientSecret" type="password" show-password autocomplete="new-password"
                :placeholder="ssoDingtalkSecretPlaceholder" :disabled="!ssoForm.dingtalkEnabled" />
            </el-form-item>
          </div>

          <el-divider content-position="left">标准 OIDC（Azure AD / Okta / Keycloak / Auth0 / Google）</el-divider>
          <el-form-item label="启用标准 OIDC 登录">
            <el-switch v-model="ssoForm.oidcEnabled" />
          </el-form-item>
          <div class="smtp-grid">
            <el-form-item label="Authority（Issuer）">
              <el-input v-model="ssoForm.oidcAuthority" :disabled="!ssoForm.oidcEnabled"
                placeholder="https://login.microsoftonline.com/{tenant}/v2.0" />
            </el-form-item>
            <el-form-item label="ClientId">
              <el-input v-model="ssoForm.oidcClientId" :disabled="!ssoForm.oidcEnabled"
                placeholder="应用注册的 Client ID" />
            </el-form-item>
            <el-form-item label="ClientSecret">
              <el-input v-model="ssoForm.oidcClientSecret" type="password" show-password autocomplete="new-password"
                :placeholder="ssoOidcSecretPlaceholder" :disabled="!ssoForm.oidcEnabled" />
            </el-form-item>
          </div>
          <div class="smtp-grid">
            <el-form-item label="登录按钮名称">
              <el-input v-model="ssoForm.oidcDisplayName" :disabled="!ssoForm.oidcEnabled" placeholder="单点登录" />
            </el-form-item>
            <el-form-item label="Scope">
              <el-input v-model="ssoForm.oidcScopes" :disabled="!ssoForm.oidcEnabled"
                placeholder="openid profile email" />
            </el-form-item>
          </div>
          <div class="switch-hint">
            端点全部从 {Authority}/.well-known/openid-configuration 自动发现；IdP 侧回调地址（Redirect URI）填
            {FrontendBaseUrl}/login/sso?provider=oidc
          </div>

          <el-form-item>
            <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
          </el-form-item>
        </el-form>
      </div>
      </el-collapse-transition>
    </el-card>

    <el-card class="settings-card">
      <template #header>
        <div class="card-header" @click="toggleCard('agent')">
          <span>Agent 自愈闭环</span>
          <span class="card-header-hint">执行失败后由 AI 归因并自动修复后重跑；仅修改执行副本、不改动用例本身</span>
          <el-icon class="collapse-arrow" :class="{ 'is-collapsed': isCollapsed('agent') }"><ArrowDown /></el-icon>
        </div>
      </template>

      <el-collapse-transition>
      <div v-show="!isCollapsed('agent')">
        <el-form :model="agentForm" label-position="top" @submit.prevent>
          <el-form-item label="启用 Agent 失败自愈闭环（系统级总开关）">
            <el-switch v-model="agentForm.enabled" />
            <div class="switch-hint">
              这是系统级总闸：开启后，执行失败会尝试由 AI 归因并自动修复后重跑（还须同时开启对应项目的项目级开关）。
              默认关闭。当 LLM 不可用时自动降级，不影响正常执行与其他功能。
            </div>
          </el-form-item>

          <el-form-item>
            <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
          </el-form-item>
        </el-form>
      </div>
      </el-collapse-transition>
    </el-card>

    <div v-if="view" class="settings-updated">最近更新：{{ formatDateTime(view.updatedAt) }}</div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { ArrowDown } from '@element-plus/icons-vue'
import {
  getAIProviders, getSettings, sendTestMail, testAiConnection, testNotification, updateSettings,
} from '@/api/settings'
import { listTestPlansApi } from '@/api/testPlan'
import type { AIProviderPreset, SettingsView } from '@/types/settings'
import type { TestPlanSummary } from '@/types/testPlan'
import { formatDateTime } from '@/utils/formatter'

const view = ref<SettingsView | null>(null)
const providers = ref<AIProviderPreset[]>([])
const providerId = ref('custom')

// ------------------------------------------------------------ 卡片展开/收起
// 默认全部展开；点击卡片头部切换，箭头指示当前状态
const collapsedCards = reactive<Record<string, boolean>>({})
const isCollapsed = (key: string) => !!collapsedCards[key]
const toggleCard = (key: string) => {
  collapsedCards[key] = !collapsedCards[key]
}

/** 当前提供商的模型候选；「自定义」为空数组 → 退化为手动输入 */
const currentProvider = computed(() => providers.value.find((p) => p.id === providerId.value))
const modelOptions = computed(() => currentProvider.value?.models ?? [])

/** 根据已保存的 Base URL 反推提供商（找不到则视为自定义） */
const inferProviderId = (baseUrl: string) => {
  if (!baseUrl) return 'custom'
  const matched = providers.value.find(
    (p) => p.baseUrl && baseUrl.replace(/\/+$/, '') === p.baseUrl.replace(/\/+$/, ''),
  )
  return matched?.id ?? 'custom'
}

/** 切换提供商：自动带出 Base URL，并把模型重置为该提供商的默认模型 */
const handleProviderChange = (id: string) => {
  const preset = providers.value.find((p) => p.id === id)
  if (!preset) return
  if (preset.baseUrl) aiForm.baseUrl = preset.baseUrl
  aiForm.model = preset.models[0]?.id ?? ''
}
const testing = ref(false)
const saving = ref(false)

const aiForm = reactive({
  baseUrl: '',
  model: '',
  maxTokens: 8192,
  apiKey: '',
  allowPrivateNetworkImport: true,
})

const webhookForm = reactive({
  token: '',
})

// ------------------------------------------------------------ M8 Agent 自愈闭环（系统级总开关）
const agentForm = reactive({
  enabled: false,
})

// ------------------------------------------------------------ SSO 扫码登录
const ssoForm = reactive({
  autoProvision: false,
  frontendBaseUrl: '',
  wecomEnabled: false,
  wecomCorpId: '',
  wecomAgentId: '',
  wecomSecret: '',
  dingtalkEnabled: false,
  dingtalkClientId: '',
  dingtalkClientSecret: '',
  oidcEnabled: false,
  oidcAuthority: '',
  oidcClientId: '',
  oidcClientSecret: '',
  oidcDisplayName: '',
  oidcScopes: '',
})

const ssoWecomSecretPlaceholder = computed(() =>
  view.value?.hasSsoWecomSecret
    ? `已配置（${view.value.ssoWecomSecretMasked}），留空保持不变`
    : '未配置',
)
const ssoDingtalkSecretPlaceholder = computed(() =>
  view.value?.hasSsoDingtalkClientSecret
    ? `已配置（${view.value.ssoDingtalkClientSecretMasked}），留空保持不变`
    : '未配置',
)
const ssoOidcSecretPlaceholder = computed(() =>
  view.value?.hasSsoOidcClientSecret
    ? `已配置（${view.value.ssoOidcClientSecretMasked}），留空保持不变`
    : '未配置',
)

// ---------------------------------------------------------------- 通知渠道

/** 三个机器人渠道的元数据（key 与 notifyForm 字段一一对应） */
const CHANNELS = [
  {
    key: 'notifyWecomWebhook' as const,
    viewKey: 'wecom' as const,
    clearKey: 'wecom',
    label: '企业微信机器人',
    placeholder: 'https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=xxx',
    hint: '企业微信群 → 添加群机器人 → 复制 Webhook 地址',
  },
  {
    key: 'notifyDingtalkWebhook' as const,
    viewKey: 'dingtalk' as const,
    clearKey: 'dingtalk',
    label: '钉钉机器人',
    placeholder: 'https://oapi.dingtalk.com/robot/send?access_token=xxx',
    hint: '钉钉群 → 智能群助手 → 添加机器人（安全设置建议使用自定义关键词，如「测试」）',
  },
  {
    key: 'notifyFeishuWebhook' as const,
    viewKey: 'feishu' as const,
    clearKey: 'feishu',
    label: '飞书机器人',
    placeholder: 'https://open.feishu.cn/open-apis/bot/v2/hook/xxx',
    hint: '飞书群 → 设置 → 群机器人 → 添加自定义机器人',
  },
]

const notifyForm = reactive({
  notifyEnabled: false,
  notifyOnFailureOnly: true,
  notifyPlanResultEmail: true,
  notifyWecomWebhook: '',
  notifyDingtalkWebhook: '',
  notifyFeishuWebhook: '',
  smtpHost: '',
  smtpPort: 465,
  smtpUseSsl: true,
  smtpUser: '',
  smtpPassword: '',
  mailTo: '',
})

/** 已被清空的渠道，保存时通过 clearWebhook 显式通知后端 */
const clearedChannels = ref<string[]>([])

const channelConfigured = (key: (typeof CHANNELS)[number]['key']) => {
  const item = CHANNELS.find((c) => c.key === key)!
  if (notifyForm[key]) return true
  if (clearedChannels.value.includes(item.clearKey)) return false
  return view.value?.[item.viewKey]?.configured ?? false
}

const clearChannel = (key: (typeof CHANNELS)[number]['key']) => {
  const item = CHANNELS.find((c) => c.key === key)!
  notifyForm[key] = ''
  if (!clearedChannels.value.includes(item.clearKey)) clearedChannels.value.push(item.clearKey)
}

const smtpPasswordPlaceholder = computed(() =>
  view.value?.hasSmtpPassword
    ? `已配置（${view.value.smtpPasswordMasked}），留空保持不变`
    : '未配置（如 QQ 邮箱为授权码）',
)

const testingNotify = ref(false)

const handleTestNotify = async (channel?: string) => {
  testingNotify.value = true
  try {
    const res = await testNotification(channel)
    ElMessage.success(res.message ?? `已向 ${res.channels.length} 个渠道发送测试消息`)
  } catch {
    // 失败详情由响应拦截器按后端返回的 message 提示（含具体渠道与原因）
  } finally {
    testingNotify.value = false
  }
}

// ---------------------------------------------------------------- 测试邮件

const testMailForm = reactive({ to: '', planId: '' })
const sendingTestMail = ref(false)
/** 结果直接显示在按钮旁边而不是弹 toast：发送成功但缺附件这类"半成功"信息需要留得住 */
const testMailResult = ref<{ ok: boolean; message: string } | null>(null)

const testMailPlanOptions = ref<TestPlanSummary[]>([])
const testMailPlanLoading = ref(false)

const planStatusText = (status: number) =>
  ({ 0: '草稿', 1: '进行中', 2: '已完成', 3: '已归档' }[status] ?? '未知')

async function loadTestMailPlans(searchTerm = '') {
  testMailPlanLoading.value = true
  try {
    const res = await listTestPlansApi({
      search: searchTerm.trim() || undefined,
      page: 1,
      pageSize: 50,
    })
    const rest = res.items.filter((p) => p.id !== testMailForm.planId)
    const pinned = res.items.find((p) => p.id === testMailForm.planId)
      ?? testMailPlanOptions.value.find((p) => p.id === testMailForm.planId)
    testMailPlanOptions.value = pinned ? [pinned, ...rest] : rest
  } catch {
    testMailPlanOptions.value = []
  } finally {
    testMailPlanLoading.value = false
  }
}

let testMailSearchTimer: ReturnType<typeof setTimeout> | undefined
function onTestMailPlanSearch(query: string) {
  clearTimeout(testMailSearchTimer)
  testMailSearchTimer = setTimeout(() => void loadTestMailPlans(query), 300)
}

const handleSendTestMail = async () => {
  sendingTestMail.value = true
  testMailResult.value = null
  try {
    // 留空时先用**表单里当前填的**收件人（可能还没保存），再退回后端的已保存配置——
    // 否则用户刚填完收件人就点发送，邮件会发到上一版配置的地址去
    const res = await sendTestMail({
      to: testMailForm.to.trim() || notifyForm.mailTo.trim() || null,
      planId: testMailForm.planId || null,
    })
    testMailResult.value = { ok: true, message: res.message }
    ElMessage.success('测试邮件已发送')
  } catch (err) {
    // 后端用 502 表达"发送失败"，message 里有具体原因；直接展示出来
    const msg =
      (err as { response?: { data?: { message?: string } } })?.response?.data?.message
      ?? '发送失败，请检查 SMTP 配置'
    testMailResult.value = { ok: false, message: msg }
    ElMessage.error(msg)
  } finally {
    sendingTestMail.value = false
  }
}

// ---------------------------------------------------------------- CI 集成

const ciEndpoint = computed(() =>
  `${window.location.origin.replace(/:\d+$/, ':5210')}/api/webhooks/executions`,
)

const ciSnippets = computed(() => {
  const endpoint = ciEndpoint.value
  return {
    curl: `curl -X POST "${endpoint}?wait=true&timeoutSeconds=600" \\
  -H "Content-Type: application/json" \\
  -H "X-Webhook-Token: <Webhook Token>" \\
  -d '{
    "projectId": "<项目 ID>",
    "module": "登录",
    "priority": "P0",
    "excludeFlaky": true,
    "commitSha": "'"$GIT_COMMIT"'",
    "branch": "'"$GIT_BRANCH"'",
    "buildNumber": "'"$BUILD_NUMBER"'",
    "source": "jenkins"
  }'
# 响应 success=true 表示本次回归全绿`,

    github: `- name: Run regression
  run: |
    RESULT=$(curl -sS -X POST "${endpoint}?wait=true&timeoutSeconds=900" \\
      -H "Content-Type: application/json" \\
      -H "X-Webhook-Token: \${{ secrets.AITEST_WEBHOOK_TOKEN }}" \\
      -d '{
        "projectId": "\${{ vars.AITEST_PROJECT_ID }}",
        "priority": "P0",
        "excludeFlaky": true,
        "commitSha": "\${{ github.sha }}",
        "branch": "\${{ github.ref_name }}",
        "buildNumber": "\${{ github.run_number }}",
        "source": "github-actions"
      }')
    echo "$RESULT"
    echo "$RESULT" | jq -e '.success == true' > /dev/null`,

    gitlab: `regression:
  stage: test
  script:
    - |
      RESULT=$(curl -sS -X POST "${endpoint}?wait=true&timeoutSeconds=900" \\
        -H "Content-Type: application/json" \\
        -H "X-Webhook-Token: $AITEST_WEBHOOK_TOKEN" \\
        -d "{
          \\"projectId\\": \\"$AITEST_PROJECT_ID\\",
          \\"priority\\": \\"P0\\",
          \\"excludeFlaky\\": true,
          \\"commitSha\\": \\"$CI_COMMIT_SHA\\",
          \\"branch\\": \\"$CI_COMMIT_REF_NAME\\",
          \\"buildNumber\\": \\"$CI_PIPELINE_ID\\",
          \\"source\\": \\"gitlab-ci\\"
        }")
      echo "$RESULT"
      echo "$RESULT" | jq -e '.success == true' > /dev/null
  # GitLab CI 也会自动带上 X-Gitlab-Ci-* 请求头，服务端会自动识别`,

    jenkins: `stage('回归测试') {
  steps {
    script {
      def payload = """{
        "projectId": "${'$'}{AITEST_PROJECT_ID}",
        "priority": "P0",
        "excludeFlaky": true,
        "commitSha": "${'$'}{GIT_COMMIT}",
        "branch": "${'$'}{GIT_BRANCH}",
        "buildNumber": "${'$'}{BUILD_NUMBER}",
        "source": "jenkins"
      }"""
      def response = sh(
        script: """curl -sS -X POST "${endpoint}?wait=true&timeoutSeconds=900" \\
          -H 'Content-Type: application/json' \\
          -H 'X-Webhook-Token: ${'$'}{AITEST_WEBHOOK_TOKEN}' \\
          -d '${'$'}{payload}'""",
        returnStdout: true,
      ).trim()
      echo response
      def json = readJSON text: response
      if (!json.success) {
        error "回归未通过：失败 ${'$'}{json.failed}，错误 ${'$'}{json.error}"
      }
    }
  }
}`,
  }
})

const ciTab = ref('curl')

const copy = async (text: string) => {
  try {
    await navigator.clipboard.writeText(text)
    ElMessage.success('已复制')
  } catch {
    ElMessage.warning('复制失败，请手动选择文本复制')
  }
}

const aiKeyPlaceholder = computed(() =>
  view.value?.hasAiApiKey
    ? `已配置（${view.value.aiApiKeyMasked}），留空保持不变`
    : '未配置（留空时回退 Worker 默认）',
)

const webhookTokenPlaceholder = computed(() =>
  view.value?.hasWebhookToken
    ? `已配置（${view.value.webhookTokenMasked}），留空保持不变`
    : '未配置（留空时回退默认配置）',
)

const applyView = (v: SettingsView) => {
  view.value = v
  aiForm.baseUrl = v.aiBaseUrl
  aiForm.model = v.aiModel
  aiForm.maxTokens = v.aiMaxTokens
  aiForm.apiKey = ''
  aiForm.allowPrivateNetworkImport = v.allowPrivateNetworkImport
  webhookForm.token = ''
  // 通知：Webhook/密码不回显明文，留空即保持原值
  notifyForm.notifyEnabled = v.notifyEnabled
  notifyForm.notifyOnFailureOnly = v.notifyOnFailureOnly
  notifyForm.notifyPlanResultEmail = v.notifyPlanResultEmail
  notifyForm.notifyWecomWebhook = ''
  notifyForm.notifyDingtalkWebhook = ''
  notifyForm.notifyFeishuWebhook = ''
  notifyForm.smtpHost = v.smtpHost
  notifyForm.smtpPort = v.smtpPort
  notifyForm.smtpUseSsl = v.smtpUseSsl
  notifyForm.smtpUser = v.smtpUser
  notifyForm.smtpPassword = ''
  notifyForm.mailTo = v.mailTo
  clearedChannels.value = []
  // SSO：密钥不回显明文，留空即保持原值。
  // ?? '' 兜底：后端未升级（响应缺字段）时不至于把 undefined 写进表单导致保存时报错
  ssoForm.autoProvision = v.ssoAutoProvision ?? false
  ssoForm.frontendBaseUrl = v.ssoFrontendBaseUrl ?? ''
  ssoForm.wecomEnabled = v.ssoWecomEnabled ?? false
  ssoForm.wecomCorpId = v.ssoWecomCorpId ?? ''
  ssoForm.wecomAgentId = v.ssoWecomAgentId ?? ''
  ssoForm.wecomSecret = ''
  ssoForm.dingtalkEnabled = v.ssoDingtalkEnabled ?? false
  ssoForm.dingtalkClientId = v.ssoDingtalkClientId ?? ''
  ssoForm.dingtalkClientSecret = ''
  ssoForm.oidcEnabled = v.ssoOidcEnabled ?? false
  ssoForm.oidcAuthority = v.ssoOidcAuthority ?? ''
  ssoForm.oidcClientId = v.ssoOidcClientId ?? ''
  ssoForm.oidcClientSecret = ''
  ssoForm.oidcDisplayName = v.ssoOidcDisplayName ?? ''
  ssoForm.oidcScopes = v.ssoOidcScopes ?? ''
  // M8 Agent 自愈闭环系统级总开关（后端未升级时缺字段，用 ?? false 兜底）
  agentForm.enabled = v.agentLoopEnabled ?? false
}

const load = async () => {
  applyView(await getSettings())
  // 已保存的 Base URL 反推提供商，保证下拉与当前配置一致
  providerId.value = inferProviderId(aiForm.baseUrl)
}

const loadProviders = async () => {
  try {
    providers.value = await getAIProviders()
  } catch {
    providers.value = []
  }
}

const handleSave = async () => {
  saving.value = true
  try {
    await updateSettings({
      aiBaseUrl: aiForm.baseUrl.trim() || null,
      aiModel: aiForm.model.trim() || null,
      aiMaxTokens: aiForm.maxTokens,
      aiApiKey: aiForm.apiKey.trim() || null,
      webhookToken: webhookForm.token.trim() || null,
      allowPrivateNetworkImport: aiForm.allowPrivateNetworkImport,
      notifyEnabled: notifyForm.notifyEnabled,
      notifyOnFailureOnly: notifyForm.notifyOnFailureOnly,
      notifyPlanResultEmail: notifyForm.notifyPlanResultEmail,
      notifyWecomWebhook: notifyForm.notifyWecomWebhook.trim() || null,
      notifyDingtalkWebhook: notifyForm.notifyDingtalkWebhook.trim() || null,
      notifyFeishuWebhook: notifyForm.notifyFeishuWebhook.trim() || null,
      smtpHost: notifyForm.smtpHost.trim() || null,
      smtpPort: notifyForm.smtpPort,
      smtpUseSsl: notifyForm.smtpUseSsl,
      smtpUser: notifyForm.smtpUser.trim() || null,
      smtpPassword: notifyForm.smtpPassword.trim() || null,
      mailTo: notifyForm.mailTo.trim() || null,
      clearWebhook: clearedChannels.value.length ? clearedChannels.value.join(',') : null,
      ssoAutoProvision: ssoForm.autoProvision,
      ssoFrontendBaseUrl: ssoForm.frontendBaseUrl.trim(),
      ssoWecomEnabled: ssoForm.wecomEnabled,
      ssoWecomCorpId: ssoForm.wecomCorpId.trim(),
      ssoWecomAgentId: ssoForm.wecomAgentId.trim(),
      ssoWecomSecret: ssoForm.wecomSecret.trim() || null,
      ssoDingtalkEnabled: ssoForm.dingtalkEnabled,
      ssoDingtalkClientId: ssoForm.dingtalkClientId.trim(),
      ssoDingtalkClientSecret: ssoForm.dingtalkClientSecret.trim() || null,
      ssoOidcEnabled: ssoForm.oidcEnabled,
      ssoOidcAuthority: ssoForm.oidcAuthority.trim(),
      ssoOidcClientId: ssoForm.oidcClientId.trim(),
      ssoOidcClientSecret: ssoForm.oidcClientSecret.trim() || null,
      ssoOidcDisplayName: ssoForm.oidcDisplayName.trim(),
      ssoOidcScopes: ssoForm.oidcScopes.trim(),
      agentLoopEnabled: agentForm.enabled,
    })
    await load()
    ElMessage.success('配置已保存并生效')
  } finally {
    saving.value = false
  }
}

const handleTest = async () => {
  testing.value = true
  try {
    const res = await testAiConnection()
    const latency = res.latencyMs != null ? `耗时=${res.latencyMs}ms` : ''
    ElMessage.success(`连接成功 model=${res.model}${latency ? ` ${latency}` : ''}`)
  } catch {
  } finally {
    testing.value = false
  }
}

onMounted(async () => {
  await loadProviders()
  await load()
})
</script>

<style scoped>
.provider-select {
  width: 260px;
}

.model-select {
  width: 320px;
}

.provider-link {
  margin-left: 12px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.provider-note {
  width: 100%;
  margin-top: 4px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.model-note {
  margin-left: 10px;
  font-size: 12px;
  color: var(--el-text-color-placeholder);
}

.settings-page {
  width: 100%;
}

.settings-card {
  margin-top: 16px;
}

.card-header {
  display: flex;
  align-items: center;
  gap: 12px;
  cursor: pointer;
  user-select: none;
}

/* 点击头部展开/收起：hint 占据中间，箭头固定最右并随状态旋转 */
.card-header-hint {
  flex: 1;
  font-size: 12px;
  font-weight: 400;
  color: var(--el-text-color-secondary);
}

.collapse-arrow {
  margin-left: auto;
  flex: none;
  color: var(--el-text-color-secondary);
  transition: transform 0.2s;
}

.collapse-arrow.is-collapsed {
  transform: rotate(-90deg);
}

.inline-switch {
  margin-left: 16px;
}

.channel-row {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
}

.channel-tag {
  flex: none;
}

.smtp-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 0 20px;
}

.ci-desc {
  margin-bottom: 12px;
}

.ci-code {
  background: var(--el-fill-color-light);
  padding: 2px 6px;
  border-radius: 3px;
  font-size: 12px;
}

.field-hint {
  margin-left: 8px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.ci-snippet {
  background: var(--el-fill-color-light);
  padding: 12px;
  border-radius: 4px;
  font-size: 12px;
  line-height: 1.7;
  overflow: auto;
  max-height: 340px;
  margin: 0 0 6px;
  white-space: pre;
}

.settings-updated {
  margin-top: 12px;
  color: #909399;
  font-size: 13px;
}

.switch-hint {
  margin-top: 6px;
  color: #909399;
  font-size: 12px;
  line-height: 1.5;
}

/* ------------------------------ 测试邮件 */
.mail-intro {
  margin-bottom: 12px;
}

.mail-plan-select {
  width: 380px;
}

/* 计划下拉里靠右弱化显示状态与用例数 */
.plan-option-meta {
  float: right;
  margin-left: 16px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

/* 发送结果留在页面上而不是只弹 toast：成功但缺附件这类"半成功"信息值得多看两眼 */
.mail-result {
  line-height: 1.5;
}

.mail-ok {
  color: var(--el-color-success);
}

.mail-fail {
  color: var(--el-color-danger);
}
</style>
