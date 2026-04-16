<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { usePhotobooth } from '@/composables/usePhotobooth'
import { callHost } from '@/composables/useHost'

const REQUIRED_TAPS = 15
const TAP_RESET_MS = 1500
const LONG_PRESS_MS = 5000
const PASSWORD = '9347'

const tapCount = ref(0)
const lastTapAt = ref(0)
let longPressTimer: ReturnType<typeof setTimeout> | null = null

const open = ref(false)
const showKeypad = ref(true)
const showMenu = ref(false)
const input = ref('')
const error = ref('')
const uploadMessage = ref('')

const { showScreen, resetSession } = usePhotobooth()

/** 是否啟用紙鈔機或投幣器；皆關閉時用長按，否則用連按 */
const isPaymentsEnabled = computed(() => {
  const bill = import.meta.env.VITE_BILL_ACCEPTOR_ENABLED
  const coin = import.meta.env.VITE_COIN_ACCEPTOR_ENABLED
  const billOn = bill === '1' || String(bill ?? '').toLowerCase() === 'true'
  const coinOn = coin === '1' || String(coin ?? '').toLowerCase() === 'true'
  return billOn || coinOn
})

function resetTap() {
  tapCount.value = 0
  lastTapAt.value = 0
}

function openSecretMenu() {
  tapCount.value = 0
  open.value = true
  showKeypad.value = true
  showMenu.value = false
  input.value = ''
  error.value = ''
}

function onHotspotClick() {
  if (isPaymentsEnabled.value) {
    // 有啟用收款：連按 15 下
    const now = Date.now()
    if (lastTapAt.value && now - lastTapAt.value > TAP_RESET_MS) {
      tapCount.value = 0
    }
    lastTapAt.value = now
    tapCount.value += 1
    if (tapCount.value >= REQUIRED_TAPS) {
      openSecretMenu()
    }
  }
  // 無啟用收款時由長按處理
}

function onHotspotPointerDown() {
  if (!isPaymentsEnabled.value) {
    longPressTimer = setTimeout(() => {
      longPressTimer = null
      openSecretMenu()
    }, LONG_PRESS_MS)
  }
}

function onHotspotPointerUp() {
  if (longPressTimer) {
    clearTimeout(longPressTimer)
    longPressTimer = null
  }
}

function onHotspotPointerLeave() {
  onHotspotPointerUp()
}

function onHotspotPointerCancel() {
  onHotspotPointerUp()
}

function close() {
  open.value = false
  showKeypad.value = true
  showMenu.value = false
  input.value = ''
  error.value = ''
  uploadMessage.value = ''
  resetTap()
}

function appendDigit(digit: string) {
  if (input.value.length >= 8) return
  input.value += digit
  error.value = ''
}

function backspace() {
  if (!input.value) return
  input.value = input.value.slice(0, -1)
  error.value = ''
}

function clearAll() {
  input.value = ''
  error.value = ''
}

function submitPassword() {
  if (input.value === PASSWORD) {
    showKeypad.value = false
    showMenu.value = true
    input.value = ''
    error.value = ''
    return
  }
  error.value = '密碼錯誤'
  input.value = ''
}

function onWatchDb() {
  close()
  showScreen('db-view')
}

function onClearTestData() {
  resetSession()
  showScreen('idle')
  close()
}

async function onUpload() {
  uploadMessage.value = '上傳中…'
  try {
    const data = await callHost('upload_to_google', {})
    const uploaded = (data as { uploaded?: boolean })?.uploaded
    uploadMessage.value = uploaded ? '上傳完成' : '上傳失敗或無資料'
  } catch (e) {
    uploadMessage.value = '上傳失敗：' + (e instanceof Error ? e.message : String(e))
  }
  setTimeout(() => { uploadMessage.value = '' }, 3000)
}

async function onShutdown() {
  try {
    await callHost('shutdown', {})
  } catch {
    // 關機後不會有回應
  }
}

function onKeydown(e: KeyboardEvent) {
  if (!open.value) return
  if (e.key === 'Escape') {
    e.preventDefault()
    close()
    return
  }
  if (showKeypad.value) {
    if (e.key >= '0' && e.key <= '9') {
      e.preventDefault()
      appendDigit(e.key)
    }
    if (e.key === 'Backspace') {
      e.preventDefault()
      backspace()
    }
    if (e.key === 'Enter') {
      e.preventDefault()
      submitPassword()
    }
  }
}

onMounted(() => {
  window.addEventListener('keydown', onKeydown, true)
})

onUnmounted(() => {
  window.removeEventListener('keydown', onKeydown, true)
  if (longPressTimer) {
    clearTimeout(longPressTimer)
    longPressTimer = null
  }
})
</script>

