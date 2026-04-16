<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch } from 'vue'
import { usePhotobooth } from '@/composables/usePhotobooth'
import { useLiveView } from '@/composables/useLiveView'
import ScreenIdle from '@/components/photobooth/ScreenIdle.vue'
import ScreenTemplate from '@/components/photobooth/ScreenTemplate.vue'
import ScreenShoot from '@/components/photobooth/shoot/ScreenShoot.vue'
import ScreenResult from '@/components/photobooth/ScreenResult.vue'
import ScreenResultNoQr from '@/components/photobooth/ScreenResultNoQr.vue'
import ScreenUploading from '@/components/photobooth/ScreenUploading.vue'
import ScreenProcessing from '@/components/photobooth/ScreenProcessing.vue'
import TestPanel from '@/components/photobooth/TestPanel.vue'
import ScreenDbViewer from '@/components/photobooth/ScreenDbViewer.vue'
import CameraTestPage from '@/components/photobooth/CameraTestPage.vue'
import TestFilterPage from '@/components/testfillter/TestFilterPage.vue'
import LoadingOverlay from '@/components/photobooth/LoadingOverlay.vue'
import Footer from '@/components/photobooth/Footer.vue'

const photobooth = usePhotobooth()
const { currentScreen, showScreen, runDevStartPage, buildFinalOutput, selectTemplate, templates, callHost } = photobooth
const { setHostLiveViewDataUrl, liveViewFrameCount } = useLiveView()

/** 紙鈔機／投幣器開關（來自 .env） */
const isBillAcceptorEnabled = () => {
  const v = import.meta.env.VITE_BILL_ACCEPTOR_ENABLED
  return v === '1' || String(v ?? '').toLowerCase() === 'true'
}
const isCoinAcceptorEnabled = () => {
  const v = import.meta.env.VITE_COIN_ACCEPTOR_ENABLED
  return v === '1' || String(v ?? '').toLowerCase() === 'true'
}
/** 是否啟用任一收款方式；皆關閉時可點擊螢幕進入選版型 */
const isPaymentsEnabled = () => isBillAcceptorEnabled() || isCoinAcceptorEnabled()

/** 已付金額累積：紙鈔只收 100 元；投幣器被動、可累積超過 100（例如 200），滿 100 扣一次進選版型，回到待機後餘額若仍 >= 100 再進選版型一次 */
const paidAccumulated = ref(0)

/** 每天早上 10:30 排程上傳：若當下非待機則設此旗標，進入待機時再執行上傳，上傳期間會暫停收錢 */
const pendingScheduledUpload = ref(false)
/** 當日是否已觸發過 10:30（避免同一分鐘內重複設 pending） */
const scheduledUploadTriggeredDate = ref('')

const SCHEDULED_UPLOAD_HOUR = 10
const SCHEDULED_UPLOAD_MINUTE = 30

function postPaymentsEnabled(enabled: boolean) {
  const win = window as unknown as { chrome?: { webview?: { postMessage: (s: string) => void } } }
  if (win.chrome?.webview?.postMessage) {
    win.chrome.webview.postMessage(JSON.stringify({ '@event': 'set_payments_enabled', enabled }))
  }
}

async function runScheduledUploadWhenIdle() {
  if (currentScreen.value !== 'idle' || !pendingScheduledUpload.value) return
  pendingScheduledUpload.value = false
  postPaymentsEnabled(false)
  try {
    await callHost('upload_to_google', {})
  } finally {
    postPaymentsEnabled(true)
  }
}

function tryGoToTemplateIfPaid() {
  if (currentScreen.value !== 'idle' || paidAccumulated.value < 100) return
  paidAccumulated.value -= 100
  setTimeout(() => showScreen('template'), 1000)
}

watch(currentScreen, (screen) => {
  if (screen === 'idle') {
    const win = window as unknown as { chrome?: { webview?: unknown } }
    if (win.chrome?.webview) callHost('stop_liveview', {}).catch(() => {})
    if (paidAccumulated.value >= 100) tryGoToTemplateIfPaid()
    runScheduledUploadWhenIdle()
  }
})

let scheduledUploadCheckTimer: ReturnType<typeof setInterval> | null = null
function checkScheduledUploadTime() {
  const now = new Date()
  const today = now.getFullYear() + '-' + String(now.getMonth() + 1).padStart(2, '0') + '-' + String(now.getDate()).padStart(2, '0')
  if (now.getHours() !== SCHEDULED_UPLOAD_HOUR || now.getMinutes() !== SCHEDULED_UPLOAD_MINUTE) return
  if (scheduledUploadTriggeredDate.value === today) return
  scheduledUploadTriggeredDate.value = today
  pendingScheduledUpload.value = true
  runScheduledUploadWhenIdle()
}

