<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { callHost } from '@/composables/useHost'
import { useLiveView } from '@/composables/useLiveView'

const emit = defineEmits<{ close: [] }>()

const { hostLiveViewDataUrl } = useLiveView()
const isRefocusing = ref(false)
const isCapturing = ref(false)
const captureError = ref<string | null>(null)

/** EVF DriveLens 軟體計步（Near1/Far1），與 C# CanonEdsdkCameraService 同步 */
const driveStep = ref(0)
const driveMaxNear = ref(10)
const far3Repeat = ref(24)
const isDriveBusy = ref(false)

/** 已拍好的照片 URL（photoUrl 或 dataUrl），供後續編輯使用 */
const capturedPhotos = ref<string[]>([])

/** 可見 LOG，供 WebView2 內除錯（無 DevTools 時） */
const logLines = ref<string[]>([])
const MAX_LOG = 50

function log(msg: string) {
  const line = `[${new Date().toLocaleTimeString('zh-TW', { hour12: false })}] ${msg}`
  console.log(line)
  logLines.value = [...logLines.value.slice(-MAX_LOG + 1), line]
}

function hasWebView() {
  const win = typeof window !== 'undefined' ? window : null
  return !!(win && (win as unknown as { chrome?: { webview?: unknown } }).chrome?.webview)
}

async function refreshDriveState() {
  if (!hasWebView()) return
  try {
    const res = (await callHost('get_evf_drive_focus_state', {})) as {
      step?: number
      maxNearSteps?: number
    }
    if (typeof res.step === 'number') driveStep.value = res.step
    if (typeof res.maxNearSteps === 'number') driveMaxNear.value = res.maxNearSteps
  } catch {
    /* ignore */
  }
}

async function onSetMaxNearSteps() {
  if (!hasWebView()) return
  isDriveBusy.value = true
  try {
    await callHost('set_evf_drive_focus_max_steps', { maxNearSteps: driveMaxNear.value })
    await refreshDriveState()
    log(`已設定 maxNearSteps=${driveMaxNear.value}`)
  } catch (e) {
    log(`set maxNearSteps 錯誤: ${e}`)
  } finally {
    isDriveBusy.value = false
  }
}

async function onCalibrateDriveFar() {
  if (!hasWebView()) return
  isDriveBusy.value = true
  log('歸零：連送 Far3…')
  try {
    const res = (await callHost('calibrate_evf_drive_focus_far', {
      far3RepeatCount: far3Repeat.value,
      maxNearSteps: driveMaxNear.value,
    })) as { step?: number; far3RepeatCount?: number }
    await refreshDriveState()
    log(`歸零完成 step=${res.step} far3=${res.far3RepeatCount ?? far3Repeat.value}`)
  } catch (e) {
    log(`歸零錯誤: ${e}`)
  } finally {
    isDriveBusy.value = false
  }
}

async function onDriveNear1() {
  if (!hasWebView()) return
  isDriveBusy.value = true
  try {
    const res = (await callHost('drive_evf_focus_near1', {})) as { ok?: boolean; step?: number }
    await refreshDriveState()
    log(`Near1 ok=${res.ok} step=${res.step}`)
  } catch (e) {
    log(`Near1 錯誤: ${e}`)
  } finally {
    isDriveBusy.value = false
  }
}

async function onDriveFar1() {
  if (!hasWebView()) return
  isDriveBusy.value = true
  try {
    const res = (await callHost('drive_evf_focus_far1', {})) as { ok?: boolean; step?: number }
    await refreshDriveState()
    log(`Far1 ok=${res.ok} step=${res.step}`)
  } catch (e) {
    log(`Far1 錯誤: ${e}`)
  } finally {
    isDriveBusy.value = false
  }
}

async function onRefocus() {
  if (!hasWebView()) {
    captureError.value = '請在拍貼機程式內使用'
    log('onRefocus: 非 WebView 環境')
    return
  }
  captureError.value = null
  isRefocusing.value = true
  log('onRefocus: 開始')
  try {
    await callHost('trigger_evf_af_with_pause', { waitAfterMs: 500 })
    log('onRefocus: 完成')
  } catch (e) {
    const err = e instanceof Error ? e.message : String(e)
    captureError.value = err
    log(`onRefocus 錯誤: ${err}`)
  } finally {
    isRefocusing.value = false
  }
}

