<script setup lang="ts">
defineOptions({ name: 'ScreenResultNoQr' })

import { ref, watch, onBeforeUnmount } from 'vue'
import { usePhotobooth } from '@/composables/usePhotobooth'

const {
  currentScreen,
  finalFilePath,
  resultDisplayUrl,
  selectedTemplate,
  showScreen,
  callHost,
  resetSession,
  autoPrint,
  isTestSession,
} = usePhotobooth()

const copies = ref(1)
const autoGoTimer = ref<ReturnType<typeof setTimeout> | null>(null)

function getResultAutoPrintSec(): number {
  const raw = import.meta.env.VITE_RESULT_AUTO_PRINT_SEC
  if (raw === undefined || raw === '') return 60
  const n = parseInt(raw, 10)
  return Number.isNaN(n) || n < 1 ? 60 : Math.min(300, n)
}

function getPrintingShowSec(): number {
  const raw = import.meta.env.VITE_PRINTING_SHOW_SEC
  if (raw === undefined || raw === '') return 20
  const n = parseInt(raw, 10)
  return Number.isNaN(n) || n < 1 ? 20 : Math.min(120, n)
}

function getSkipPrint(): boolean {
  const v = import.meta.env.VITE_SKIP_PRINT
  return v === '1' || String(v).toLowerCase() === 'true'
}

function getReceiptAmount(): string {
  const v = import.meta.env.VITE_RECEIPT_AMOUNT
  return typeof v === 'string' && v !== '' ? v : '0'
}

function getLogPrintRecordWhenSkip(): boolean {
  const v = import.meta.env.VITE_LOG_PRINT_RECORD_WHEN_SKIP
  return v === '1' || String(v).toLowerCase() === 'true'
}

function getProjectName(): string {
  const v = import.meta.env.VITE_PROJECT_NAME
  return typeof v === 'string' ? v : ''
}

function getMachineName(): string {
  const v = import.meta.env.VITE_MACHINE_NAME
  return typeof v === 'string' ? v : ''
}

function getIsTest(): boolean {
  if (isTestSession.value) return true
  const v = import.meta.env.VITE_TEST_FAST_COUNTDOWN
  return v === '1' || String(v).toLowerCase() === 'true'
}

function getFinalFileName(): string {
  const path = finalFilePath.value
  if (!path) return ''
  return path.replace(/^.*[/\\]/, '') || ''
}

function clearAutoGoTimer() {
  if (autoGoTimer.value != null) {
    clearTimeout(autoGoTimer.value)
    autoGoTimer.value = null
  }
}

function goToPrintingThenIdle() {
  const printingSec = getPrintingShowSec()
  const skipPrint = getSkipPrint()
  showScreen('processing')
  if (!finalFilePath.value) {
    setTimeout(() => { autoPrint.value = false; resetSession(); showScreen('idle') }, printingSec * 1000)
    return
  }
  if (skipPrint) {
    if (getLogPrintRecordWhenSkip()) {
      callHost('log_print_record', {
        templateName: selectedTemplate.value?.id ?? 'unknown',
        printTime: new Date().toISOString(),
        amount: getReceiptAmount(),
        projectName: getProjectName(),
        machineName: getMachineName(),
        copies: 1,
        fileName: getFinalFileName(),
        isTest: getIsTest(),
      }).finally(() => {
        setTimeout(() => { autoPrint.value = false; resetSession(); showScreen('idle') }, printingSec * 1000)
      })
    } else {
      setTimeout(() => { autoPrint.value = false; resetSession(); showScreen('idle') }, printingSec * 1000)
    }
    return
  }
  callHost('print_hotfolder', {
    filePath: finalFilePath.value,
    sizeKey: selectedTemplate.value?.sizeKey ?? '4x6',
    copies: 1,
  })
    .then(() =>
      callHost('log_print_record', {
        templateName: selectedTemplate.value?.id ?? 'unknown',
        printTime: new Date().toISOString(),
        amount: getReceiptAmount(),
        projectName: getProjectName(),
        machineName: getMachineName(),
        copies: copies.value,
        fileName: getFinalFileName(),
        isTest: getIsTest(),
      })
    )
    .finally(() => {
      setTimeout(() => {
        autoPrint.value = false
        resetSession()
        showScreen('idle')
      }, printingSec * 1000)
    })
}

