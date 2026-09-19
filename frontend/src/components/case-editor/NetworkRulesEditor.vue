<template>
  <div class="network-rules">
    <!-- 空态直接说明"这是干什么的"：光说"未配置"用户不知道该不该配 -->
    <div v-if="modelValue.length === 0" class="nr-empty">
      未配置。用来把不稳定的第三方依赖（支付、短信、地图、埋点）按用例桩掉——
      它们抖一下用例就红一次，却和被测系统的质量无关。
    </div>

    <div v-for="(rule, index) in modelValue" :key="index" class="nr-rule">
      <div class="nr-row">
        <el-input v-model="rule.pattern" class="nr-pattern" placeholder="URL 匹配，例：**/api/pay**"
          @input="emitAll" />
        <el-select v-model="rule.action" class="nr-action" @change="onActionChange(rule)">
          <el-option v-for="(label, value) in NETWORK_RULE_ACTION_LABELS" :key="value" :label="label"
            :value="value" />
        </el-select>
        <el-button link type="danger" @click="removeRule(index)">删除</el-button>
      </div>

      <template v-if="rule.action === 'fulfill'">
        <div class="nr-row">
          <el-input-number v-model="rule.status" class="nr-status" :min="100" :max="599" :controls="false"
            placeholder="状态码" @change="emitAll" />
          <el-input v-model="rule.contentType" class="nr-ctype" placeholder="Content-Type（默认 application/json）"
            @input="emitAll" />
        </div>
        <el-input v-model="rule.body" class="nr-body" type="textarea" :rows="3"
          placeholder='响应体，例：{"code":0,"data":[]}' @input="emitAll" />
      </template>

      <div v-else-if="rule.action === 'delay'" class="nr-row">
        <el-input-number v-model="rule.delayMs" :min="0" :max="120000" :step="500" @change="emitAll" />
        <span class="nr-hint">毫秒（上限 120000），用来验证 loading 态与超时处理</span>
      </div>

      <div v-else class="nr-hint">
        请求会直接失败。用来验证前端的降级与错误提示，不需要填响应体。
      </div>
    </div>

    <el-button :icon="Plus" size="small" @click="addRule">添加规则</el-button>
  </div>
</template>

<script setup lang="ts">
import { Plus } from '@element-plus/icons-vue'
import { NETWORK_RULE_ACTION_LABELS, type NetworkRule } from '@/types/testcase'

const props = defineProps<{ modelValue: NetworkRule[] }>()
const emit = defineEmits<{ 'update:modelValue': [NetworkRule[]] }>()

/**
 * 每次编辑都显式 emit 一份**新数组**（元素按引用共享）。
 *
 * 直接改 `props.modelValue` 里的对象虽然也能生效（父组件持有同一批对象），
 * 但"数组本身变了"这件事父组件感知不到，父层若有基于数组的校验/脏检查就会漏掉。
 * 浅拷贝成本可忽略，换来的是 v-model 语义完整。
 */
function emitAll() {
  emit('update:modelValue', [...props.modelValue])
}

function addRule() {
  emit('update:modelValue', [
    ...props.modelValue,
    { pattern: '', action: 'fulfill', status: 200, contentType: 'application/json', body: '{}' },
  ])
}

function removeRule(index: number) {
  const next = [...props.modelValue]
  next.splice(index, 1)
  emit('update:modelValue', next)
}

/**
 * 切换动作时清掉不适用的字段。
 *
 * 不清的话 abort 规则里会残留上一动作填的 status/body，而服务端会因此报
 * "abort 不应再填状态码或响应体"——用户看着自己没填过的字段被报错，根本无从下手。
 */
function onActionChange(rule: NetworkRule) {
  if (rule.action !== 'fulfill') {
    rule.status = null
    rule.contentType = null
    rule.body = null
  } else if (rule.status == null) {
    rule.status = 200
  }
  if (rule.action !== 'delay') rule.delayMs = null
  emitAll()
}
</script>

<style scoped>
.network-rules {
  width: 100%;
}

.nr-empty {
  margin-bottom: 8px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  line-height: 1.7;
}

.nr-rule {
  padding: 10px 12px;
  margin-bottom: 8px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 4px;
  background: var(--el-fill-color-lighter);
}

.nr-row {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-bottom: 8px;
}

.nr-pattern {
  flex: 1 1 auto;
}

.nr-action {
  width: 150px;
  flex: 0 0 auto;
}

.nr-status {
  width: 110px;
  flex: 0 0 auto;
}

.nr-ctype {
  flex: 1 1 auto;
}

.nr-body {
  margin-bottom: 0;
}

.nr-hint {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
</style>