<template>
  <div
    class="secret-hotspot"
    @click="onHotspotClick"
    @pointerdown="onHotspotPointerDown"
    @pointerup="onHotspotPointerUp"
    @pointerleave="onHotspotPointerLeave"
    @pointercancel="onHotspotPointerCancel"
  />
  <div v-if="open" class="secret-overlay" role="dialog" aria-label="管理登入">
    <!-- 密碼鍵盤 -->
    <div v-if="showKeypad" class="secret-keypad">
      <div class="secret-title">請輸入密碼</div>
      <div class="secret-input" aria-live="polite">
        {{ input ? '•'.repeat(input.length) : '____' }}
      </div>
      <div v-if="error" class="secret-error">{{ error }}</div>
      <div class="secret-grid">
        <button type="button" class="key" @click="appendDigit('1')">1</button>
        <button type="button" class="key" @click="appendDigit('2')">2</button>
        <button type="button" class="key" @click="appendDigit('3')">3</button>
        <button type="button" class="key" @click="appendDigit('4')">4</button>
        <button type="button" class="key" @click="appendDigit('5')">5</button>
        <button type="button" class="key" @click="appendDigit('6')">6</button>
        <button type="button" class="key" @click="appendDigit('7')">7</button>
        <button type="button" class="key" @click="appendDigit('8')">8</button>
        <button type="button" class="key" @click="appendDigit('9')">9</button>
        <button type="button" class="key secondary" @click="clearAll">清除</button>
        <button type="button" class="key" @click="appendDigit('0')">0</button>
        <button type="button" class="key secondary" @click="backspace">退格</button>
      </div>
      <div class="secret-actions">
        <button type="button" class="btn ghost" @click="close">取消</button>
        <button type="button" class="btn primary" @click="submitPassword">確認</button>
      </div>
    </div>
    <!-- 管理選單（密碼正確後） -->
    <div v-else class="secret-menu">
      <div class="secret-title">管理選單</div>
      <button type="button" class="menu-btn" @click="onWatchDb">
        觀看資料庫
      </button>
      <button type="button" class="menu-btn" @click="onClearTestData">
        清除測試資料
      </button>
      <button type="button" class="menu-btn" @click="onUpload">
        上傳資料
      </button>
      <button type="button" class="menu-btn menu-btn--danger" @click="onShutdown">
        關機
      </button>
      <p v-if="uploadMessage" class="secret-msg">{{ uploadMessage }}</p>
      <button type="button" class="menu-btn menu-btn--ghost" @click="close">
        關閉
      </button>
    </div>
  </div>
</template>

<style scoped lang="scss">
.secret-hotspot {
  position: fixed;
  top: 0;
  right: 0;
  width: 90px;
  height: 90px;
  z-index: 2147483647;
  background: transparent;
  opacity: 1;
}

.secret-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.55);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 2147483647;
}

.secret-keypad,
.secret-menu {
  width: 320px;
  background: #111;
  color: #fff;
  border-radius: 12px;
  padding: 20px;
  box-shadow: 0 10px 30px rgba(0, 0, 0, 0.35);
}

.secret-menu {
  width: 280px;
}

.secret-title {
  font-size: 18px;
  text-align: center;
  margin-bottom: 16px;
}

.secret-input {
  text-align: center;
  font-size: 22px;
  letter-spacing: 4px;
  padding: 10px 0 6px;
}

.secret-error {
  text-align: center;
  color: #ff8a8a;
  font-size: 14px;
  margin-bottom: 8px;
}

.secret-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 10px;
  margin-top: 6px;
}

.key {
  height: 48px;
  font-size: 18px;
  border-radius: 8px;
  border: none;
  background: #2b2b2b;
  color: #fff;
  cursor: pointer;
}

.key.secondary {
  background: #3b3b3b;
}

.secret-actions {
  display: flex;
  justify-content: space-between;
  margin-top: 14px;
  gap: 10px;
}

.btn {
  flex: 1;
  height: 40px;
  border-radius: 8px;
  border: none;
  font-size: 16px;
  cursor: pointer;
}

.btn.ghost {
  background: transparent;
  color: #fff;
  border: 1px solid #555;
}

.btn.primary {
  background: #4f8cff;
  color: #fff;
}

.menu-btn {
  display: block;
  width: 100%;
  height: 44px;
  margin-bottom: 10px;
  font-size: 16px;
  border-radius: 8px;
  border: none;
  background: #2b2b2b;
  color: #fff;
  cursor: pointer;
  &:last-of-type {
    margin-bottom: 0;
  }
  &.menu-btn--ghost {
    margin-top: 12px;
    background: transparent;
    border: 1px solid #555;
  }
  &.menu-btn--danger {
    background: #c0392b;
  }
}

.secret-msg {
  margin: 8px 0 0;
  font-size: 13px;
  color: #aaa;
  text-align: center;
}
</style>