onMounted(() => {
  // 鎖住右鍵選單
  document.addEventListener('contextmenu', (e) => {
    e.preventDefault()
    return false
  })
  document.addEventListener('dragstart', (e) => {
    e.preventDefault()
    return false
  })
  // 禁止 Ctrl + 滾輪／Ctrl + +/- 放大縮小網頁
  document.addEventListener('wheel', (e) => {
    if (e.ctrlKey) {
      e.preventDefault()
      return false
    }
  }, { passive: false })
  // 禁止觸控雙指放大縮小（需 passive: false 才能 preventDefault）
  document.addEventListener('touchmove', (e) => {
    if (e.touches.length >= 2) {
      e.preventDefault()
    }
  }, { passive: false })
  document.addEventListener('keydown', (e) => {
    // if (e.key === 'F12') {
    //   e.preventDefault()
    //   return false
    // }
    if (e.ctrlKey && (e.key === '+' || e.key === '=' || e.key === '-')) {
      e.preventDefault()
      return false
    }
    if (e.ctrlKey && e.shiftKey && e.key === 'I') {
      e.preventDefault()
      return false
    }
    if (e.ctrlKey && e.shiftKey && e.key === 'J') {
      e.preventDefault()
      return false
    }
    if (e.ctrlKey && e.key === 'U') {
      e.preventDefault()
      return false
    }
    if (e.ctrlKey && e.key === 'S') {
      e.preventDefault()
      return false
    }
  })

  const win = window as unknown as {
    chrome?: {
      webview?: {
        addEventListener: (type: string, handler: (ev: { data: string }) => void) => void
      }
    }
  }
  if (win.chrome?.webview) {
    win.chrome.webview.addEventListener('message', (ev: { data: string }) => {
      try {
        const msg = JSON.parse(ev.data) as {
          '@event'?: string
          event?: string
          amount?: number
          dataUrl?: string
          templateId?: string
        }
        const eventType = msg['@event'] ?? msg.event
        if (eventType === 'wpf_shoot_done' && typeof msg.templateId === 'string') {
          const templateId = msg.templateId
          const tpl = templates.value.find((t: { id: string }) => t.id === templateId) ?? templates.value[0]
          if (tpl) selectTemplate(tpl)
          showScreen('uploading')
          buildFinalOutput()
          return
        }
        if (eventType === 'liveview_frame' && typeof msg.dataUrl === 'string') {
          setHostLiveViewDataUrl(msg.dataUrl)
          if (liveViewFrameCount.value === 1) {
            console.log('[Live View] 第一幀已收到')
          } else if (liveViewFrameCount.value % 100 === 0) {
            console.log(`[Live View] 已收到 ${liveViewFrameCount.value} 幀`)
          }
          return
        }
        const amount = typeof msg.amount === 'number' ? msg.amount : 0
        if (eventType !== 'paid' || amount <= 0) return
        paidAccumulated.value += amount
        if (currentScreen.value === 'idle' && paidAccumulated.value >= 100) {
          tryGoToTemplateIfPaid()
        }
      } catch {
        // ignore
      }
    })
  }

  runDevStartPage()

  // 通知 C# 紙鈔機／投幣器開關，讓 C# 依此決定是否啟動
  const webview = win.chrome?.webview as { postMessage?: (s: string) => void } | undefined
  if (webview?.postMessage) {
    webview.postMessage(JSON.stringify({
      '@event': 'payments_config',
      billAcceptorEnabled: isBillAcceptorEnabled(),
      coinAcceptorEnabled: isCoinAcceptorEnabled(),
    }))
  }

  scheduledUploadCheckTimer = setInterval(checkScheduledUploadTime, 60 * 1000)
  checkScheduledUploadTime()
})

onUnmounted(() => {
  if (scheduledUploadCheckTimer != null) {
    clearInterval(scheduledUploadCheckTimer)
    scheduledUploadCheckTimer = null
  }
})
</script>

<template>
  <div id="app" class="photobooth-app">
    <ScreenIdle
      :class="{ active: currentScreen === 'idle' }"
      :is-payments-enabled="isPaymentsEnabled()"
      @click-to-start="showScreen('template')"
    />
    <CameraTestPage
      v-if="currentScreen === 'camera-test'"
      @close="showScreen('idle')"
    />
    <TestFilterPage
      v-if="currentScreen === 'test-filter'"
      :class="{ active: currentScreen === 'test-filter' }"
      @close="showScreen('idle')"
    />
    <ScreenDbViewer
      v-if="currentScreen === 'db-view'"
      class="active"
      @close="showScreen('idle')"
    />
    <TestPanel />
    <ScreenTemplate :class="{ active: currentScreen === 'template' }" />
    <ScreenShoot
      :class="{ active: currentScreen === 'shoot' }"
      :is-active="currentScreen === 'shoot'"
    />
    <ScreenResult :class="{ active: currentScreen === 'result' }" />
    <ScreenResultNoQr :class="{ active: currentScreen === 'result-no-qr' }" />
    <ScreenUploading :class="{ active: currentScreen === 'uploading' }" />
    <ScreenProcessing :class="{ active: currentScreen === 'processing' }" />
    <LoadingOverlay />
    <Footer />
  </div>
</template>

<style>
/* 全域由 main.scss 提供 */
</style>