watch(
  [() => currentScreen.value, () => finalFilePath.value],
  ([screen, path]) => {
    clearAutoGoTimer()
    if (screen !== 'result-no-qr' || !path) return
    const sec = getResultAutoPrintSec()
    autoGoTimer.value = setTimeout(() => {
      autoGoTimer.value = null
      goToPrintingThenIdle()
    }, sec * 1000)
  },
  { immediate: true }
)

onBeforeUnmount(clearAutoGoTimer)

function onCancel() {
  clearAutoGoTimer()
  autoPrint.value = false
  resetSession()
  showScreen('idle')
}

async function onPrint() {
  if (!finalFilePath.value) return
  clearAutoGoTimer()
  const skipPrint = getSkipPrint()
  let c = copies.value
  if (Number.isNaN(c)) c = 1
  copies.value = Math.min(5, Math.max(1, c))
  const printingSec = getPrintingShowSec()
  showScreen('processing')
  if (skipPrint) {
    if (getLogPrintRecordWhenSkip()) {
      await callHost('log_print_record', {
        templateName: selectedTemplate.value?.id ?? 'unknown',
        printTime: new Date().toISOString(),
        amount: getReceiptAmount(),
        projectName: getProjectName(),
        machineName: getMachineName(),
        copies: copies.value,
        fileName: getFinalFileName(),
        isTest: getIsTest(),
      })
    }
    autoPrint.value = false
    resetSession()
    setTimeout(() => showScreen('idle'), printingSec * 1000)
    return
  }
  await callHost('print_hotfolder', {
    filePath: finalFilePath.value,
    sizeKey: selectedTemplate.value?.sizeKey ?? '4x6',
    copies: copies.value,
  })
  await callHost('log_print_record', {
    templateName: selectedTemplate.value?.id ?? 'unknown',
    printTime: new Date().toISOString(),
    amount: getReceiptAmount(),
    projectName: getProjectName(),
    machineName: getMachineName(),
    copies: copies.value,
    fileName: getFinalFileName(),
    isTest: getIsTest(),
  })
  autoPrint.value = false
  resetSession()
  setTimeout(() => showScreen('idle'), printingSec * 1000)
}
</script>

<template>
  <div class="screen screen--result-no-qr" role="region" aria-label="結果畫面（無 QR）">
    <div class="result-wrap">
      <div class="result-preview">
        <img id="final-preview" alt="final preview" :src="resultDisplayUrl" />
      </div>
      <div class="btns-row">
        <button type="button" class="btn-action btn-cancel" @click="onCancel">
          <img src="/assets/templates/NoQRcodePage/cancelbutton.png" alt="取消" />
        </button>
        <button type="button" class="btn-action btn-print" @click="onPrint">
          <img src="/assets/templates/NoQRcodePage/printbutton.png" alt="列印" />
        </button>
      </div>
    </div>
  </div>
</template>

<style lang="scss" scoped>
@use '@/styles/variables' as *;

.screen--result-no-qr {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  min-height: 100vh;
  /* 若無 background.png 可從 QRcodePage 複製，或使用背景色 */
  background-image: url('#{$path-templates}/NoQRcodePage/background.png');
  background-repeat: no-repeat;
  background-position: center center;
  background-size: cover;
  background-color: #ffffff;
  padding: $spacing-5xl;
}

.result-wrap {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: $spacing-4xl;
  max-width: 1600px;
  margin: 0 auto;
}

.result-preview {
  display: flex;
  align-items: center;
  justify-content: center;
  min-width: 0;

  img {
    max-width: 100%;
    max-height: calc(100vh - 180px);
    width: auto;
    height: auto;
    object-fit: contain;
    display: block;
  }
}

.btns-row {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: $spacing-3xl;
}

.btn-action {
  padding: 0;
  border: none;
  background: none;
  cursor: pointer;

  img {
    width: auto;
    height: 64px;
    display: block;
  }
}
</style>
