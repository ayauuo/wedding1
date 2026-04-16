<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import type { ScreenName } from '@/types/photobooth'
import { usePhotobooth } from '@/composables/usePhotobooth'

const {
  showScreen,
  templates,
  selectedTemplate,
  getDefaultTemplateIndex,
  selectTemplate,
  setResultMock,
  setCaptureResultsFromTestImages,
  setTestSession,
  resetSession,
  callHost,
  isTestSession,
} = usePhotobooth()

/** 正式環境設 VITE_ALLOW_DEV_PANEL=0 或 false，雙擊空白鍵不會開啟測試面板 */
const allowDevPanel = computed(() => {
  const v = import.meta.env.VITE_ALLOW_DEV_PANEL
  return v === '1' || String(v).toLowerCase() === 'true'
})

const hidden = ref(true)
const uploadCloudMessage = ref('')
const dbClearMessage = ref('')
const clearTestMessage = ref('')
const DOUBLE_TAP_MS = 400
let lastSpaceAt = 0

async function onUploadCloud() {
  uploadCloudMessage.value = '上傳中…'
  try {
    const data = await callHost('upload_to_google', {})
    const uploaded = (data as { uploaded?: boolean })?.uploaded
    if (uploaded) {
      const count = (data as { summaryCount?: number })?.summaryCount ?? 0
      uploadCloudMessage.value = `已上傳今日彙總至日總表、拍貼機_4格窗核銷表（${count} 筆）`
    } else {
      uploadCloudMessage.value = '上傳失敗或無資料'
    }
  } catch (e) {
    uploadCloudMessage.value = '上傳失敗：' + (e instanceof Error ? e.message : String(e))
  }
  setTimeout(() => { uploadCloudMessage.value = '' }, 5000)
}

async function onClearDb() {
  dbClearMessage.value = '清理中…'
  try {
    const data = await callHost('clear_photo_detail_db', {})
    const detailDeleted = (data as { detailDeleted?: number })?.detailDeleted ?? 0
    const printRecordDeleted = (data as { printRecordDeleted?: number })?.printRecordDeleted ?? 0
    const total = detailDeleted + printRecordDeleted
    const path = (data as { dbPath?: string })?.dbPath ?? ''
    const pathShort = path ? path.replace(/^.*[/\\]report[/\\]/, 'report/') : ''
    dbClearMessage.value = pathShort
      ? `已清理資料庫（${pathShort}）：細表 ${detailDeleted} 筆、列印紀錄 ${printRecordDeleted} 筆，共 ${total} 筆`
      : `已清理資料庫：細表 ${detailDeleted} 筆、列印紀錄 ${printRecordDeleted} 筆，共 ${total} 筆`
  } catch (e) {
    dbClearMessage.value = '清理失敗：' + (e instanceof Error ? e.message : String(e))
  }
  setTimeout(() => { dbClearMessage.value = '' }, 8000)
}

function onClearTestData() {
  resetSession()
  showScreen('idle')
  clearTestMessage.value = '已清除測試資料（已重置流程狀態）'
  setTimeout(() => { clearTestMessage.value = '' }, 4000)
}

function goTo(id: ScreenName) {
  if (id === 'shoot' && !selectedTemplate.value && templates.value.length > 0) {
    selectTemplate(templates.value[getDefaultTemplateIndex()] ?? null)
  }
  // 測試相關畫面：從測試面板進入時一律標記為測試流程（含 template，選版型後進拍照也會維持測試）
  if (id === 'template' || id === 'shoot' || id === 'camera-test' || id === 'test-filter' || id === 'result' || id === 'result-no-qr' || id === 'uploading' || id === 'processing') {
    setTestSession(true)
  }
  // #region agent log
  if (id === 'template') {
    fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Debug-Session-Id': 'b8574e' }, body: JSON.stringify({ sessionId: 'b8574e', location: 'TestPanel.vue:goTo', message: 'goTo_template_after_setTestSession', data: { id, isTestSession: isTestSession.value }, timestamp: Date.now(), hypothesisId: 'H1' }) }).catch(() => {})
  }
  // #endregion
  showScreen(id)
}

async function goToShootWithPreview() {
  if (templates.value.length > 0) {
    selectTemplate(templates.value[getDefaultTemplateIndex()] ?? null)
  }
  await setCaptureResultsFromTestImages()
  showScreen('shoot')
}

function onQuickPrint() {
  if (templates.value.length > 0) {
    selectTemplate(templates.value[getDefaultTemplateIndex()] ?? null)
    // 「直接拍照並列印」視為測試流程
    setTestSession(true)
    showScreen('shoot')
  }
}