async function onCapture() {
  if (!hasWebView()) {
    captureError.value = '請在拍貼機程式內使用'
    log('onCapture: 非 WebView 環境')
    return
  }
  captureError.value = null
  isCapturing.value = true
  const index = capturedPhotos.value.length
  log(`onCapture: 開始 index=${index}`)
  try {
    log(`onCapture: 呼叫 take_one_shot_edsdk…`)
    const forceWithoutAf = (() => {
      const v = import.meta.env.VITE_FORCE_CAPTURE_WITHOUT_AF
      return v === '1' || String(v).toLowerCase() === 'true'
    })()
    const res = (await callHost('take_one_shot_edsdk', {
      index,
      noRetry: true,
      dataUrlMaxSize: 1200,
      forceWithoutAf,
    })) as { photoUrl?: string; dataUrl?: string; thumbUrl?: string; filePath?: string }
    log(`onCapture: 回應 photoUrl=${!!res?.photoUrl} dataUrl=${!!res?.dataUrl} thumbUrl=${!!res?.thumbUrl} filePath=${res?.filePath ?? '(空)'}`)
    const url = res?.thumbUrl ?? res?.dataUrl ?? ''
    if (url) {
      capturedPhotos.value = [...capturedPhotos.value, url]
      log(`onCapture: 已加入照片 共${capturedPhotos.value.length}張`)
    } else {
      captureError.value = '未取得照片 URL'
      log(`onCapture: 無 URL res=${JSON.stringify(res).slice(0, 100)}`)
    }
  } catch (e) {
    const err = e instanceof Error ? e.message : String(e)
    captureError.value = err
    log(`onCapture 錯誤: ${err}`)
  } finally {
    isCapturing.value = false
    log('onCapture: 結束')
  }
}

function onClose() {
  emit('close')
}

onMounted(async () => {
  log('CameraTestPage mounted')
  if (hasWebView()) {
    log('start: clear_captures')
    await callHost('clear_captures', {}).catch((e) => log(`clear_captures err: ${e}`))
    log('start: start_liveview')
    await callHost('start_liveview', {}).catch((e) => log(`start_liveview err: ${e}`))
    await refreshDriveState()
    log('start: 完成')
  } else {
    log('非 WebView 環境，跳過相機初始化')
  }
})

onUnmounted(() => {
  if (hasWebView()) {
    callHost('stop_liveview', {}).catch(() => {})
  }
})
</script>

<template>
  <div class="camera-test-page" role="region" aria-label="相機測試頁">
    <header class="camera-test-page__header">
      <h1>相機測試</h1>
      <button type="button" class="btn-close" @click="onClose" aria-label="關閉">×</button>
    </header>

    <div class="camera-test-page__body">
      <!-- Live View 預覽 -->
      <section class="camera-test-page__preview">
        <h2>即時預覽</h2>
        <div class="preview-box">
          <img
            v-if="hostLiveViewDataUrl"
            :src="hostLiveViewDataUrl"
            alt="Live View"
            class="preview-img"
          />
          <div v-else class="preview-placeholder">等待鏡頭…</div>
        </div>
      </section>

      <!-- 操作按鈕 -->
      <section class="camera-test-page__actions">
        <h2>操作</h2>
        <div class="action-buttons">
          <button
            type="button"
            class="btn btn-refocus"
            :disabled="isRefocusing || isCapturing"
            @click="onRefocus"
          >
            {{ isRefocusing ? '對焦中…' : '重新對焦' }}
          </button>
          <button
            type="button"
            class="btn btn-capture"
            :disabled="isRefocusing || isCapturing"
            @click="onCapture"
          >
            {{ isCapturing ? '拍照中…' : '立刻拍照' }}
          </button>
        </div>
        <p v-if="captureError" class="error-msg">{{ captureError }}</p>
      </section>

      <!-- EVF 手動驅動對焦（DriveLensEvf + 軟體計步） -->
      <section class="camera-test-page__drive" aria-label="EVF 對焦驅動">
        <h2>手動驅動對焦（計步上限）</h2>
        <p class="drive-hint">
          需先開啟即時預覽。先「歸零（Far3）」再 Near1 微調；步數僅程式記錄，手轉對焦環會失步。
        </p>
        <div class="drive-row">
          <span class="drive-label">步數 {{ driveStep }} / 上限 {{ driveMaxNear }}</span>
        </div>
        <div class="drive-row drive-row--inputs">
          <label class="drive-field">
            上限步數
            <input v-model.number="driveMaxNear" type="number" min="0" max="500" class="drive-input" />
          </label>
          <label class="drive-field">
            Far3 次數（歸零）
            <input v-model.number="far3Repeat" type="number" min="1" max="80" class="drive-input" />
          </label>
          <button
            type="button"
            class="btn btn-drive btn-drive--secondary"
            :disabled="isDriveBusy || isCapturing"
            @click="onSetMaxNearSteps"
          >
            套用上限
          </button>
        </div>
        <div class="action-buttons drive-actions">
          <button
            type="button"
            class="btn btn-drive"
            :disabled="isDriveBusy || isCapturing"
            @click="onCalibrateDriveFar"
          >
            {{ isDriveBusy ? '處理中…' : '歸零（Far3×N）' }}
          </button>
          <button
            type="button"
            class="btn btn-drive"
            :disabled="isDriveBusy || isCapturing"
            @click="onDriveNear1"
          >
            近一步（Near1）
          </button>
          <button
            type="button"
            class="btn btn-drive"
            :disabled="isDriveBusy || isCapturing"
            @click="onDriveFar1"
          >
            遠一步（Far1）
          </button>
          <button
            type="button"
            class="btn btn-drive btn-drive--ghost"
            :disabled="isDriveBusy"
            @click="refreshDriveState"
          >
            重新讀取狀態
          </button>
        </div>
      </section>

      <!-- LOG 面板 -->
      <section class="camera-test-page__log">
        <h2>LOG（除錯用）</h2>
        <div class="log-box">
          <div
            v-for="(line, i) in logLines"
            :key="i"
            class="log-line"
          >{{ line }}</div>
        </div>
      </section>

      <!-- 已拍照片（暫存供編輯） -->
      <section class="camera-test-page__photos">
        <h2>已拍照片（共 {{ capturedPhotos.length }} 張）</h2>
        <div class="photo-grid">
          <div
            v-for="(url, i) in capturedPhotos"
            :key="i"
            class="photo-item"
          >
            <img :src="url" :alt="`照片 ${i + 1}`" class="photo-img" />
            <span class="photo-index">{{ i + 1 }}</span>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>

