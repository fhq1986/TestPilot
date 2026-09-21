<template>
  <div class="testcase-list">
    <el-card class="list-card">
      <div class="toolbar">
        <el-select v-model="projectId" placeholder="选择项目" clearable filterable class="project-select" @change="load(1)">
          <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
        </el-select>
        <el-input v-model="search" placeholder="搜索用例名称/编号" clearable class="search-input" @keyup.enter="load(1)"
          @clear="load(1)" />
        <el-select v-model="moduleFilter" placeholder="全部模块" clearable filterable class="module-select"
          @change="load(1)">
          <el-option v-for="m in moduleOptions" :key="m.module" :label="`${m.module}（${m.count}）`" :value="m.module" />
        </el-select>
        <!-- 按「最近执行结果」筛。文案与列里的措辞刻意不完全相同：
             这里「执行中」是一个桶（排队中 + 在跑），放在筛选框里才符合"我在找什么" -->
        <el-select v-model="execStateFilter" placeholder="全部执行结果" clearable class="exec-select" @change="load(1)">
          <el-option v-for="o in CASE_EXEC_FILTER_OPTIONS" :key="o.value" :label="o.label" :value="o.value" />
        </el-select>
        <el-checkbox v-model="flakyOnly" class="flaky-filter" @change="load(1)">
          仅看不稳定用例
        </el-checkbox>
        <!-- 从需求覆盖页跳过来时带的筛选：给个显眼的标识 + 一键清除，
             否则用户看着被过滤的列表会以为用例丢了 -->
        <el-tag v-if="requirementFilterId" type="warning" effect="plain" closable
          @close="clearRequirementFilter">
          关联需求：{{ requirementFilterTitle || requirementFilterId }}
        </el-tag>
        <el-button type="success" :icon="VideoPlay" :disabled="selectedRows.length === 0" @click="openBatchDialog">
          批量执行（{{ selectedRows.length }}）
        </el-button>
        <el-button type="warning" plain :icon="RefreshLeft" :disabled="flakySelectedCount === 0"
          :loading="resettingFlake" @click="handleResetFlake">
          解除不稳定标记（{{ flakySelectedCount }}）
        </el-button>
        <el-button type="primary" plain :icon="EditPen" @click="openBatchEdit">
          批量编辑{{ selectedRows.length > 0 ? `（${selectedRows.length}）` : '' }}
        </el-button>
        <el-button type="danger" :icon="Delete" :disabled="selectedRows.length === 0" :loading="deleting"
          @click="handleBatchDelete">
          批量删除（{{ selectedRows.length }}）
        </el-button>
        <el-button :icon="Upload" @click="openImport">导入用例</el-button>
        <el-button :icon="DocumentCopy" @click="openScriptImport">从脚本导入</el-button>
        <el-button type="primary" :icon="Plus" @click="openCreate">新建用例</el-button>
      </div>

      <div v-if="!isMobile" class="table-wrap">
        <el-table ref="tableRef" v-loading="loading" :data="testCases" row-key="id" height="100%"
          @selection-change="onSelectionChange" @row-click="handleRowSelectionClick">
          <el-table-column type="selection" width="44"
            :selectable="(row: TestCaseSummary) => row.type !== TestType.Mobile" />
          <el-table-column label="用例名称" min-width="240" show-overflow-tooltip fixed="left">
            <template #default="{ row }">
              <el-tag v-if="row.priority" size="small" :type="priorityTagType(row.priority)" class="ai-tag">{{
                row.priority }}</el-tag>
              <el-tag v-if="row.aiGenerated" size="small" type="warning" class="ai-tag">AI</el-tag>
              <el-tooltip v-if="row.isFlaky" placement="top"
                :content="`最近 10 次执行约有 ${Math.round((row.flakeRate ?? 0) * 100)}% 的结果与其余相反，已自动多给一次重试`">
                <el-tag size="small" type="danger" effect="plain" class="ai-tag"
                  @click.stop="handleToggleFlake(row)">不稳定</el-tag>
              </el-tooltip>
              <!-- 名称可点进详情；.stop 避免同时触发行勾选 -->
              <span class="case-name" @click.stop="goDetail(row)">{{ row.name }}</span>
            </template>
          </el-table-column>
          <!-- 所属项目：列表默认筛「全部项目」，跨项目看时得知道每条用例属于谁。
               点项目名直接进项目详情，省得再从菜单绕一圈 -->
          <el-table-column label="所属项目" min-width="140" show-overflow-tooltip>
            <template #default="{ row }">
              <el-button v-if="row.projectName" link type="primary"
                @click.stop="router.push(`/projects/${row.projectId}`)">
                {{ row.projectName }}
              </el-button>
              <span v-else class="muted">-</span>
            </template>
          </el-table-column>
          <el-table-column label="模块" width="150" show-overflow-tooltip>
            <template #default="{ row }">
              <span v-if="row.module">{{ row.module }}</span>
              <span v-else class="muted">-</span>
            </template>
          </el-table-column>
          <!-- 创建人（M8 审计字段）：历史行可能为空 -->
          <el-table-column label="创建人" width="110" show-overflow-tooltip>
            <template #default="{ row }">
              <span v-if="row.createdByName">{{ row.createdByName }}</span>
              <span v-else class="muted">-</span>
            </template>
          </el-table-column>
          <el-table-column label="编号" width="130" show-overflow-tooltip>
            <template #default="{ row }">
              <span v-if="row.caseCode" class="mono">{{ row.caseCode }}</span>
              <span v-else class="muted">-</span>
            </template>
          </el-table-column>
          <el-table-column label="类型" width="100">
            <template #default="{ row }">
              <el-tag>{{ typeLabel(row.type) }}</el-tag>
            </template>
          </el-table-column>
          <!-- 原来这里是「状态」（草稿/启用）。换成最近执行结果：
               日常真正想知道的是"这条用例现在跑得通吗、什么时候跑的"，
               而草稿/启用是整个用例的一个静态属性，详情页看得到就够了。
               时间放在 tag 下方而不是并排——并排会把列撑得很宽。 -->
          <!-- 宽度 140：120 时「2026-09-14 10:59」会被挤成两行 -->
          <el-table-column label="最近执行结果" width="140">
            <template #default="{ row }">
              <template v-if="row.latestExecutionStatus != null">
                <el-tag :type="executionStatusTagType(row.latestExecutionStatus)" size="small">
                  {{ EXECUTION_STATUS_LABELS[row.latestExecutionStatus] ?? '未知' }}
                </el-tag>
                <div v-if="row.lastExecutedAt" class="exec-time">{{ formatDateTime(row.lastExecutedAt) }}</div>
              </template>
              <span v-else class="muted">从未执行</span>
            </template>
          </el-table-column>
          <el-table-column prop="version" label="版本" width="80" />
          <el-table-column label="更新时间" width="180">
            <template #default="{ row }">
              {{ formatDateTime(row.updatedAt) }}
            </template>
          </el-table-column>
          <el-table-column label="操作" width="340" fixed="right">
            <template #default="{ row }">
              <el-button link type="primary" @click="goDetail(row)">详情</el-button>
              <el-button link type="primary" @click="goEdit(row)">编辑</el-button>
              <el-button link type="primary" @click="handleExportScript(row)">导出脚本</el-button>
              <el-button link type="warning" @click="handleToggleFlake(row)">
                {{ row.isFlaky ? '解除不稳定' : '标记不稳定' }}
              </el-button>
              <el-button link type="danger" @click="handleDelete(row)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <!-- 窄屏：表格换成卡片。数据源仍是 testCases，只换渲染方式。
           为什么不留横向滚动：这张表列宽合计 1504px，是 375px 视口的 4 倍，
           而「操作」列（340px）压在最右 —— 横滚等于把最该点的东西藏到 4 屏之外。
           批量操作依赖复选框，在触屏上要多选 + 确认，容易误触，窄屏不提供；
           单条操作（详情/编辑/导出脚本/标记不稳定/删除）全部保留。 -->
      <MobileCardList v-else v-loading="loading" :items="testCases" :row-key="(row) => row.id" empty-text="暂无用例">
        <template #title="{ item }">
          <el-tag v-if="item.priority" size="small" :type="priorityTagType(item.priority)" class="ai-tag">{{
            item.priority }}</el-tag>
          <el-tag v-if="item.aiGenerated" size="small" type="warning" class="ai-tag">AI</el-tag>
          <span class="case-name" @click="goDetail(item)">{{ item.name }}</span>
        </template>

        <template #badge="{ item }">
          <el-tag size="small" :type="statusTagType(item.status)">{{ statusLabel(item.status) }}</el-tag>
          <!-- 表格里这里挂的是 tooltip 解释不稳定度；触屏没有 hover，
               要说明就直接写在卡片上，不让信息只能靠悬停才能看到 -->
          <el-tag v-if="item.isFlaky" size="small" type="danger" effect="plain" @click="handleToggleFlake(item)">
            不稳定 {{ Math.round((item.flakeRate ?? 0) * 100) }}%
          </el-tag>
        </template>

        <template #meta="{ item }">
          <span><span class="mcl-label">项目</span>{{ item.projectName || '-' }}</span>
          <span><span class="mcl-label">模块</span>{{ item.module || '-' }}</span>
          <span><span class="mcl-label">编号</span>{{ item.caseCode || '-' }}</span>
          <span><span class="mcl-label">类型</span>{{ typeLabel(item.type) }}</span>
          <span><span class="mcl-label">版本</span>v{{ item.version }}</span>
          <span><span class="mcl-label">更新</span>{{ formatDateTime(item.updatedAt) }}</span>
        </template>

        <template #actions="{ item }">
          <el-button link type="primary" @click="goDetail(item)">详情</el-button>
          <el-button link type="primary" @click="goEdit(item)">编辑</el-button>
          <el-button link type="primary" @click="handleExportScript(item)">导出脚本</el-button>
          <el-button link type="warning" @click="handleToggleFlake(item)">
            {{ item.isFlaky ? '解除不稳定' : '标记不稳定' }}
          </el-button>
          <el-button link type="danger" @click="handleDelete(item)">删除</el-button>
        </template>
      </MobileCardList>

      <el-pagination class="pagination" v-model:current-page="page" v-model:page-size="pageSize" :total="total"
        :page-sizes="[10, 20, 50]" layout="total, sizes, prev, pager, next" @current-change="load()"
        @size-change="load(1)" />
    </el-card>

    <el-dialog v-model="dialogVisible" title="新建用例" width="600px" @closed="resetForm">
      <el-form ref="formRef" :model="form" :rules="formRules" label-width="90px">
        <el-form-item label="所属项目" prop="projectId">
          <el-select v-model="form.projectId" placeholder="选择项目" filterable class="w-full">
            <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="用例名称" prop="name">
          <el-input v-model="form.name" placeholder="请输入用例名称" />
        </el-form-item>
        <el-form-item label="类型" prop="type">
          <el-select v-model="form.type" class="w-full">
            <el-option label="Web" :value="TestType.Web" />
            <el-option label="API" :value="TestType.Api" />
          </el-select>
        </el-form-item>
        <el-form-item label="描述" prop="description">
          <el-input v-model="form.description" type="textarea" :rows="3" placeholder="用例描述（可选）" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="handleSave">保存</el-button>
      </template>
    </el-dialog>
    <!-- 批量编辑：字段留空 = 不修改该项；只覆盖白名单内的字段 -->
    <el-dialog v-model="batchEditVisible" title="批量编辑" width="480px" @closed="resetBatchEdit">
      <el-form label-position="top">
        <el-form-item label="项目">
          <el-select v-model="batchEditForm.projectId" placeholder="留空则不修改" clearable filterable style="width: 100%"
            @change="onBatchEditProjectChange">
            <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
          </el-select>
          <div v-if="batchEditForm.projectId" class="batch-edit-hint">
            改项目后原关联需求会被清空，下方需求可重新指定
          </div>
        </el-form-item>
        <el-form-item label="需求">
          <el-select v-model="batchEditForm.requirementId" placeholder="留空则不修改" clearable filterable style="width: 100%"
            :loading="batchReqLoading" :disabled="!batchReqProjectId">
            <el-option v-for="r in batchRequirementOptions" :key="r.id" :label="r.title" :value="r.id" />
          </el-select>
          <div v-if="!batchReqProjectId" class="batch-edit-hint">先选择项目（或用例已在某项目下）才能选需求</div>
        </el-form-item>

        <el-form-item label="模块">
          <el-input v-model="batchEditForm.module" placeholder="留空则不修改" maxlength="50" clearable />
        </el-form-item>
        <el-form-item label="优先级">
          <el-select v-model="batchEditForm.priority" placeholder="留空则不修改" clearable style="width: 100%">
            <el-option v-for="p in PRIORITY_OPTIONS" :key="p" :label="p" :value="p" />
          </el-select>
        </el-form-item>
        <el-form-item label="状态">
          <el-select v-model="batchEditForm.status" placeholder="留空则不修改" clearable style="width: 100%">
            <el-option label="草稿" :value="0" />
            <el-option label="启用" :value="1" />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="batchEditVisible = false">取消</el-button>
        <el-button type="primary" :loading="batchEditSaving" @click="handleBatchEdit">
          应用到 {{ selectedRows.length }} 条
        </el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="batchDialogVisible" title="批量执行" width="480px">
      <div class="batch-body">
        <p>将按顺序入队 <b>{{ selectedRows.length }}</b> 条用例（当前队列串行执行）。</p>
        <el-select v-model="batchEnvironmentId" placeholder="选择执行环境（可选）" clearable class="w-full">
          <el-option label="不使用环境" value="" />
          <el-option v-for="env in environments" :key="env.id" :label="`${env.name}（${env.baseUrl}）`" :value="env.id" />
        </el-select>
        <div class="batch-field">
          <div class="batch-label">浏览器矩阵</div>
          <el-select v-model="batchBrowsers" multiple placeholder="不指定则按环境/用例配置" class="w-full">
            <el-option v-for="b in BROWSER_OPTIONS" :key="b.id" :label="b.label" :value="b.id" />
          </el-select>
          <div class="batch-hint">多选时同一条用例会在每个浏览器上各跑一次</div>
        </div>
        <div class="batch-field">
          <el-checkbox v-model="batchExpandDataSets">按数据行展开（用例绑定数据集时）</el-checkbox>
        </div>
      </div>
      <template #footer>
        <el-button @click="batchDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="batchExecuting" @click="handleBatchExecute">开始执行</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="importDialogVisible" title="导入测试用例（Excel）" width="80%" @closed="resetImport">
      <el-form label-width="110px">
        <el-form-item label="所属项目" required>
          <el-select v-model="importForm.projectId" placeholder="选择项目" filterable class="w-full">
            <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
          </el-select>
        </el-form-item>
        <el-form-item label="关联测试计划">
          <el-select v-model="importForm.testPlanId" :placeholder="importForm.projectId ? '可选：导入的用例将自动加入该计划' : '请先选择项目'"
            :disabled="!importForm.projectId" :loading="plansLoading" filterable clearable class="w-full">
            <el-option v-for="p in planOptions" :key="p.id" :label="p.name" :value="p.id" />
          </el-select>
          <span class="hint">仅列出所选项目下的测试计划；导入完成后在「测试计划」详情页可查看已关联用例</span>
        </el-form-item>
        <el-form-item label="Excel 文件" required>
          <div class="import-file-row">
            <el-upload ref="uploadRef" :auto-upload="false" :limit="1" accept=".xlsx" :on-change="onImportFileChange"
              :on-remove="() => (importFile = null)">
              <el-button :icon="Upload">选择文件</el-button>
            </el-upload>
            <el-button class="template-btn" type="primary" plain :icon="Download" :loading="downloadingTemplate"
              @click="handleDownloadTemplate">下载导入模板</el-button>
          </div>
          <div class="el-upload__tip">
            <el-alert type="warning" :closable="false" class="script-tip" title="仅支持 .xlsx；模板要求：每个模块一个工作表，表头含「用例编号/测试场景/操作步骤/预期结果/优先级/类别」。
            首次使用建议先下载模板，按示例整理后上传。" />
          </div>
        </el-form-item>
        <el-form-item label="AI 生成步骤">
          <el-switch v-model="importForm.useAi" />
          <span class="hint">开启后调用 AI Worker 把「操作步骤+预期结果」转成可执行步骤（用例较多时耗时较长，失败的行按基础信息导入）</span>
        </el-form-item>
        <el-form-item label="被测地址">
          <el-input v-model="importForm.baseUrl" placeholder="http://localhost:3000（可选，作为步骤默认基地址）" />
        </el-form-item>
        <el-form-item label="同名编号">
          <el-radio-group v-model="importForm.overwrite">
            <el-radio :value="false">跳过</el-radio>
            <el-radio :value="true">覆盖更新</el-radio>
          </el-radio-group>
          <span class="hint">以「模块 + 用例编号」为去重键</span>
        </el-form-item>
      </el-form>

      <div v-if="importResult" class="import-result">
        <el-alert :type="importResult.failed > 0 ? 'warning' : 'success'" :closable="false" show-icon
          :title="`共 ${importResult.totalRows} 行：新增 ${importResult.imported}，更新 ${importResult.updated}，跳过 ${importResult.skipped}，失败 ${importResult.failed}`" />
        <p v-if="importForm.useAi" class="hint">
          AI 已生成可执行步骤 {{ importResult.aiParsed }} / {{ importResult.aiCaseCount }} 条
        </p>
        <el-alert v-if="(importResult.planLinked ?? 0) + (importResult.planSkipped ?? 0) > 0" type="success"
          :closable="false" class="result-line" :title="`已关联测试计划：新增 ${importResult.planLinked ?? 0} 条` +
            (importResult.planSkipped ? `，${importResult.planSkipped} 条已在计划范围内跳过` : '')" />
        <el-table :data="importResult.modules" size="small" max-height="220" class="result-table">
          <el-table-column prop="module" label="模块" min-width="160" show-overflow-tooltip />
          <el-table-column prop="total" label="行数" width="70" />
          <el-table-column prop="imported" label="导入" width="70" />
          <el-table-column prop="skipped" label="跳过" width="70" />
          <el-table-column prop="failed" label="失败" width="70" />
        </el-table>
        <el-alert v-for="(w, i) in importResult.warnings" :key="`w${i}`" type="info" :closable="false"
          class="result-line" :title="w" />
        <el-alert v-for="(e, i) in importResult.errors.slice(0, 5)" :key="`e${i}`" type="error" :closable="false"
          class="result-line" :title="`${e.module} 第 ${e.rowNumber} 行：${e.message}`" />
      </div>

      <template #footer>
        <el-button @click="importDialogVisible = false">关闭</el-button>
        <el-button type="primary" :loading="importing" @click="handleImport">
          {{ importing ? '导入中（AI 解析可能需要数分钟）' : '开始导入' }}
        </el-button>
      </template>
    </el-dialog>
    <el-dialog v-model="scriptDialogVisible" title="从 Playwright 脚本导入用例" width="80%" destroy-on-close top="6vh">
      <el-alert type="warning" :closable="false" class="script-tip"
        title="把 codegen 生成的脚本（或手写脚本）粘贴进来即可转成平台步骤；语义定位（getByRole/getByText 等）会自动转成 AI 定位（描述式），运行时可自愈。" />

      <el-form label-width="90px">
        <el-form-item label="脚本内容">
          <el-input v-model="scriptText" type="textarea" :rows="15" spellcheck="false"
            placeholder="npx playwright codegen http://localhost:3000  →  复制生成的脚本粘贴到这里" />
          <div class="script-actions">
            <el-button size="small" :loading="parsing" @click="handleParseScript">解析预览</el-button>
            <span v-if="parseResult" class="script-summary">
              {{ parseResult.steps.length }} 个步骤
              <template v-if="parseResult.suggestedName">｜建议名称：{{ parseResult.suggestedName }}</template>
              <template v-if="parseResult.suggestedBaseUrl">｜基地址：{{ parseResult.suggestedBaseUrl }}</template>
            </span>
          </div>
        </el-form-item>

        <div v-if="parseResult && parseResult.warnings.length > 0" class="script-warnings">
          <el-alert v-for="(w, i) in parseResult.warnings.slice(0, 6)" :key="i" type="warning" :closable="false"
            class="script-warning" :title="w" />
          <div v-if="parseResult.warnings.length > 6" class="script-summary">
            还有 {{ parseResult.warnings.length - 6 }} 条未映射的行（已跳过）
          </div>
        </div>

        <el-table v-if="parseResult && parseResult.steps.length > 0" :data="parseResult.steps" size="small"
          max-height="220" class="script-preview">
          <el-table-column label="#" width="50" prop="stepOrder" />
          <el-table-column label="动作" width="120">
            <template #default="{ row }">{{ ACTION_LABELS[row.actionType] ?? row.actionType }}</template>
          </el-table-column>
          <el-table-column label="识别到的目标" min-width="240">
            <template #default="{ row }">
              <span>{{ row.description || row.config?.url || row.config?.endpoint || '—' }}</span>
              <el-tag v-if="row.note" size="small" type="warning" class="script-note">{{ row.note }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="原始脚本行" min-width="240" show-overflow-tooltip prop="sourceLine" />
        </el-table>

        <div class="script-form">
          <el-form-item label="所属项目" required>
            <el-select v-model="scriptForm.projectId" placeholder="选择项目" filterable>
              <el-option v-for="p in projectOptions" :key="p.id" :label="p.name" :value="p.id" />
            </el-select>
          </el-form-item>
          <el-form-item label="测试计划">
            <el-select v-model="scriptForm.testPlanId"
              :placeholder="scriptForm.projectId ? '可选：导入的用例将自动加入该计划' : '请先选择项目'" :disabled="!scriptForm.projectId"
              :loading="scriptPlansLoading" filterable clearable>
              <el-option v-for="p in scriptPlanOptions" :key="p.id" :label="p.name" :value="p.id" />
            </el-select>
          </el-form-item>
          <el-form-item label="用例名称">
            <el-input v-model="scriptForm.name" placeholder="留空则用脚本里的 test 标题" maxlength="200" />
          </el-form-item>
          <el-form-item label="模块">
            <el-input v-model="scriptForm.module" placeholder="可选" maxlength="100" />
          </el-form-item>
          <el-form-item label="优先级">
            <el-select v-model="scriptForm.priority" placeholder="未设置" clearable>
              <el-option v-for="p in PRIORITY_OPTIONS" :key="p" :label="p" :value="p" />
            </el-select>
          </el-form-item>
          <el-form-item label="BaseUrl">
            <el-input v-model="scriptForm.baseUrl" placeholder="留空则用脚本里第一个绝对 URL" />
          </el-form-item>
        </div>
      </el-form>

      <template #footer>
        <el-button @click="scriptDialogVisible = false">关闭</el-button>
        <el-button type="primary" :loading="importingScript"
          :disabled="!parseResult || parseResult.steps.length === 0 || !scriptForm.projectId"
          @click="handleImportScript">
          导入为用例
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import { Delete, DocumentCopy, Download, Plus, RefreshLeft, Upload, VideoPlay, EditPen } from '@element-plus/icons-vue'
import type { UploadFile, UploadInstance } from 'element-plus'
import { getProjects } from '@/api/project'
import {
  getTestCases, createTestCase, deleteTestCase, getTestCaseModules, importTestCases,
  downloadImportTemplate, batchDeleteTestCases, batchUpdateTestCases, resetTestCaseFlake, setTestCaseFlake,
} from '@/api/testcase'
import { useBatchDelete } from '@/composables/useBatchDelete'
import { useRowSelection } from '@/composables/useRowSelection'
import { saveBlobAsFile } from '@/api/report'
import { batchExecute } from '@/api/execution'
import { listTestPlansApi } from '@/api/testPlan'
import { exportScript, importScript, parseScript, type ScriptParseResult } from '@/api/script'
import { getEnvironments } from '@/api/environment'
import { getRequirement, getRequirements } from '@/api/requirement'
import type { RequirementListItem } from '@/types/requirement'
import { formatDateTime } from '@/utils/formatter'
import { EXECUTION_STATUS_LABELS, executionStatusTagType } from '@/types/execution'
import {
  TestType, TestCaseStatus, CASE_EXEC_FILTER_OPTIONS,
  type TestCaseImportResult, type TestCaseModuleStat, type TestCaseSummary,
} from '@/types/testcase'
import type { Project } from '@/types/project'
import type { EnvironmentView } from '@/types/environment'
import MobileCardList from '@/components/common/MobileCardList.vue'
import { useBreakpoint } from '@/composables/useBreakpoint'

const route = useRoute()
const router = useRouter()

/** 窄屏（< 1024px）：表格换成卡片形态，见下方模板 */
const { isMobile } = useBreakpoint()

const projectOptions = ref<Project[]>([])
const projectId = ref('')
const testCases = ref<TestCaseSummary[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(10)
const search = ref('')
const moduleFilter = ref('')
const moduleOptions = ref<TestCaseModuleStat[]>([])
/**
 * 按「最近一次执行结果」筛选（见 CaseExecFilter）。
 * 读一次 URL 参数，便于从别处（如仪表盘上的失败数）直接跳过来就带好筛选。
 */
const execStateFilter = ref<number | undefined>(
  route.query.execState != null && route.query.execState !== ''
    ? Number(route.query.execState)
    : undefined,
)
// ------------------------------------------------------------ flake（不稳定用例）
const flakyOnly = ref(route.query.flakyOnly === 'true')
/**
 * 按「关联需求」筛选：需求覆盖页点「关联用例」数字跳过来时带 requirementId。
 * 这里只认 URL 上的 id，标题另外查一次用于展示筛选标识（否则用户只看到一堆用例，
 * 不知道当前被过滤了）。
 */
const requirementFilterId = ref(
  typeof route.query.requirementId === 'string' ? route.query.requirementId : '',
)
const requirementFilterTitle = ref('')

const clearRequirementFilter = () => {
  requirementFilterId.value = ''
  requirementFilterTitle.value = ''
  const query = { ...route.query }
  delete query.requirementId
  void router.replace({ query })
  void load(1)
}
const resettingFlake = ref(false)
/** 选中行中仍带不稳定标记的数量（决定「解除标记」按钮是否可用） */
const flakySelectedCount = computed(() => selectedRows.value.filter((r) => r.isFlaky).length)
const loading = ref(false)

/** 可选浏览器（与后端 BrowserCatalog 对齐） */
const BROWSER_OPTIONS = [
  { id: 'chromium', label: 'Chromium' },
  { id: 'firefox', label: 'Firefox' },
  { id: 'webkit', label: 'WebKit' },
]

const PRIORITY_OPTIONS = ['P0', 'P1', 'P2', 'P3']

/** 动作类型中文名（脚本解析预览用） */
const ACTION_LABELS: Record<number, string> = {
  0: '点击', 1: '输入', 2: '打开页面', 3: '等待', 4: '截图', 5: '滚动',
  6: '接口请求', 7: '响应断言', 8: '提取变量', 9: 'AI 动作', 10: 'AI 断言',
  11: '断言可见', 12: '断言文本', 13: '断言地址', 14: '断言标题',
}

const dialogVisible = ref(false)
const saving = ref(false)

// ------------------------------------------------------------ 脚本导入 / 导出
const scriptDialogVisible = ref(false)
const scriptText = ref('')
const parsing = ref(false)
const importingScript = ref(false)
const parseResult = ref<ScriptParseResult | null>(null)
const scriptForm = reactive({
  projectId: '',
  testPlanId: '',
  name: '',
  module: '',
  priority: '',
  baseUrl: '',
})

const openScriptImport = () => {
  scriptText.value = ''
  parseResult.value = null
  scriptForm.projectId = projectId.value || projectOptions.value[0]?.id || ''
  scriptForm.testPlanId = ''
  // 首次打开或项目值未变化时 watch 不触发，这里显式拉一次计划选项
  void (async () => {
    scriptPlanOptions.value = []
    if (!scriptForm.projectId) return
    scriptPlansLoading.value = true
    try {
      scriptPlanOptions.value = await fetchPlanOptions(scriptForm.projectId)
    } finally {
      scriptPlansLoading.value = false
    }
  })()
  scriptForm.name = ''
  scriptForm.module = moduleFilter.value || ''
  scriptForm.priority = ''
  scriptForm.baseUrl = ''
  scriptDialogVisible.value = true
}

const handleParseScript = async () => {
  if (scriptText.value.trim().length === 0) {
    ElMessage.warning('请先粘贴 Playwright 脚本')
    return
  }
  parsing.value = true
  try {
    parseResult.value = await parseScript(scriptText.value)
    if (parseResult.value.suggestedName && !scriptForm.name) scriptForm.name = parseResult.value.suggestedName
    if (parseResult.value.suggestedBaseUrl && !scriptForm.baseUrl) scriptForm.baseUrl = parseResult.value.suggestedBaseUrl
    ElMessage.success(`解析出 ${parseResult.value.steps.length} 个步骤`)
  } catch {
    parseResult.value = null
  } finally {
    parsing.value = false
  }
}

const handleImportScript = async () => {
  if (!parseResult.value) return
  if (!scriptForm.projectId) {
    ElMessage.warning('请选择所属项目')
    return
  }
  importingScript.value = true
  try {
    const result = await importScript({
      projectId: scriptForm.projectId,
      name: scriptForm.name.trim() || parseResult.value.suggestedName || '导入的用例',
      script: scriptText.value,
      module: scriptForm.module.trim() || null,
      priority: scriptForm.priority || null,
      baseUrl: scriptForm.baseUrl.trim() || null,
      testPlanId: scriptForm.testPlanId || null,
    })
    const planSuffix = result.planLinked
      ? `，已关联测试计划 ${result.planLinked} 条` +
      (result.planSkipped ? `（${result.planSkipped} 条已在范围内跳过）` : '')
      : ''
    ElMessage.success(`已创建用例「${result.name}」（${result.stepCount} 个步骤）${planSuffix}`)
    scriptDialogVisible.value = false
    if (!projectId.value) projectId.value = scriptForm.projectId
    await load(1)
  } finally {
    importingScript.value = false
  }
}

/** 导出为 Playwright 脚本（可直接入 Git） */
const handleExportScript = async (row: TestCaseSummary) => {
  const result = await exportScript(row.id)
  const blob = new Blob([result.script], { type: 'text/plain;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `${(row.caseCode || row.name).replace(/[\\/:*?"<>|]/g, '_')}.spec.ts`
  link.click()
  URL.revokeObjectURL(url)
  ElMessage.success('脚本已导出')
}
const formRef = ref<FormInstance>()
const form = reactive({
  projectId: '',
  name: '',
  type: TestType.Web,
  description: '',
})
const formRules: FormRules = {
  projectId: [{ required: true, message: '请选择项目', trigger: 'change' }],
  name: [{ required: true, message: '请输入用例名称', trigger: 'blur' }],
}

/** 优先级标签配色：P0 红 / P1 橙 / P2 蓝 / P3 灰 */
const priorityTagType = (priority: string) =>
  ({ P0: 'danger', P1: 'warning', P2: 'primary', P3: 'info' }[priority] ?? 'info') as
  'danger' | 'warning' | 'primary' | 'info'

const typeLabel = (type: TestType) => {
  return { [TestType.Web]: 'Web', [TestType.Api]: 'API', [TestType.Mobile]: '移动端' }[type] ?? '未知'
}

// 批量操作（执行 / 删除）
// ------------------------------ 批量编辑（字段白名单：模块/优先级/状态/项目/需求，留空不改）
const batchEditVisible = ref(false)
const batchEditSaving = ref(false)
const batchEditForm = reactive<{
  module: string | null
  priority: string | null
  status: number | null
  projectId: string | null
  requirementId: string | null
}>({
  module: null, priority: null, status: null, projectId: null, requirementId: null,
})

/** 需求下拉按项目过滤：显式选了项目按它拉；没选项目则禁用（跨项目勾选无法确定锚点） */
const batchRequirementOptions = ref<RequirementListItem[]>([])
const batchReqLoading = ref(false)
const batchReqProjectId = computed(() => batchEditForm.projectId ?? '')

const loadBatchRequirements = async (projectId: string) => {
  batchReqLoading.value = true
  try {
    const res = await getRequirements({ projectId, pageSize: 200 })
    batchRequirementOptions.value = res.items
  } finally {
    batchReqLoading.value = false
  }
}

/** 项目切换：需求选项跟着换，已选需求先清掉（需求必须属于目标项目） */
const onBatchEditProjectChange = async (pid: string | null) => {
  batchEditForm.requirementId = null
  batchRequirementOptions.value = []
  if (pid) await loadBatchRequirements(pid)
}

const openBatchEdit = () => {
  if (selectedRows.value.length === 0) {
    ElMessage.info('请先在列表中勾选要编辑的用例')
    return
  }
  batchEditVisible.value = true
}
const resetBatchEdit = () => {
  batchEditForm.module = null
  batchEditForm.priority = null
  batchEditForm.status = null
  batchEditForm.projectId = null
  batchEditForm.requirementId = null
  batchRequirementOptions.value = []
}
const handleBatchEdit = async () => {
  if (batchEditForm.module == null && batchEditForm.priority == null && batchEditForm.status == null
    && batchEditForm.projectId == null && batchEditForm.requirementId == null) {
    ElMessage.warning('请至少填写一个要修改的字段')
    return
  }
  batchEditSaving.value = true
  try {
    const result = await batchUpdateTestCases({
      ids: selectedRows.value.map((r: TestCaseSummary) => r.id),
      module: batchEditForm.module,
      priority: batchEditForm.priority,
      status: batchEditForm.status,
      projectId: batchEditForm.projectId,
      requirementId: batchEditForm.requirementId,
    })
    ElMessage.success(`已更新 ${result.updated} 条`)
    batchEditVisible.value = false
    await load()
  } finally {
    batchEditSaving.value = false
  }
}

const { deleting, selectedRows, onSelectionChange, handleBatchDelete } =
  useBatchDelete<TestCaseSummary>({
    entity: '测试用例',
    remove: batchDeleteTestCases,
    reload: () => load(),
  })

/** 点击行直接勾选（与复选框列同一 selectable 规则：移动端用例不参与批量操作） */
const { tableRef, handleRowSelectionClick } = useRowSelection(
  (row: TestCaseSummary) => row.type !== TestType.Mobile,
)

const batchDialogVisible = ref(false)
const batchExecuting = ref(false)
const batchEnvironmentId = ref('')
const batchBrowsers = ref<string[]>([])
const batchExpandDataSets = ref(true)
const environments = ref<EnvironmentView[]>([])

const loadEnvironments = async () => {
  if (environments.value.length > 0) return
  // 环境按项目隔离：取当前筛选项目的环境；未筛选时取第一个有环境的项目兜底
  const targetProjectId = projectId.value || selectedRows.value[0]?.projectId
  if (!targetProjectId) return
  try {
    environments.value = await getEnvironments(targetProjectId)
  } catch {
    environments.value = []
  }
}

const handleBatchExecute = async () => {
  if (selectedRows.value.length === 0) return
  batchExecuting.value = true
  try {
    const res = await batchExecute({
      testCaseIds: selectedRows.value.map((r) => r.id),
      environmentId: batchEnvironmentId.value || null,
      browsers: batchBrowsers.value.length > 0 ? batchBrowsers.value : null,
      expandDataSets: batchExpandDataSets.value,
    })
    const dataHint = res.casesWithoutData > 0 ? `，${res.casesWithoutData} 条用例的数据集为空` : ''
    ElMessage.success(
      `已入队 ${res.created} 条执行${res.skippedCaseIds.length > 0 ? `，${res.skippedCaseIds.length} 条被跳过（不支持的类型）` : ''}${dataHint}`,
    )
    batchDialogVisible.value = false
    router.push('/executions')
  } finally {
    batchExecuting.value = false
  }
}

const statusLabel = (status: TestCaseStatus) => {
  return {
    [TestCaseStatus.Draft]: '草稿',
    [TestCaseStatus.Active]: '启用',
  }[status] ?? '未知'
}

const statusTagType = (status: TestCaseStatus) => {
  return {
    [TestCaseStatus.Draft]: 'info',
    [TestCaseStatus.Active]: 'success',
  }[status] ?? 'info'
}

const loadProjects = async () => {
  const res = await getProjects({ page: 1, pageSize: 100 })
  projectOptions.value = res.items
}

const load = async (targetPage?: number) => {
  if (targetPage) page.value = targetPage
  loading.value = true
  try {
    const res = await getTestCases({
      projectId: projectId.value || undefined,
      search: search.value || undefined,
      module: moduleFilter.value || undefined,
      requirementId: requirementFilterId.value || undefined,
      flakyOnly: flakyOnly.value || undefined,
      // 清空 el-select 后拿到的可能是 '' 或 undefined；直接透传会被后端当成枚举解析而 400，
      // 所以只认真正的数字（0=未执行 是合法值，不能用真值判断）
      execState: typeof execStateFilter.value === 'number' ? execStateFilter.value : undefined,
      page: page.value,
      pageSize: pageSize.value,
    })
    testCases.value = res.items
    total.value = res.total
  } finally {
    loading.value = false
  }
}

const openCreate = () => {
  form.projectId = projectId.value || projectOptions.value[0]?.id || ''
  dialogVisible.value = true
}

const loadModules = async () => {
  try {
    moduleOptions.value = await getTestCaseModules(projectId.value || undefined)
  } catch {
    moduleOptions.value = []
  }
}

const openBatchDialog = () => {
  void loadEnvironments()
  batchDialogVisible.value = true
}

// ------------------------------------------------------------ Excel 导入
const importDialogVisible = ref(false)
const importing = ref(false)
const importFile = ref<File | null>(null)
const uploadRef = ref<UploadInstance>()
const importResult = ref<TestCaseImportResult | null>(null)
const importForm = reactive({
  projectId: '',
  testPlanId: '',
  useAi: true,
  overwrite: false,
  baseUrl: '',
})

// 可选的关联测试计划：按所选项目过滤（跨项目计划不允许关联）。
// Excel 导入与脚本导入两个对话框各有一份选项缓存，拉取逻辑共用。
const planOptions = ref<{ id: string; name: string }[]>([])
const scriptPlanOptions = ref<{ id: string; name: string }[]>([])
const plansLoading = ref(false)
const scriptPlansLoading = ref(false)
const fetchPlanOptions = async (projectId: string) => {
  if (!projectId) return []
  const result = await listTestPlansApi({ projectId, pageSize: 100 })
  return result.items.map((p) => ({ id: p.id, name: p.name }))
}
const loadPlanOptions = async (projectId: string) => {
  planOptions.value = []
  if (!projectId) return
  plansLoading.value = true
  try {
    planOptions.value = await fetchPlanOptions(projectId)
  } finally {
    plansLoading.value = false
  }
}
// 项目切换：已选计划立即失效（计划与项目强绑定），并重新拉取该项目下的计划
watch(
  () => importForm.projectId,
  (newId, oldId) => {
    if (newId !== oldId) {
      importForm.testPlanId = ''
      void loadPlanOptions(newId)
    }
  },
)
// 脚本导入对话框：同上
watch(
  () => scriptForm.projectId,
  (newId, oldId) => {
    if (newId !== oldId) {
      scriptForm.testPlanId = ''
      void (async () => {
        scriptPlanOptions.value = []
        if (!newId) return
        scriptPlansLoading.value = true
        try {
          scriptPlanOptions.value = await fetchPlanOptions(newId)
        } finally {
          scriptPlansLoading.value = false
        }
      })()
    }
  },
)

const downloadingTemplate = ref(false)

/** 下载导入模板（含填写说明与示例模块） */
const handleDownloadTemplate = async () => {
  downloadingTemplate.value = true
  try {
    const blob = await downloadImportTemplate()
    saveBlobAsFile(blob, '测试用例导入模板.xlsx')
    ElMessage.success('模板已开始下载')
  } finally {
    downloadingTemplate.value = false
  }
}

const openImport = () => {
  importForm.projectId = projectId.value || projectOptions.value[0]?.id || ''
  importForm.testPlanId = ''
  // 首次打开或项目值未变化时 watch 不触发，这里显式拉一次计划选项
  void loadPlanOptions(importForm.projectId)
  importDialogVisible.value = true
}

const onImportFileChange = (file: UploadFile) => {
  importFile.value = (file.raw as File) ?? null
}

const resetImport = () => {
  importFile.value = null
  importResult.value = null
  uploadRef.value?.clearFiles()
}

const handleImport = async () => {
  if (!importForm.projectId) {
    ElMessage.warning('请选择所属项目')
    return
  }
  if (!importFile.value) {
    ElMessage.warning('请选择要导入的 Excel 文件')
    return
  }
  const form = new FormData()
  form.append('file', importFile.value)
  form.append('projectId', importForm.projectId)
  if (importForm.testPlanId) form.append('testPlanId', importForm.testPlanId)
  form.append('useAi', String(importForm.useAi))
  form.append('overwrite', String(importForm.overwrite))
  if (importForm.baseUrl) form.append('baseUrl', importForm.baseUrl)

  importing.value = true
  try {
    importResult.value = await importTestCases(form)
    ElMessage.success(
      `导入完成：新增 ${importResult.value.imported}，更新 ${importResult.value.updated}，跳过 ${importResult.value.skipped}`,
    )
    if (!projectId.value) projectId.value = importForm.projectId
    await Promise.all([load(1), loadModules()])
  } finally {
    importing.value = false
  }
}

const resetForm = () => {
  form.name = ''
  form.description = ''
  formRef.value?.clearValidate()
}

const handleSave = async () => {
  try {
    await formRef.value?.validate()
  } catch {
    return
  }
  saving.value = true
  try {
    await createTestCase({
      projectId: form.projectId,
      name: form.name,
      type: form.type,
      description: form.description || null,
      browser: 'chrome',
      timeout: 30000,
      retryCount: 0,
      steps: [],
    })
    ElMessage.success('用例已创建')
    dialogVisible.value = false
    if (!projectId.value) projectId.value = form.projectId
    await load()
  } finally {
    saving.value = false
  }
}

const goDetail = (row: TestCaseSummary) => {
  router.push(`/testcases/${row.id}`)
}

const goEdit = (row: TestCaseSummary) => {
  router.push(`/testcases/${row.id}/edit`)
}

const handleDelete = async (row: TestCaseSummary) => {
  try {
    await ElMessageBox.confirm(`确认删除用例「${row.name}」？`, '提示', { type: 'warning' })
  } catch {
    return
  }
  await deleteTestCase(row.id)
  ElMessage.success('用例已删除')
  await load()
}

// ------------------------------------------------------------ 不稳定用例（flake）

/** 单条：手动标记 / 解除不稳定（自动识别之外的人工干预） */
const handleToggleFlake = async (row: TestCaseSummary) => {
  const next = !row.isFlaky
  await setTestCaseFlake(row.id, next)
  ElMessage.success(next ? '已标记为不稳定用例' : '已解除不稳定标记')
  await load()
}

/** 批量：解除选中行的不稳定标记，重置统计基准 */
const handleResetFlake = async () => {
  const ids = selectedRows.value.filter((r) => r.isFlaky).map((r) => r.id)
  if (ids.length === 0) return
  resettingFlake.value = true
  try {
    const res = await resetTestCaseFlake(ids)
    ElMessage.success(`已解除 ${res.reset} 条记录的不稳定标记`)
    await load()
  } finally {
    resettingFlake.value = false
  }
}

onMounted(async () => {
  projectId.value = (route.query.projectId as string) || ''
  if (requirementFilterId.value) {
    // 标题拉不到不影响筛选本身，静默降级成只显示 id
    getRequirement(requirementFilterId.value)
      .then((r) => { requirementFilterTitle.value = r.title })
      .catch(() => { requirementFilterTitle.value = '' })
  }
  await loadProjects()
  await Promise.all([load(), loadModules()])
})
</script>

<style scoped>
/* 页面撑满视口：卡片自适应高度，表格区域内部滚动，分页固定在底部 */
.testcase-list {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.list-card {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

/* el-card 主体参与纵向布局，表格才能占满剩余高度 */
.list-card :deep(.el-card__body) {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

.table-wrap {
  flex: 1;
  min-height: 0;
}



.ai-tag {
  margin-right: 6px;
}

.module-select {
  width: 190px;
}

/* 比模块窄：选项文案最长「执行中」三个字，不需要 190px */
.exec-select {
  width: 150px;
}

.flaky-filter {
  margin-left: 4px;
}

.muted {
  color: var(--el-text-color-placeholder);
}

/* 用例名称可点击进详情（链接样式提示可点） */
.case-name {
  cursor: pointer;
  color: var(--el-color-primary);
}

.case-name:hover {
  text-decoration: underline;
}

/* 「最近执行结果」列里的时间：比 tag 低一档，不抢视线但能回答"什么时候跑的" */
.exec-time {
  margin-top: 2px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.mono {
  font-family: var(--el-font-family-mono, monospace);
  font-size: 12px;
}

.hint {
  margin-left: 8px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.import-result {
  margin-top: 8px;
}

/* 注意：el-upload 容器内含「已选文件列表」，整体比按钮高；
   若用 align-items:center 会让右侧按钮按容器高度居中而显得偏低，故顶部对齐并固定按钮高度 */
.import-file-row {
  display: flex;
  align-items: flex-start;
  gap: 12px;
}

.import-file-row :deep(.el-upload) {
  display: flex;
  align-items: center;
}

.import-file-row .template-btn {
  height: 32px;
  margin-top: 0;
}

.result-table {
  margin: 10px 0;
}

.result-line {
  margin-top: 6px;
}

.toolbar {
  display: flex;
  gap: 12px;
  margin-bottom: 16px;
  flex-wrap: wrap;
}

.project-select {
  width: 200px;
}

.search-input {
  width: 300px;
}

.pagination {
  margin-top: 16px;
  justify-content: flex-end;
}

.w-full {
  width: 100%;
}

.script-tip {
  margin-bottom: 12px;
}

.script-actions {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: 6px;
}

.script-summary {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.script-warnings {
  margin-bottom: 8px;
}

.script-warning {
  margin-bottom: 4px;
}

.script-note {
  margin-left: 6px;
}

.script-preview {
  margin-bottom: 12px;
}

.script-form {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0 16px;
}

/* 窄屏：表单改单列（弹窗在 375px 下只有 ~345px 宽，两栏各剩 160px 不够填） */
@media (max-width: 767px) {
  .script-form {
    grid-template-columns: minmax(0, 1fr);
  }
}

.batch-field {
  margin-top: 12px;
}

/* 批量编辑：项目/需求字段的辅助说明 */
.batch-edit-hint {
  margin-top: 4px;
  font-size: 12px;
  color: #9aa2ae;
  line-height: 1.5;
}

.batch-label {
  margin-bottom: 6px;
  font-size: 13px;
  color: var(--el-text-color-regular);
}

.batch-hint {
  margin-top: 4px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.batch-body p {
  margin: 0 0 12px;
  color: #606266;
}
</style>