function onKeydown(e: KeyboardEvent) {
  if (e.code === 'Escape' || e.key === 'Escape') {
    if (!hidden.value) {
      e.preventDefault()
      e.stopPropagation()
      hidden.value = true
    }
    return
  }
  // 空白鍵：支援 code 與 key（WebView2 等環境可能只設其中一個）
  const isSpace = e.code === 'Space' || e.key === ' '
  if (!isSpace) return
  const now = Date.now()
  if (now - lastSpaceAt <= DOUBLE_TAP_MS) {
    e.preventDefault()
    e.stopPropagation()
    hidden.value = !hidden.value
    lastSpaceAt = 0
    return
  }
  lastSpaceAt = now
}

function togglePanel() {
  if (!allowDevPanel.value) return
  hidden.value = !hidden.value
}

function onToggleEvent() {
  togglePanel()
}

onMounted(() => {
  // 直接讀 env，避免 computed 時機導致 listener 未註冊
  const v = import.meta.env.VITE_ALLOW_DEV_PANEL
  const allow = v === '1' || String(v).toLowerCase() === 'true'
  if (allow) {
    window.addEventListener('keydown', onKeydown, true)
    window.addEventListener('photobooth-toggle-test-panel', onToggleEvent as EventListener)
  }
})

onUnmounted(() => {
  window.removeEventListener('keydown', onKeydown, true)
  window.removeEventListener('photobooth-toggle-test-panel', onToggleEvent as EventListener)
})
</script>

<template>
  <div
    v-if="allowDevPanel"
    class="test-panel"
    :class="{ 'test-panel--hidden': hidden }"
    role="dialog"
    aria-label="測試面板"
  >
    <div class="test-panel__buttons">
      <button type="button" class="btn primary" @click="goTo('idle')">
        測試：回待機畫面
      </button>
      <button type="button" class="btn primary" @click="goTo('template')">
        測試：選版型畫面
      </button>
      <button type="button" class="btn primary" @click="goTo('db-view')">
        觀看資料庫
      </button>
      <button type="button" class="btn primary" @click="goTo('camera-test')">
        測試：相機測試頁（對焦／拍照）
      </button>
      <button type="button" class="btn primary" @click="goTo('test-filter')">
        測試濾鏡
      </button>
      <button type="button" class="btn primary" @click="goTo('shoot')">
        測試：拍照畫面
      </button>
      <button type="button" class="btn primary" @click="goToShootWithPreview">
        測試：拍照預覽畫面（測試圖）
      </button>
      <button
        type="button"
        class="btn primary"
        @click="() => { setResultMock(); goTo('result'); }"
      >
        測試：結果畫面（QR）
      </button>
      <button
        type="button"
        class="btn primary"
        @click="() => { setResultMock(); goTo('result-no-qr'); }"
      >
        測試：結果畫面（無 QR）
      </button>
      <button type="button" class="btn primary" @click="goTo('uploading')">
        測試：照片上傳中畫面
      </button>
      <button type="button" class="btn primary" @click="goTo('processing')">
        測試：列印中畫面
      </button>
      <button
        type="button"
        class="btn primary btn-quick-print"
        @click="onQuickPrint"
      >
        直接拍照並列印
      </button>
      <button
        type="button"
        class="btn primary btn-upload-cloud"
        @click="onUploadCloud"
      >
        上傳雲端
      </button>
      <button
        type="button"
        class="btn primary btn-clear-db"
        @click="onClearDb"
      >
        清理資料庫
      </button>
      <button
        type="button"
        class="btn primary btn-clear-test"
        @click="onClearTestData"
      >
        清除測試資料
      </button>
      <p v-if="uploadCloudMessage" class="test-panel__message">{{ uploadCloudMessage }}</p>
      <p v-if="dbClearMessage" class="test-panel__message">{{ dbClearMessage }}</p>
      <p v-if="clearTestMessage" class="test-panel__message">{{ clearTestMessage }}</p>
    </div>
  </div>
</template>

<style lang="scss" scoped>
@use '@/styles/mixins' as *;

.test-panel {
  position: fixed;
  inset: 0;
  z-index: 10000;
  @include flex-center;
  background: rgba(0, 0, 0, 0.6);

  &.test-panel--hidden {
    display: none !important;
  }
}

.test-panel__buttons {
  @include flex-center;
  flex-direction: column;
  gap: 16px;
  min-height: 100vh;
  padding: 40px;
  position: relative;
  z-index: 10;

  .btn {
    min-width: 250px;
    font-size: 20px;
  }

  .btn-quick-print {
    background-color: #28a745;
    font-size: 18px;
    padding: 15px 30px;
    margin-top: 10px;
  }

  .btn-upload-cloud {
    background-color: #17a2b8;
    font-size: 18px;
    padding: 15px 30px;
  }

  .btn-clear-db {
    background-color: #dc3545;
    font-size: 18px;
    padding: 15px 30px;
  }

  .btn-clear-test {
    background-color: #fd7e14;
    font-size: 18px;
    padding: 15px 30px;
  }

  .test-panel__message {
    margin-top: 12px;
    color: #fff;
    font-size: 16px;
    max-width: 360px;
    text-align: center;
  }
}
</style>