<style lang="scss" scoped>
.camera-test-page {
  position: fixed;
  inset: 0;
  z-index: 9999;
  background: #1a1a2e;
  color: #eee;
  display: flex;
  flex-direction: column;
  overflow: auto;
}

.camera-test-page__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 24px;
  background: #16213e;
  border-bottom: 1px solid #333;

  h1 {
    margin: 0;
    font-size: 1.5rem;
  }
}

.btn-close {
  width: 40px;
  height: 40px;
  font-size: 28px;
  line-height: 1;
  border: none;
  background: transparent;
  color: #aaa;
  cursor: pointer;
  border-radius: 8px;
  padding: 0;

  &:hover {
    color: #fff;
    background: rgba(255, 255, 255, 0.1);
  }
}

.camera-test-page__body {
  flex: 1;
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.camera-test-page__preview,
.camera-test-page__actions,
.camera-test-page__drive,
.camera-test-page__log,
.camera-test-page__photos {
  h2 {
    margin: 0 0 12px 0;
    font-size: 1rem;
    color: #ccc;
  }
}

.preview-box {
  width: 100%;
  max-width: 640px;
  aspect-ratio: 16/10;
  background: #0f0f1a;
  border-radius: 8px;
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
}

.preview-img {
  width: 100%;
  height: 100%;
  object-fit: contain;
  transform: scaleX(-1); /* 即時預覽鏡像，與拍照畫面一致 */
}

.preview-placeholder {
  color: #666;
  font-size: 1.2rem;
}

.action-buttons {
  display: flex;
  gap: 16px;
  flex-wrap: wrap;
}

.btn {
  padding: 14px 28px;
  font-size: 1.1rem;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  transition: opacity 0.2s;

  &:disabled {
    opacity: 0.5;
    cursor: not-allowed;
  }
}

.btn-refocus {
  background: #4361ee;
  color: #fff;

  &:not(:disabled):hover {
    background: #5a75f5;
  }
}

.btn-capture {
  background: #06d6a0;
  color: #0d1b2a;

  &:not(:disabled):hover {
    background: #20e9b5;
  }
}

.camera-test-page__drive {
  padding: 16px;
  background: #12122a;
  border-radius: 8px;
  border: 1px solid #2a2a44;
}

.drive-hint {
  margin: 0 0 12px 0;
  font-size: 0.9rem;
  color: #9aa;
  line-height: 1.45;
}

.drive-row {
  margin-bottom: 12px;
}

.drive-row--inputs {
  display: flex;
  flex-wrap: wrap;
  gap: 12px 20px;
  align-items: flex-end;
}

.drive-label {
  font-size: 1rem;
  color: #b8c5c5;
}

.drive-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 0.85rem;
  color: #aaa;
}

.drive-input {
  width: 100px;
  padding: 8px 10px;
  border-radius: 6px;
  border: 1px solid #444;
  background: #0f0f1a;
  color: #eee;
}

.drive-actions {
  margin-top: 8px;
}

.btn-drive {
  background: #7b2cbf;
  color: #fff;

  &:not(:disabled):hover {
    background: #9d4edd;
  }
}

.btn-drive--secondary {
  background: #495057;
  &:not(:disabled):hover {
    background: #6c757d;
  }
}

.btn-drive--ghost {
  background: transparent;
  border: 1px solid #555;
  color: #ccc;
  &:not(:disabled):hover {
    background: rgba(255, 255, 255, 0.06);
  }
}

.error-msg {
  margin: 12px 0 0 0;
  color: #ef476f;
  font-size: 0.95rem;
}

.log-box {
  max-height: 180px;
  overflow-y: auto;
  background: #0a0a14;
  border-radius: 8px;
  padding: 12px;
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 0.8rem;
  line-height: 1.5;
  color: #8f8;
}

.log-line {
  word-break: break-all;
}

.photo-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
}

.photo-item {
  position: relative;
  width: 160px;
  height: 120px;
  border-radius: 8px;
  overflow: hidden;
  background: #0f0f1a;
  border: 2px solid #333;
}

.photo-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.photo-index {
  position: absolute;
  bottom: 4px;
  left: 8px;
  background: rgba(0, 0, 0, 0.7);
  color: #fff;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 0.85rem;
}
</style>
