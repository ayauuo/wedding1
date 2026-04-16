<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted, nextTick } from 'vue'
import { usePhotobooth } from '@/composables/usePhotobooth'
import { useTakePicture, isCountdownMinus5Mode } from '@/composables/useTakePicture'
import { useLiveView } from '@/composables/useLiveView'
import FilterOptions from './FilterOptions.vue'

const props = defineProps<{ isActive?: boolean }>()
const {
  selectedTemplate,
  templates,
  selectTemplate,
  showScreen,
  setCaptureResults,
  setCaptureVideoBlob,
  captureResults,
  selectedFilter,
  getFilterCssForCanvas,
  getColorBalanceForFilter,
  applyColorBalance,
  buildFinalOutput,
  callHost,
  setTestSession,
  // 貼圖相關狀態（依格分開）
  stickersBySlot,
  addSticker,
  updateSticker,
  removeSticker,
  getSynthesisSlots,
} = usePhotobooth()

/** 目前編輯的格索引（0-based），貼圖只作用在這一格 */
const currentSlotIndex = computed(() => tp.currentMainIndex.value - 1)
/** 目前這一格的貼圖列表 */
const stickersForCurrentSlot = computed(
  () => stickersBySlot.value[currentSlotIndex.value] ?? []
)
const { hostLiveViewDataUrl, liveViewFrameCount, clearHostLiveViewDataUrl } = useLiveView()
const tp = useTakePicture(() => selectedTemplate.value ?? null)

/** 是否顯示濾鏡區（拍完照按「下一步」後顯示，同一頁內完成預覽→濾鏡→確認） */
const showFilterOptions = ref(false)

/** 濾鏡／貼圖頁 1 分鐘倒數（秒），時間到自動進入下一步 */
const FILTER_COUNTDOWN_SECONDS = 60
const filterCountdownSeconds = ref(FILTER_COUNTDOWN_SECONDS)
let filterCountdownTimerId: ReturnType<typeof setInterval> | null = null

function startFilterCountdown() {
  filterCountdownSeconds.value = FILTER_COUNTDOWN_SECONDS
  filterCountdownTimerId = setInterval(() => {
    filterCountdownSeconds.value--
    if (filterCountdownSeconds.value <= 0) {
      stopFilterCountdown()
      onFilterConfirm()
    }
  }, 1000)
}

function stopFilterCountdown() {
  if (filterCountdownTimerId) {
    clearInterval(filterCountdownTimerId)
    filterCountdownTimerId = null
  }
}

const shootRootRef = ref<HTMLElement | null>(null)
const pictureAreaRef = ref<HTMLElement | null>(null)
const leftPanelRef = ref<HTMLElement | null>(null)
/** 左側面板 left：拍照外框左緣往左 80px，再扣掉左側縮圖區寬度（動態依外框與縮圖大小計算） */
const leftPanelLeftPx = ref(70)
const LEFT_PANEL_OFFSET = 80

/** 只執行一次：左側面板 left 計算（拍照外框左緣往左 80px 再扣掉縮圖區寬度） */
function doUpdateLeftPanelLeft() {
  const root = shootRootRef.value
  const area = pictureAreaRef.value
  const panel = leftPanelRef.value
  if (!root || !area) return
  const rootRect = root.getBoundingClientRect()
  const areaRect = area.getBoundingClientRect()
  const panelWidth = panel ? panel.getBoundingClientRect().width : 0
  leftPanelLeftPx.value = Math.round(areaRect.left - rootRect.left - LEFT_PANEL_OFFSET - panelWidth)
}

/** 是否已做過第一次 left 計算（之後不再更新，版面已定型） */
const hasInitialLeft = ref(false)
/** 左側面板是否已定位完成（定位前隱藏，避免使用者看到跑位） */
const shootLayoutReady = ref(false)

/** 左側面板在 absolute 模式時的 style（left 依拍照外框動態計算） */
const leftPanelStyle = computed(() =>
  !showFilterOptions.value ? { left: `${leftPanelLeftPx.value}px` } : {}
)

const thumbUrls = ref<string[]>([])
/** 連拍中目前要拍的是第幾格（0-based），-1 表示非連拍中；左側該格顯示即時預覽 */
const currentShootingIndex = ref(-1)
const isReshooting = ref(false)
/**
 * 連拍過程右側顯示：tex1.png / tex2.png ...（拍完後不再顯示）
 * 檔案放在 public/assets/templates/ShootPage/texN.png
 */
const shootTexUrl = computed(() => {
  if (!tp.isBurstShooting.value) return ''
  const i = currentShootingIndex.value
  if (i < 0) return ''
  const n = i + 1
  if (n < 1 || n > shotCount.value) return ''
  return `/assets/templates/ShootPage/tex${n}.png`
})
/** 非 WebView 時顯示提示：僅在拍貼機程式內使用 EDSDK 相機 */
const cameraError = ref<string | null>(null)

const shotCount = computed(() => tp.shotCount.value)
/** 強制拍攝不等待對焦（到時機就拍，不管有無對焦成功） */
function getForceWithoutAf(): boolean {
  const v = import.meta.env.VITE_FORCE_CAPTURE_WITHOUT_AF
  return v === '1' || String(v).toLowerCase() === 'true'
}

/** 貼圖功能開關：1 或 true 時啟用貼圖，0 或 false 時隱藏貼圖選項 */
function isStickerEnabled(): boolean {
  const v = import.meta.env.VITE_STICKER_ENABLED
  return v === '1' || String(v).toLowerCase() === 'true'
}

/** 只半按模式：進入拍照頁只對焦不自動倒數，需手動點「開始倒數拍照」才會倒數與拍照 */
const onlyHalfPressMode = computed(
  () =>
    String(import.meta.env.VITE_SHOOT_ONLY_HALF_PRESS ?? '') === '1' ||
    String(import.meta.env.VITE_SHOOT_ONLY_HALF_PRESS ?? '').toLowerCase() === 'true'
)

/** 倒數與對焦參數：可由 .env 覆寫。
 * VITE_FOCUS_AT_SECONDS=10,5 表示在倒數 10 秒與 5 秒時「觸發對焦」；預設 10,5。拍攝只在 T=0 一次。
 * VITE_COUNTDOWN_MINUS5_MODE=1：在 VITE_COUNTDOWN_SECONDS 基礎上倒數總長減 5 秒、對焦只觸發一次、音檔改為 倒數5秒拍照.mp3。 */
function getCountdownOptions(): {
  countdownSeconds: number
  focusAtSeconds: number[]
  focusWaitAfterMs: number
  shootAfterFirstFocus: boolean
  countdownAudioFile?: string
} {
  const secRaw = String(import.meta.env.VITE_COUNTDOWN_SECONDS ?? '10').trim()
  const baseCountdown = Math.min(30, Math.max(1, parseInt(secRaw, 10) || 10))
  const defaultFocus = [10, 5]
  const focusRaw = String(import.meta.env.VITE_FOCUS_AT_SECONDS ?? '10,5').trim()
  const focusAtSeconds = focusRaw
    ? focusRaw.split(',').map((s) => parseInt(s.trim(), 10)).filter((n) => !Number.isNaN(n) && n >= 1 && n <= 30)
    : defaultFocus
  const waitRaw = String(import.meta.env.VITE_FOCUS_WAIT_AFTER_MS ?? '350').trim()
  const focusWaitAfterMs = Math.min(2000, Math.max(100, parseInt(waitRaw, 10) || 350))
  const shootRaw = String(import.meta.env.VITE_SHOOT_AFTER_FIRST_FOCUS ?? '0').trim()
  const shootAfterFirstFocus = shootRaw === '1' || shootRaw.toLowerCase() === 'true'

  if (isCountdownMinus5Mode()) {
    const countdownSeconds = Math.max(1, baseCountdown - 5)
    return {
      countdownSeconds,
      focusAtSeconds: [countdownSeconds],
      focusWaitAfterMs,
      shootAfterFirstFocus: false,
      countdownAudioFile: '倒數5秒拍照.mp3',
    }
  }

  return {
    countdownSeconds: baseCountdown,
    focusAtSeconds: focusAtSeconds.length > 0 ? focusAtSeconds : defaultFocus,
    focusWaitAfterMs,
    shootAfterFirstFocus,
  }
}
/** 左側縮圖：有拍照用原圖，沒拍照一開始就顯示外匡圖；外匡兩層 */
const thumbList = computed(() =>
  Array.from({ length: shotCount.value }, (_, i) => ({
    id: i + 1,
    url: thumbUrls.value[i] ?? '',
    frameUrl: tp.getCurrentFrameUrl(i),
  }))
)

/** 依版型帶入的根節點 class，方便依 .screen--shoot--bk01 等重寫 CSS */
const shootRootClass = computed(() => {
  const t = selectedTemplate.value
  const base = ['screen', 'screen--shoot']
  const idClass = t ? `screen--shoot--${t.id}` : 'screen--shoot--none'
  const layoutClass = t?.shootLayout?.layoutKey
    ? `screen--shoot--layout-${t.shootLayout!.layoutKey}`
    : ''
  const reshootingClass = isReshooting.value ? 'is-reshooting' : ''
  return [...base, idClass, layoutClass, reshootingClass].filter(Boolean)
})

/** 依版型帶入的 CSS 變數，可在拍照畫面 CSS 內用 var(--shoot-capture-w) 等 */
const shootRootStyle = computed(() => {
  const t = selectedTemplate.value
  if (!t) return {}
  const layout = t.shootLayout ?? {}
  const w = layout.captureW ?? t.captureW
  const h = layout.captureH ?? t.captureH
  const previewScale = typeof layout.previewScale === 'number' ? layout.previewScale : 0.5
  const vars: Record<string, string> = {
    '--shoot-capture-w': `${w}px`,
    '--shoot-capture-h': `${h}px`,
    '--shoot-shot-count': String(t.shotCount),
    '--shoot-preview-scale': String(previewScale),
  }
  if (layout.cssVars) Object.assign(vars, layout.cssVars)
  return vars
})

/**
 * 拍照區尺寸。濾鏡／貼圖模式時改為「與合成 slot 同比例」，讓編輯時所見位置與合成後一致。
 */
const pictureAreaStyle = computed(() => {
  const baseW = tp.pictureAreaWidth.value
  const baseH = tp.pictureAreaHeight.value
  if (!showFilterOptions.value) {
    return { width: `${baseW}px`, height: `${baseH}px` }
  }
  const tpl = selectedTemplate.value
  const slots = tpl ? getSynthesisSlots(tpl) : []
  const slot = slots[currentSlotIndex.value]
  if (!slot || slot.w <= 0 || slot.h <= 0) {
    return { width: `${baseW}px`, height: `${baseH}px` }
  }
  // 以 slot 比例為準：固定寬度，高度 = 寬 * (slot.h/slot.w)
  const w = baseW
  const h = Math.round(w * (slot.h / slot.w))
  return { width: `${w}px`, height: `${h}px` }
})

/** 左側縮圖容器：與右側大圖同比例（讀取框圖大小），避免左邊裁切跑掉 */
const THUMB_HEIGHT = 203
const thumbWrapStyle = computed(() => {
  const w = tp.pictureAreaWidth.value
  const h = tp.pictureAreaHeight.value
  if (!h || h <= 0) return { width: '277px', height: `${THUMB_HEIGHT}px` }
  const widthPx = Math.round(THUMB_HEIGHT * (w / h))
  return { width: `${widthPx}px`, height: `${THUMB_HEIGHT}px` }
})

/** 框圖 .cover：拍完後用選中格的框，拍攝中用目前格的框；與大圖同用 100% 填滿容器，不溢出 */
const coverStyle = computed(() => {
  const url = tp.shootingDone.value
    ? tp.getCurrentFrameUrl(tp.currentMainIndex.value - 1)
    : tp.coverFrameUrl.value
  return {
    backgroundImage: url ? `url('${url}')` : 'none',
    backgroundSize: 'contain',
    backgroundPosition: 'center',
    backgroundRepeat: 'no-repeat',
  }
})

/** 倒數顯示：不顯示 11 與 0，只顯示 10～1 */
const countdownDisplay = computed(() => {
  const n = tp.countdownNum.value
  return n >= 1 && n <= 10 ? String(n) : ''
})

/** 右側主預覽圖（目前選中的縮圖）；拍完後與外匡兩層顯示 */
const mainPreviewUrl = computed(() => {
  if (!tp.shootingDone.value || !thumbUrls.value.length) return ''
  const i = tp.currentMainIndex.value - 1
  return thumbUrls.value[i] ?? thumbUrls.value[0] ?? ''
})

/** 濾鏡區主預覽用 canvas（僅中間大圖；左側縮圖改回 img 模式以降低記憶體） */
const filterPreviewCanvasRef = ref<HTMLCanvasElement | null>(null)
/** 非濾鏡模式時的主預覽 img */
const mainPreviewImgRef = ref<HTMLImageElement | null>(null)

/** 右側可選貼圖清單（Texture 資料夾 1.png～10.png） */
const stickerOptions = [
  { id: 'texture-1', label: '貼圖 1', imageUrl: '/assets/templates/Texture/1.png' },
  { id: 'texture-2', label: '貼圖 2', imageUrl: '/assets/templates/Texture/2.png' },
  { id: 'texture-3', label: '貼圖 3', imageUrl: '/assets/templates/Texture/3.png' },
  { id: 'texture-4', label: '貼圖 4', imageUrl: '/assets/templates/Texture/4.png' },
  { id: 'texture-5', label: '貼圖 5', imageUrl: '/assets/templates/Texture/5.png' },
  { id: 'texture-6', label: '貼圖 6', imageUrl: '/assets/templates/Texture/6.png' },
  { id: 'texture-7', label: '貼圖 7', imageUrl: '/assets/templates/Texture/7.png' },
  { id: 'texture-8', label: '貼圖 8', imageUrl: '/assets/templates/Texture/8.png' },
  { id: 'texture-9', label: '貼圖 9', imageUrl: '/assets/templates/Texture/9.png' },
  { id: 'texture-10', label: '貼圖 10', imageUrl: '/assets/templates/Texture/10.png' },
  { id: 'texture-10', label: '貼圖 11', imageUrl: '/assets/templates/Texture/11.png' },
  { id: 'texture-10', label: '貼圖 12', imageUrl: '/assets/templates/Texture/12.png' },
  { id: 'texture-10', label: '貼圖 13', imageUrl: '/assets/templates/Texture/13.png' },
  { id: 'texture-10', label: '貼圖 14', imageUrl: '/assets/templates/Texture/14.png' },
  { id: 'texture-10', label: '貼圖 15', imageUrl: '/assets/templates/Texture/15.png' },
]

type DragState = {
  id: string
  offsetX: number
  offsetY: number
} | null

type PinchState = {
  id: string
  initialDistance: number
  initialScale: number
} | null

const draggingSticker = ref<DragState>(null)
const pinchingSticker = ref<PinchState>(null)

/** 觸控雙擊刪除：記錄上次點擊時間與貼圖 ID */
let lastTapTime = 0
let lastTapStickerId: string | null = null
let didMoveDuringTouch = false

function onAddSticker(option: { imageUrl: string }) {
  // 加在「目前選中的那一格」中央
  addSticker(currentSlotIndex.value, option.imageUrl, 0.5, 0.5, 1)
}

function getStickerStyle(sticker: { x: number; y: number; scale: number }) {
  const left = `${sticker.x * 100}%`
  const top = `${sticker.y * 100}%`
  const width = `${20 * sticker.scale}%` // 與合成邏輯 BASE_WIDTH_RATIO=0.2 對應
  return {
    left,
    top,
    width,
    transform: 'translate(-50%, -50%)',
  }
}

function onStickerMouseDown(id: string, e: MouseEvent) {
  if (!showFilterOptions.value) return
  const area = pictureAreaRef.value
  if (!area) return
  const rect = area.getBoundingClientRect()
  const st = stickersForCurrentSlot.value.find((s: { id: string }) => s.id === id)
  if (!st) return
  const centerX = rect.left + st.x * rect.width
  const centerY = rect.top + st.y * rect.height
  draggingSticker.value = {
    id,
    offsetX: e.clientX - centerX,
    offsetY: e.clientY - centerY,
  }
  window.addEventListener('mousemove', onStickerMouseMove, true)
  window.addEventListener('mouseup', onStickerMouseUp, true)
}

function onStickerMouseMove(e: MouseEvent) {
  const drag = draggingSticker.value
  const area = pictureAreaRef.value
  if (!drag || !area) return
  const rect = area.getBoundingClientRect()
  let centerX = e.clientX - drag.offsetX
  let centerY = e.clientY - drag.offsetY
  centerX = Math.max(rect.left, Math.min(rect.right, centerX))
  centerY = Math.max(rect.top, Math.min(rect.bottom, centerY))
  const x = (centerX - rect.left) / rect.width
  const y = (centerY - rect.top) / rect.height
  updateSticker(currentSlotIndex.value, drag.id, { x, y })
}

function onStickerMouseUp() {
  draggingSticker.value = null
  window.removeEventListener('mousemove', onStickerMouseMove, true)
  window.removeEventListener('mouseup', onStickerMouseUp, true)
}

function onStickerWheel(id: string, e: WheelEvent) {
  const st = stickersForCurrentSlot.value.find((s: { id: string }) => s.id === id)
  if (!st) return
  const factor = e.deltaY > 0 ? 0.9 : 1.1
  const nextScale = st.scale * factor
  updateSticker(currentSlotIndex.value, id, { scale: nextScale })
}

/** 觸控：兩指距離 */
function getTouchDistance(touches: TouchList): number {
  const t0 = touches[0]
  const t1 = touches[1]
  if (!t0 || !t1) return 0
  const dx = t1.clientX - t0.clientX
  const dy = t1.clientY - t0.clientY
  return Math.hypot(dx, dy)
}

function onStickerTouchStart(id: string, e: TouchEvent) {
  if (!showFilterOptions.value) return
  const area = pictureAreaRef.value
  if (!area) return
  const rect = area.getBoundingClientRect()
  const st = stickersForCurrentSlot.value.find((s: { id: string }) => s.id === id)
  if (!st) return

  if (e.touches.length === 2) {
    // 雙指：開始縮放手勢
    draggingSticker.value = null
    pinchingSticker.value = {
      id,
      initialDistance: getTouchDistance(e.touches),
      initialScale: st.scale,
    }
    window.addEventListener('touchmove', onStickerTouchMove, { capture: true, passive: false })
    window.addEventListener('touchend', onStickerTouchEnd, true)
    window.addEventListener('touchcancel', onStickerTouchEnd, true)
  } else if (e.touches.length === 1) {
    // 單指：開始拖曳（或可能是雙擊刪除的第一次點擊）
    const t0 = e.touches[0]
    if (!t0) return
    pinchingSticker.value = null
    didMoveDuringTouch = false
    const centerX = rect.left + st.x * rect.width
    const centerY = rect.top + st.y * rect.height
    draggingSticker.value = {
      id,
      offsetX: t0.clientX - centerX,
      offsetY: t0.clientY - centerY,
    }
    window.addEventListener('touchmove', onStickerTouchMove, { capture: true, passive: false })
    window.addEventListener('touchend', onStickerTouchEnd, true)
    window.addEventListener('touchcancel', onStickerTouchEnd, true)
  }
}

function onStickerTouchMove(e: TouchEvent) {
  if (e.touches.length === 0) return
  const area = pictureAreaRef.value
  if (!area) return
  const rect = area.getBoundingClientRect()

  if (pinchingSticker.value && e.touches.length >= 2) {
    const pinch = pinchingSticker.value
    const dist = getTouchDistance(e.touches)
    if (dist > 0) {
      let scale = (pinch.initialScale * dist) / pinch.initialDistance
      scale = Math.max(0.3, Math.min(3, scale))
      updateSticker(currentSlotIndex.value, pinch.id, { scale })
    }
    e.preventDefault()
    return
  }

  if (draggingSticker.value && e.touches.length === 1) {
    didMoveDuringTouch = true
    const t0 = e.touches[0]
    if (!t0) return
    const drag = draggingSticker.value
    let centerX = t0.clientX - drag.offsetX
    let centerY = t0.clientY - drag.offsetY
    centerX = Math.max(rect.left, Math.min(rect.right, centerX))
    centerY = Math.max(rect.top, Math.min(rect.bottom, centerY))
    const x = (centerX - rect.left) / rect.width
    const y = (centerY - rect.top) / rect.height
    updateSticker(currentSlotIndex.value, drag.id, { x, y })
    e.preventDefault()
  }
}

function onStickerTouchEnd(e: TouchEvent) {
  if (e.touches.length < 2) pinchingSticker.value = null
  if (e.touches.length === 0) {
    const wasDragging = draggingSticker.value
    const tappedId = wasDragging?.id ?? null
    draggingSticker.value = null
    window.removeEventListener('touchmove', onStickerTouchMove, true)
    window.removeEventListener('touchend', onStickerTouchEnd, true)
    window.removeEventListener('touchcancel', onStickerTouchEnd, true)
    // 觸控雙擊刪除：若為輕觸（未拖曳）且與上次點擊同一貼圖、間隔 < 400ms，則刪除
    if (tappedId && !didMoveDuringTouch && !pinchingSticker.value) {
      const now = Date.now()
      if (lastTapStickerId === tappedId && now - lastTapTime < 400) {
        onStickerRemove(tappedId)
        lastTapStickerId = null
        lastTapTime = 0
        return
      }
      lastTapStickerId = tappedId
      lastTapTime = now
    }
  }
}

function onStickerRemove(id: string) {
  removeSticker(currentSlotIndex.value, id)
}

/** 濾鏡模式時：把 mainPreviewUrl 畫到 canvas 並套用 selectedFilter（僅中間大圖） */
function drawFilterPreview() {
  const canvas = filterPreviewCanvasRef.value
  const url = mainPreviewUrl.value
  if (!canvas || !url || !showFilterOptions.value) return
  const parent = canvas.parentElement
  if (!parent) return
  const w = parent.clientWidth
  const h = parent.clientHeight
  if (w <= 0 || h <= 0) return
  const img = new Image()
  img.crossOrigin = 'anonymous'
  img.onload = () => {
    if (!showFilterOptions.value) return
    const dpr = window.devicePixelRatio || 1
    const cw = Math.round(w * dpr)
    const ch = Math.round(h * dpr)
    canvas.width = cw
    canvas.height = ch
    canvas.style.width = `${w}px`
    canvas.style.height = `${h}px`
    const ctx = canvas.getContext('2d')
    if (!ctx) return
    ctx.save()
    ctx.scale(dpr, dpr)
    const scale = Math.max(w / img.naturalWidth, h / img.naturalHeight)
    const drawW = img.naturalWidth * scale
    const drawH = img.naturalHeight * scale
    const dx = (w - drawW) / 2
    const dy = (h - drawH) / 2
    ctx.filter = getFilterCssForCanvas(selectedFilter.value)
    ctx.drawImage(img, 0, 0, img.naturalWidth, img.naturalHeight, dx, dy, drawW, drawH)
    const balance = getColorBalanceForFilter(selectedFilter.value)
    if (balance) {
      const sx = Math.round(dx * dpr)
      const sy = Math.round(dy * dpr)
      const sw = Math.max(1, Math.round(drawW * dpr))
      const sh = Math.max(1, Math.round(drawH * dpr))
      requestAnimationFrame(() => {
        if (!showFilterOptions.value) return
        const imageData = ctx.getImageData(sx, sy, sw, sh)
        applyColorBalance(imageData, balance.deltaR, balance.deltaG, balance.deltaB)
        ctx.putImageData(imageData, sx, sy)
      })
    }
    ctx.restore()
  }
  img.onerror = () => {}
  img.src = url
}

watch(
  () => [showFilterOptions.value, mainPreviewUrl.value, selectedFilter.value],
  () => {
    if (showFilterOptions.value && mainPreviewUrl.value) {
      nextTick(() => {
        requestAnimationFrame(() => drawFilterPreview())
      })
    }
  },
  { immediate: true }
)

watch(showFilterOptions, (visible) => {
  if (visible) startFilterCountdown()
  else stopFilterCountdown()
})

/** 左側縮圖上的外匡 style（每格各自框） */
function thumbCoverStyle(frameUrl: string) {
  return {
    backgroundImage: frameUrl ? `url('${frameUrl}')` : 'none',
    backgroundSize: 'contain',
    backgroundPosition: 'center',
    backgroundRepeat: 'no-repeat',
  }
}

function shootLog(msg: string) {
  console.log('[Shoot]', msg)
  callHost('append_shoot_log', { msg }).catch(() => {})
}

function logWaitForLiveViewReady(reason: string, frameCountBeforeRestart: number) {
  // #region agent log
  fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      location: 'ScreenShoot.vue:waitForLiveViewReady',
      message: 'wait_ready_resolve',
      data: {
        reason,
        frameCountBeforeRestart,
        frameCountNow: liveViewFrameCount.value,
        hasUrl: !!hostLiveViewDataUrl.value,
      },
      timestamp: Date.now(),
      sessionId: 'debug-session',
      runId: 'run1',
      hypothesisId: 'H2',
    }),
  }).catch(() => {})
  // #endregion
}

async function stopLiveViewWithClear(reason: string) {
  // #region agent log
  fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      location: 'ScreenShoot.vue:stopLiveViewWithClear',
      message: 'before_stop_liveview',
      data: { reason, hasUrl: !!hostLiveViewDataUrl.value, frameCount: liveViewFrameCount.value },
      timestamp: Date.now(),
      sessionId: 'debug-session',
      runId: 'run1',
      hypothesisId: 'H1',
    }),
  }).catch(() => {})
  // #endregion
  clearHostLiveViewDataUrl(reason)
  await callHost('stop_liveview', {}).catch(() => {})
  // #region agent log
  fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      location: 'ScreenShoot.vue:stopLiveViewWithClear',
      message: 'after_stop_liveview',
      data: { reason, hasUrl: !!hostLiveViewDataUrl.value, frameCount: liveViewFrameCount.value },
      timestamp: Date.now(),
      sessionId: 'debug-session',
      runId: 'run1',
      hypothesisId: 'H1',
    }),
  }).catch(() => {})
  // #endregion
}

/** 等待 Live View 重啟後收到「新幀」，逾時 5 秒（只以 frameCount 變化為準，避免舊幀 hasUrl 誤判 ready） */
function waitForLiveViewReady(frameCountBeforeRestart: number): Promise<void> {
  const timeoutMs = 5000
  const intervalMs = 100
  const deadline = Date.now() + timeoutMs
  return new Promise((resolve) => {
    const check = () => {
      // 僅當收到「新的幀」（frameCount 增加）才視為 Live View 恢復，避免舊的 hasUrl 導致第一次重拍誤判 ready
      if (liveViewFrameCount.value > frameCountBeforeRestart) {
        shootLog(`waitForLiveViewReady 已恢復（幀數 ${liveViewFrameCount.value} > ${frameCountBeforeRestart}）`)
        logWaitForLiveViewReady('frameCount', frameCountBeforeRestart)
        resolve()
        return
      }
      if (Date.now() >= deadline) {
        shootLog('waitForLiveViewReady 逾時，繼續下一張')
        logWaitForLiveViewReady('timeout', frameCountBeforeRestart)
        resolve()
        return
      }
      setTimeout(check, intervalMs)
    }
    check()
  })
}

// #region agent log
function debugLog(location: string, message: string, data?: Record<string, unknown>, hypothesisId?: string) {
  fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      location,
      message,
      data: data ?? {},
      timestamp: Date.now(),
      sessionId: 'debug-session',
      hypothesisId,
    }),
  }).catch(() => {})
}
// #endregion

/** 使用 EDSDK 連拍：預覽與拍照皆由 C# 推送／take_one_shot_edsdk，無 webcam。僅拍 shotCount 張（通常 4 張）。 */
async function startBurstShootEdsdk() {
  if (tp.isBurstShooting.value) {
    shootLog('startBurstShootEdsdk 忽略：已在連拍中')
    return
  }
  if (!props.isActive) {
    shootLog('startBurstShootEdsdk 忽略：已離開拍照頁')
    return
  }
  const count = shotCount.value
  const opts = getCountdownOptions()
  shootLog(`startBurstShootEdsdk 開始 count=${count} opts=${JSON.stringify(opts)} forceWithoutAf=${getForceWithoutAf()}`)
  tp.isBurstShooting.value = true
  if (hasWebView()) callHost('clear_captures', {}).catch(() => {})
  // 依格號保留空位，放棄的格留空字串，方便補拍回填
  const results: string[] = Array.from({ length: count }, () => '')
  thumbUrls.value = Array.from({ length: count }, () => '')

  for (let i = 0; i < count; i++) {
    currentShootingIndex.value = i
    shootLog(`連拍 第 ${i + 1}/${count} 張：setCoverFrameOnly → runCountdown`)
    // #region agent log
    debugLog('ScreenShoot.vue:startBurstShootEdsdk', 'COUNTDOWN_START', { shotIndex: i, total: count }, 'H1')
    // #endregion
    tp.setCoverFrameOnly(i)
    await new Promise((r) => requestAnimationFrame(r))
    // #region agent log
    debugLog('ScreenShoot.vue:startBurstShootEdsdk', 'NO_WAIT_FOR_CAPTURE_BEFORE_COUNTDOWN', { shotIndex: i }, 'H1')
    // #endregion
    try {
      await tp.runCountdownWithEvfAf(callHost, opts)
    } catch (e) {
      shootLog(`連拍 第 ${i + 1} 張 runCountdown 錯誤: ${e instanceof Error ? e.message : e}`)
    }
    // 拍照當下擷取目前 Live View 並做鏡像（與 captureFrame 相同效果）；有擷取到則優先使用
    let capturedMirroredUrl = ''
    if (hostLiveViewDataUrl.value) {
      try {
        capturedMirroredUrl = await tp.captureFrameFromImage(hostLiveViewDataUrl.value)
      } catch (e) {
        shootLog(`連拍 第 ${i + 1} 張 captureFrameFromImage 失敗: ${e instanceof Error ? e.message : e}`)
      }
    }
    shootLog(`連拍 第 ${i + 1}/${count} 張：呼叫 take_one_shot_edsdk（T=0 立即拍攝）`)
    try {
      const res = (await callHost('take_one_shot_edsdk', { index: i, shotCount: count, forceWithoutAf: getForceWithoutAf() })) as { photoUrl?: string; dataUrl?: string; thumbUrl?: string }
      const urlFromHost = res?.thumbUrl ?? res?.dataUrl ?? ''
      const url = capturedMirroredUrl || urlFromHost
      console.log('[Vue] 收到 C# 回傳:', { hasDataUrl: !!res?.dataUrl, dataUrlLen: res?.dataUrl?.length, photoUrl: res?.photoUrl })
      // #region agent log
      debugLog('ScreenShoot.vue:startBurstShootEdsdk', 'TAKE_ONE_SHOT_RETURNED', { shotIndex: i, hasUrl: !!url }, 'H1')
      if (!url) debugLog('ScreenShoot.vue:startBurstShootEdsdk', 'EMPTY_URL_NO_THUMB_UPDATE', { shotIndex: i, resultsLen: results.length }, 'H3')
      // #endregion
      shootLog(`連拍 第 ${i + 1}/${count} 張 take_one_shot 回傳 url=${url ? '有' : '無'}`)
      if (url) {
        results[i] = url
        thumbUrls.value = [...thumbUrls.value.slice(0, i), url, ...thumbUrls.value.slice(i + 1)]
      }
      // 拍完後：短暫顯示剛拍的縮圖，再重啟 Live View，等恢復後再拍下一張
      if (i < count - 1) {
        await new Promise((r) => setTimeout(r, 300))
        // #region agent log
        fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            location: 'ScreenShoot.vue:startBurstShootEdsdk',
            message: 'skip_frontend_liveview_restart',
            data: { shotIndex: i, frameCount: liveViewFrameCount.value, hasUrl: !!hostLiveViewDataUrl.value },
            timestamp: Date.now(),
            sessionId: 'debug-session',
            runId: 'run1',
            hypothesisId: 'H1',
          }),
        }).catch(() => {})
        // #endregion
      }
    } catch (e) {
      shootLog(`連拍 第 ${i + 1} 張 take_one_shot 錯誤: ${e instanceof Error ? e.message : e}，後端應已回傳截圖／placeholder，若無則用框圖占位`)
      console.error('EDSDK capture failed at shot', i + 1, e)
      callHost('recover_camera_after_error', {}).catch(() => {})
      const fallbackUrl = tp.getCurrentFrameUrl(i)
      if (fallbackUrl) {
        results[i] = fallbackUrl
        thumbUrls.value = [...thumbUrls.value.slice(0, i), fallbackUrl, ...thumbUrls.value.slice(i + 1)]
      }
      if (i < count - 1) {
        await new Promise((r) => setTimeout(r, 300))
        // #region agent log
        fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            location: 'ScreenShoot.vue:startBurstShootEdsdk',
            message: 'skip_frontend_liveview_restart_error',
            data: { shotIndex: i, frameCount: liveViewFrameCount.value, hasUrl: !!hostLiveViewDataUrl.value },
            timestamp: Date.now(),
            sessionId: 'debug-session',
            runId: 'run1',
            hypothesisId: 'H1',
          }),
        }).catch(() => {})
        // #endregion
      }
    }
  }

  currentShootingIndex.value = -1
  const filledCount = results.filter((u) => !!u).length
  shootLog(`startBurstShootEdsdk 完成 results=${filledCount}/${count} 張（依格號保留空位）`)
  tp.isBurstShooting.value = false
  if (cameraError.value) return
  tp.shootingDone.value = true
  tp.reshootUsedSlots.value = new Set()
  tp.currentMainIndex.value = 1
  thumbUrls.value = [...results]
  setCaptureResults([...results])
  // #region agent log
  fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      location: 'ScreenShoot.vue:startBurstShootEdsdk',
      message: 'burst_completed',
      data: { resultsLen: results.length, filledCount, hasUrl: !!hostLiveViewDataUrl.value, frameCount: liveViewFrameCount.value },
      timestamp: Date.now(),
      sessionId: 'debug-session',
      runId: 'run1',
      hypothesisId: 'H2',
    }),
  }).catch(() => {})
  // #endregion

  // 拍完照進入預覽時就啟動 Live View，讓第一次按重拍也能立刻顯示即時預覽（不 await，背景執行）
  if (hasWebView()) callHost('start_liveview', {}).catch(() => {})

  const testFast = String(import.meta.env.VITE_TEST_FAST_COUNTDOWN ?? '')
  if (testFast === '1' || testFast === 'true') {
    // 標記為測試模式
    setTestSession(true)
    const tplId = selectedTemplate.value?.id
    if (tplId) {
      callHost('save_test_captures', { templateId: tplId, dataUrls: results.filter(Boolean) }).catch(() => {})
    }
  }
}

async function onNext() {
  // #region agent log
  fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      location: 'ScreenShoot.vue:onNext',
      message: 'onNext_entry',
      data: { shootingDone: tp.shootingDone.value, showFilterOptionsBefore: showFilterOptions.value },
      timestamp: Date.now(),
      hypothesisId: 'H2',
    }),
  }).catch(() => {})
  // #endregion
  if (tp.shootingDone.value) {
    // 進入濾鏡模式前先停止 Live View，避免 60fps 更新 hostLiveViewDataUrl 導致主線程被佔滿、drawFilterPreview 的 rAF 永遠跑不到
    if (hasWebView()) await stopLiveViewWithClear('filter_enter').catch(() => {})
    showFilterOptions.value = true
    // #region agent log
    fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        location: 'ScreenShoot.vue:onNext',
        message: 'onNext_set_showFilterOptions_true',
        data: { showFilterOptionsAfter: showFilterOptions.value },
        timestamp: Date.now(),
        hypothesisId: 'H2',
      }),
    }).catch(() => {})
    // #endregion
  }
}

async function onFilterConfirm() {
  stopFilterCountdown()
  await stopLiveViewWithClear('filter_confirm').catch(() => {})
  await new Promise((r) => setTimeout(r, 100))
  buildFinalOutput()
}

/** 下一步按鈕點擊：記錄目前狀態與分支後再呼叫 onNext / onFilterConfirm */
function handleNextClick() {
  // #region agent log
  fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      location: 'ScreenShoot.vue:handleNextClick',
      message: 'next_btn_clicked',
      data: {
        showFilterOptions: showFilterOptions.value,
        shootingDone: tp.shootingDone.value,
        branch: showFilterOptions.value ? 'onFilterConfirm' : 'onNext',
      },
      timestamp: Date.now(),
      hypothesisId: 'H1',
    }),
  }).catch(() => {})
  // #endregion
  if (showFilterOptions.value) onFilterConfirm()
  else void onNext()
}

async function onAgain() {
  if (tp.reshootUsedSlots.value.has(tp.currentMainIndex.value) || !tp.shootingDone.value) return
  if (!hasWebView()) return
  const idx = tp.currentMainIndex.value - 1
  const shotCountVal = shotCount.value
  // #region agent log
  fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ location: 'ScreenShoot.vue:onAgain:entry', message: 'onAgain entry', data: { idx, currentMainIndex: tp.currentMainIndex.value, thumbUrlsLen: thumbUrls.value.length, shotCount: shotCountVal, thumbHasUrlAtIdx: !!(thumbUrls.value[idx]) }, timestamp: Date.now(), sessionId: 'debug-session', runId: 'run1', hypothesisId: 'H1' }) }).catch(() => {})
  // #endregion
  shootLog(`onAgain 開始 idx=${idx} slot=${tp.currentMainIndex.value}`)
  // #region agent log
  fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      location: 'ScreenShoot.vue:onAgain',
      message: 'debug_entry',
      data: {
        idx,
        shootingDone: tp.shootingDone.value,
        reshootSlotsSize: tp.reshootUsedSlots.value.size,
        hasHostUrl: !!hostLiveViewDataUrl.value,
        hostLen: hostLiveViewDataUrl.value?.length ?? 0,
        liveViewFrameCount: liveViewFrameCount.value,
        isReshootingBefore: isReshooting.value,
      },
      timestamp: Date.now(),
      runId: 'run1',
      hypothesisId: 'H1',
    }),
  }).catch(() => {})
  // #endregion
  isReshooting.value = true
  // 重拍開頭先清空舊 Live View 幀，避免殘留畫面誤導判斷（UI 會進入「等待鏡頭…」狀態）
  clearHostLiveViewDataUrl('reshoot_start')
  tp.setCoverFrameOnly(idx)
  await nextTick()
  await new Promise((r) => requestAnimationFrame(r))
  // 重拍前重新啟動 Live View，確保即時預覽會顯示（連拍結束後 C# 可能已停止推送）
  const frameBeforeRestart = liveViewFrameCount.value
  await callHost('start_liveview', {}).catch(() => {})
  await waitForLiveViewReady(frameBeforeRestart)
  // 多給第一幀一點時間穩定，避免剛切換時出現黑畫面／閃爍
  if (hostLiveViewDataUrl.value) {
    await new Promise((r) => setTimeout(r, 200))
  }
  // #region agent log
  fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      location: 'ScreenShoot.vue:onAgain',
      message: 'debug_before_countdown',
      data: {
        hasHostUrl: !!hostLiveViewDataUrl.value,
        hostLen: hostLiveViewDataUrl.value?.length ?? 0,
        liveViewFrameCount: liveViewFrameCount.value,
        isReshooting: isReshooting.value,
      },
      timestamp: Date.now(),
      runId: 'run1',
      hypothesisId: 'H1',
    }),
  }).catch(() => {})
  // #endregion
  try {
    await tp.runCountdownWithEvfAf(callHost, getCountdownOptions())
  } catch (e) {
    shootLog(`onAgain runCountdown 錯誤: ${e instanceof Error ? e.message : e}`)
  }
  let capturedMirroredUrl = ''
  if (hostLiveViewDataUrl.value) {
    try {
      capturedMirroredUrl = await tp.captureFrameFromImage(hostLiveViewDataUrl.value)
    } catch (e) {
      shootLog(`onAgain captureFrameFromImage 失敗: ${e instanceof Error ? e.message : e}`)
    }
  }
  shootLog(`onAgain 呼叫 take_one_shot_edsdk idx=${idx}`)
  try {
    const res = (await callHost('take_one_shot_edsdk', { index: idx, shotCount: shotCountVal, forceWithoutAf: getForceWithoutAf() })) as { photoUrl?: string; dataUrl?: string; thumbUrl?: string }
    const urlFromHost = res?.thumbUrl ?? res?.dataUrl ?? ''
    const url = capturedMirroredUrl || urlFromHost
    console.log('[Vue] 收到 C# 回傳 (onAgain):', { hasDataUrl: !!res?.dataUrl, dataUrlLen: res?.dataUrl?.length, photoUrl: res?.photoUrl })
    // #region agent log
    fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ location: 'ScreenShoot.vue:onAgain:after_take', message: 'take_one_shot_edsdk returned', data: { idx, hasUrl: !!url, urlLen: url?.length ?? 0 }, timestamp: Date.now(), sessionId: 'debug-session', runId: 'run1', hypothesisId: 'H2' }) }).catch(() => {})
    // #endregion
    if (url) {
      // 依格號固定長度更新，避免 thumbUrls 為空或較短時 .map 無法放入補拍照片（見 debug.log burst_completed resultsLen:0）
      const next = Array.from({ length: shotCountVal }, (_, i) => (i === idx ? url : (thumbUrls.value[i] ?? '')))
      thumbUrls.value = next
      setCaptureResults([...next])
      // #region agent log
      fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ location: 'ScreenShoot.vue:onAgain:after_update', message: 'after thumbUrls set', data: { idx, newThumbLen: thumbUrls.value.length, shotCount: shotCountVal, hasUrlAtIdx: !!(thumbUrls.value[idx]) }, timestamp: Date.now(), sessionId: 'debug-session', runId: 'post-fix', hypothesisId: 'H4' }) }).catch(() => {})
      // #endregion
    }
  } catch (e) {
    console.error('EDSDK reshoot failed', e)
  }
  tp.reshootUsedSlots.value = new Set(
    Array.from(tp.reshootUsedSlots.value).concat(tp.currentMainIndex.value)
  )
  // #region agent log
  fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      location: 'ScreenShoot.vue:onAgain',
      message: 'debug_exit',
      data: {
        idx,
        reshootSlotsSizeAfter: tp.reshootUsedSlots.value.size,
        hasHostUrlAfter: !!hostLiveViewDataUrl.value,
        hostLenAfter: hostLiveViewDataUrl.value?.length ?? 0,
        liveViewFrameCountAfter: liveViewFrameCount.value,
      },
      timestamp: Date.now(),
      runId: 'run1',
      hypothesisId: 'H1',
    }),
  }).catch(() => {})
  // #endregion
  isReshooting.value = false
}

function onThumbClick(num: number) {
  if (!tp.shootingDone.value || isReshooting.value) return
  tp.currentMainIndex.value = num
}

/** 與原本 web-vue 一致：錯誤時可點重試（在 WebView 內會重新 start_liveview + 連拍） */
function onRetryCamera() {
  cameraError.value = null
  if (!hasWebView()) {
    cameraError.value = '請在拍貼機程式內使用相機（EDSDK）'
    return
  }
  callHost('start_liveview', {}).catch(() => {})
  startBurstShootEdsdk()
}

function hasWebView(): boolean {
  const w = typeof window !== 'undefined' ? window : null
  return !!(w && (w as unknown as { chrome?: { webview?: unknown } }).chrome?.webview)
}

/** 等版面穩定後再算左側面板位置，避免框圖／JS 尚未載完就計算導致位置錯誤（競態） */
function scheduleLeftPanelLayout() {
  return new Promise<void>((resolve) => {
    nextTick(() => {
      requestAnimationFrame(() => {
        requestAnimationFrame(() => {
          doUpdateLeftPanelLeft()
          hasInitialLeft.value = true
          shootLayoutReady.value = true
          resolve()
        })
      })
    })
  })
}

onMounted(() => {
  tp.setCoverAndVideoSize(0).catch(() => {})
})

onUnmounted(() => {
  stopFilterCountdown()
  if (hasWebView()) {
    stopLiveViewWithClear('unmount').catch(() => {})
  }
  window.removeEventListener('mousemove', onStickerMouseMove, true)
  window.removeEventListener('mouseup', onStickerMouseUp, true)
  window.removeEventListener('touchmove', onStickerTouchMove, true)
  window.removeEventListener('touchend', onStickerTouchEnd, true)
  window.removeEventListener('touchcancel', onStickerTouchEnd, true)
})

/** 直接進入濾鏡區測試用：無相機時使用的預設預覽圖（可被 VITE_TEST_FILTER_IMAGE 覆寫） */
const DEFAULT_FILTER_TEST_IMAGE =
  "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='400' height='300' viewBox='0 0 400 300'%3E%3Crect fill='%23666' width='400' height='300'/%3E%3Ctext x='200' y='160' text-anchor='middle' fill='%23fff' font-size='24' font-family='sans-serif'%3E測試濾鏡%3C/text%3E%3C/svg%3E"

/** 是否為「直接進入濾鏡區」測試模式（VITE_TEST_FILTER_DIRECT=1，瀏覽器可測濾鏡、不需相機） */
function isTestFilterDirect(): boolean {
  const v = String(import.meta.env.VITE_TEST_FILTER_DIRECT ?? '').trim()
  return v === '1' || v.toLowerCase() === 'true'
}

/** 防止 watch 非同步重入：同一時間只允許一組「進入拍照頁」流程，避免連拍被觸發兩次（例如拍到 8 張） */
const entryFlowRunning = ref(false)

/** 單一 watch：進入/離開拍照頁時排版、Live View、連拍（與你原本 web-vue 流程一致，改為 EDSDK 預覽+拍照） */
watch(
  () => props.isActive,
  async (active) => {
    if (!active) {
      entryFlowRunning.value = false
      shootLayoutReady.value = false
      hasInitialLeft.value = false
      thumbUrls.value = []
      tp.shootingDone.value = false
      tp.reshootUsedSlots.value = new Set()
      tp.currentMainIndex.value = 1
      showFilterOptions.value = false
      isReshooting.value = false
      cameraError.value = null
      if (hasWebView()) stopLiveViewWithClear('deactivate').catch(() => {})
      return
    }

    if (entryFlowRunning.value) return
    entryFlowRunning.value = true

    try {
      if (isTestFilterDirect()) {
        // 標記為測試模式
        setTestSession(true)
        cameraError.value = null
        selectTemplate(templates.value[0] ?? null)
        await nextTick()
        const count = shotCount.value
        const testImage =
          String(import.meta.env.VITE_TEST_FILTER_IMAGE ?? '').trim() || DEFAULT_FILTER_TEST_IMAGE
        const urls = Array.from({ length: count }, () => testImage)
        thumbUrls.value = urls
        setCaptureResults(urls)
        tp.shootingDone.value = true
        tp.reshootUsedSlots.value = new Set()
        tp.currentMainIndex.value = 1
        showFilterOptions.value = true
        try {
          await tp.setCoverAndVideoSize(0)
          await scheduleLeftPanelLayout()
        } catch {
          shootLayoutReady.value = true
        }
        return
      }

      if (!hasWebView()) {
        cameraError.value = '請在拍貼機程式內使用相機（EDSDK）'
        return
      }
      cameraError.value = null
      await callHost('start_liveview', {}).catch(() => {})
      // #region agent log
      fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          location: 'ScreenShoot.vue:watchActive',
          message: 'start_liveview_requested',
          data: { hasUrl: !!hostLiveViewDataUrl.value, frameCount: liveViewFrameCount.value },
          timestamp: Date.now(),
          sessionId: 'debug-session',
          runId: 'run1',
          hypothesisId: 'H1',
        }),
      }).catch(() => {})
      // #endregion

      if (!selectedTemplate.value) {
        await nextTick()
      }

      try {
        if (!hasInitialLeft.value) {
          shootLayoutReady.value = false
          await tp.setCoverAndVideoSize(0)
          await scheduleLeftPanelLayout()
        } else {
          shootLayoutReady.value = true
        }
      } catch {
        shootLayoutReady.value = true
      }

      const count = shotCount.value
      const onlyHalfPress = String(import.meta.env.VITE_SHOOT_ONLY_HALF_PRESS ?? '') === '1' || String(import.meta.env.VITE_SHOOT_ONLY_HALF_PRESS ?? '').toLowerCase() === 'true'
      const existing = captureResults.value
      if (!onlyHalfPress && existing.length >= count) {
        thumbUrls.value = existing.slice(0, count)
        tp.shootingDone.value = true
        tp.reshootUsedSlots.value = new Set()
        tp.currentMainIndex.value = 1
        showFilterOptions.value = false
        return
      }

      if (onlyHalfPress) {
        setTimeout(() => callHost('half_press_shutter', {}).catch(() => {}), 2000)
        await new Promise((r) => setTimeout(r, 1200))
        if (!props.isActive) return
        setTimeout(() => startBurstShootEdsdk(), 0)
        return
      }

      try {
        await tp.setCoverAndVideoSize(0)
      } catch {
        /* 框圖載入失敗仍繼續連拍 */
      }
      await new Promise((r) => setTimeout(r, 1200))
      if (!props.isActive) return
      startBurstShootEdsdk()
    } finally {
      entryFlowRunning.value = false
    }
  },
  { immediate: true }
)
</script>

<template>
  <div
    ref="shootRootRef"
    :class="shootRootClass"
    :style="shootRootStyle"
    role="region"
    aria-label="拍照畫面"
  >
    <div class="screen-shoot-content" :class="{ 'is-reshooting': isReshooting, 'is-ready': shootLayoutReady || !!cameraError, 'is-filter-mode': showFilterOptions }">

      <!-- <div class="test-red"></div> -->

      <div
        ref="leftPanelRef"
        class="left-panel"
        :class="{
          'absolute-panel': !showFilterOptions,
          'is-filter-mode': showFilterOptions,
          'is-positioned': shootLayoutReady,
        }"
        :style="leftPanelStyle"
      >
        <!-- 濾鏡區：拍完照按「下一步」後顯示，同一頁內選濾鏡並確認 -->
        <div v-show="showFilterOptions" class="filter-list-wrap">
          <FilterOptions />
        </div>
        <div class="thumb-column">
          <div class="thumb-wrapper">
            <div
              v-for="item in thumbList"
              :key="item.id"
              class="thumb-frame"
              :class="{ 'is-selected': tp.shootingDone.value && tp.currentMainIndex.value === item.id }"
              role="button"
              tabindex="0"
              @click="onThumbClick(item.id)"
              @keydown.enter="onThumbClick(item.id)"
            >
              <div class="thumb-frame__wrap" :style="thumbWrapStyle">
                <!-- 縮圖一律用 img（濾鏡模式僅中間大圖用 canvas，左側改回 img 以降低記憶體） -->
                <img
                  :id="`shoot-page-${item.id}`"
                  class="shoot-page"
                  :class="{ 'shoot-page--live-view-mirror': (tp.isBurstShooting.value && item.id - 1 === currentShootingIndex && hostLiveViewDataUrl) || (isReshooting && tp.currentMainIndex.value === item.id && hostLiveViewDataUrl) }"
                  :alt="`縮圖 ${item.id}`"
                  :src="(tp.isBurstShooting.value && item.id - 1 === currentShootingIndex && hostLiveViewDataUrl) || (isReshooting && tp.currentMainIndex.value === item.id && hostLiveViewDataUrl) ? hostLiveViewDataUrl : (item.url || item.frameUrl)"
                />
                <div
                  class="thumb-frame__cover"
                  :style="thumbCoverStyle(item.frameUrl)"
                  aria-hidden="true"
                />
              </div>
            </div>
          </div>
          <p
            v-show="!showFilterOptions && tp.shootingDone.value"
            class="thumb-reshoot-hint"
          >
            每張照片皆可重拍一次
          </p>
        </div>
      </div>
      <div class="right-panel">
        <div class="title-and-picture-column">
          <div class="title-align-wrap">
            <div class="title-align-inner">
              <h1 v-if="showFilterOptions && isStickerEnabled()">選擇濾鏡與貼圖</h1>
              <img
                v-else-if="showFilterOptions"
                class="shoot-filter-title-img"
                src="/assets/templates/ShootPage/choosetext.png"
                alt="選擇濾鏡"
              />
              <img
                v-else
                class="shoot-read-title-img"
                src="/assets/templates/ShootPage/Readtext.png"
                alt="請看上方鏡頭"
              />
            </div>
            <div v-if="showFilterOptions && isStickerEnabled()" class="title-align-spacer" />
          </div>
          <div class="picture-and-stickers">
          <div
            ref="pictureAreaRef"
            class="picture-area"
            :class="{ 'is-preview': tp.shootingDone.value && !isReshooting }"
            :style="pictureAreaStyle"
          >
          <!-- 相機錯誤時顯示（非 WebView 或 EDSDK 失敗），與原本 web-vue 排版一致 -->
          <div
            v-if="cameraError"
            class="shoot-camera-error"
            role="alert"
          >
            <p class="shoot-camera-error__msg">{{ cameraError }}</p>
            <button
              type="button"
              class="shoot-camera-error__retry"
              @click="onRetryCamera"
            >
              重試
            </button>
          </div>
          <div class="cover" :style="coverStyle" aria-hidden="true" />
          <!-- 預覽與拍照皆由 C# 推送：EDSDK Live View 即時預覽、拍完顯示選中照片；重拍時也要顯示 Live View -->
          <img
            v-show="hostLiveViewDataUrl && (!tp.shootingDone.value || isReshooting)"
            class="host-live-view-img"
            :src="hostLiveViewDataUrl"
            alt="即時預覽"
          />
          <!-- 連拍進行中：貼在「預覽框（picture-area）右側」顯示 tex1/tex2/... -->
          <img v-if="shootTexUrl" :src="shootTexUrl" class="shoot-shot-tex" alt="" aria-hidden="true" />
          <!-- 倒數層放在 Live View 之上，確保數字可見 -->
          <div
            v-show="tp.countdownVisible.value && countdownDisplay"
            class="shoot-countdown"
            aria-hidden="true"
          >
            {{ countdownDisplay }}
          </div>
          <div
            v-show="hasWebView() && !hostLiveViewDataUrl && (!tp.shootingDone.value || isReshooting)"
            class="shoot-waiting"
            aria-hidden="true"
          >
            等待鏡頭…
          </div>
          <!-- 只半按模式：有畫面後可手動觸發倒數＋拍照 -->
          <button
            v-show="onlyHalfPressMode && hostLiveViewDataUrl && !tp.shootingDone.value && !tp.isBurstShooting.value"
            type="button"
            class="shoot-start-countdown-btn"
            @click="startBurstShootEdsdk"
          >
            開始倒數拍照
          </button>
            <!-- 濾鏡區：用 canvas 顯示主預覽並套用 selectedFilter -->
            <canvas
              v-show="showFilterOptions && tp.shootingDone.value && !isReshooting"
              ref="filterPreviewCanvasRef"
              class="shoot-main-preview shoot-main-preview--canvas"
              aria-label="預覽（濾鏡）"
            />
            <!-- 非濾鏡區：用 img 顯示主預覽 -->
            <img
              ref="mainPreviewImgRef"
              v-show="!showFilterOptions && tp.shootingDone.value && !isReshooting"
              class="shoot-main-preview"
              :src="mainPreviewUrl"
              alt="預覽"
            />
            <!-- 使用者貼圖圖層：可拖曳與滾輪縮放（依 VITE_STICKER_ENABLED 開關） -->
            <div
              v-show="showFilterOptions && tp.shootingDone.value && !isReshooting && isStickerEnabled()"
              class="stickers-layer"
            >
              <div
                v-for="sticker in stickersForCurrentSlot"
                :key="sticker.id"
                class="sticker-instance"
                :style="getStickerStyle(sticker)"
                @mousedown.prevent.stop="onStickerMouseDown(sticker.id, $event)"
                @touchstart.prevent.stop="onStickerTouchStart(sticker.id, $event)"
                @wheel.prevent.stop="onStickerWheel(sticker.id, $event)"
                @dblclick.stop="onStickerRemove(sticker.id)"
              >
                <img :src="sticker.imageUrl" alt="貼圖" />
              </div>
            </div>
          </div>
          <div
            v-if="showFilterOptions && isStickerEnabled()"
            class="sticker-panel"
          >
            <p class="sticker-panel__title">選擇貼圖</p>
            <div class="sticker-panel__options">
              <div
                v-for="option in stickerOptions"
                :key="option.id"
                class="sticker-option"
                role="button"
                tabindex="0"
                @click="onAddSticker(option)"
                @keydown.enter.prevent="onAddSticker(option)"
              >
                <img :src="option.imageUrl" :alt="option.label" />
              </div>
            </div>
            <p class="sticker-panel__hint">
              貼圖會貼在「目前選中的那張」照片上，<br />
              可拖曳、滾輪／雙指縮放，雙擊或連點兩下刪除。
            </p>
          </div>
        </div>
        </div>
        <div
          class="btns shoot-btns"
          :class="{
            'is-visible': tp.shootingDone.value && !isReshooting,
            'is-reshooting': isReshooting,
          }"
        >
          <button
            type="button"
            class="again-btn shoot-btn"
            :class="{ 'is-hidden': tp.reshootUsedSlots.value.has(tp.currentMainIndex.value) || showFilterOptions }"
            @click="onAgain"
          />
          <div class="shoot-next-wrap">
            <button type="button" class="next-btn shoot-btn" @click="handleNextClick" />
            <!-- 濾鏡／貼圖頁 1 分鐘倒數（在下一步右側，不佔 flex 寬度以免擠動按鈕） -->
            <div
              v-show="showFilterOptions && tp.shootingDone.value && !isReshooting"
              class="filter-countdown"
              aria-label="剩餘時間"
            >
              {{ filterCountdownSeconds }}
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" scoped>
@use '@/styles/variables' as *;
@use '@/styles/mixins' as *;

/* 根節點會依版型帶入 .screen--shoot--bk01 / .screen--shoot--bk02 等，可依此重寫各版型 CSS */
/* 另有 CSS 變數：--shoot-capture-w, --shoot-capture-h, --shoot-shot-count（來自 selectedTemplate） */
.screen--shoot {
  position: relative;
  // background-color: #ff4d4f;
  width: 100%;
  height: 100vh;
  background-image: url('#{$path-templates}/ShootPage/background.png');

  &.is-reshooting {
    pointer-events: none;
    user-select: none;
  }

  .shoot-countdown {
    position: absolute;
    left: 50%;
    top: 50%;
    transform: translate(-50%, -50%);
    z-index: 100;
    font-size: 120px;
    font-weight: bold;
    color: #fff;
    text-shadow: 0 4px 20px rgba(0, 0, 0, 0.8);
    pointer-events: none;

    &.is-visible {
      visibility: visible;
    }
  }

  /* 連拍進行中：tex1/tex2/... 顯示在預覽框（picture-area）右側 */
  .shoot-shot-tex {
    position: absolute;
    top: 50%;
    left: calc(100% + 12px);
    transform: translateY(-50%) scale(0.8);
    height: 100%;
    width: auto;
    max-width: 50%;
    object-fit: contain;
    object-position: left center;
    z-index: 50;
    pointer-events: none;
  }

  /* 濾鏡／貼圖頁 1 分鐘倒數：緊貼「下一步」右側，wrapper 維持與單顆 next 按鈕同寬 */
  .shoot-next-wrap {
    position: relative;
    flex-shrink: 0;
    width: 410px;
    height: 100px;
  }

  .filter-countdown {
    position: absolute;
    left: 100%;
    top: 50%;
    transform: translateY(-50%);
    margin-left: 0.75rem;
    z-index: 50;
    font-size: 48px;
    font-weight: bold;
    color: #fff;
    text-shadow: 0 2px 12px rgba(0, 0, 0, 0.8);
    min-width: 2.5em;
    text-align: center;
    white-space: nowrap;
    pointer-events: none;
  }

  .screen-shoot-content {
    display: flex;
    width: 100%;
    height: 100%;
    justify-content: center;
    opacity: 0;
    transition: opacity 0.2s ease;
    &.is-ready {
      opacity: 1;
    }

    &.is-reshooting {
      pointer-events: none;
      user-select: none;
    }
  }

  .left-panel {
    // width: 1000px;
    margin-right: 50px;
    height: 100%;
    display: flex;
    justify-content: flex-end;
    position: relative;
    gap: 36px;
    // padding-top: 100px;
  }
  .absolute-panel {
    justify-content: flex-start;

    width: auto;
    // background-color: #ff4d4f;
    position: absolute;
    /* left 只算一次（leftPanelStyle），之後不再更新 */
    opacity: 0;
    transition: opacity 0.15s ease-out;

    &.is-positioned {
      opacity: 1;
    }
  }

  /* 進入濾鏡時移除 absolute 排版，恢復一般 flow，確保濾鏡列表可見 */
  .left-panel.is-filter-mode {
    position: relative;
    left: auto;
    justify-content: flex-end;
    opacity: 1;
    min-width: 280px; /* 濾鏡列表約 250px + 間距，避免被壓成 0 寬 */
  }

  .filter-list-wrap {
    padding-top: 50px;
    display: flex;
    // position: absolute;
    // left: 20px;
    // top: 180px;
    // z-index: 10;
  }

  /* 左側縮圖欄：與濾鏡列表並排時維持單一縱欄（縮圖＋提示） */
  .thumb-column {
    display: flex;
    flex-direction: column;
    align-items: center;
    flex-shrink: 0;
  }

  .thumb-reshoot-hint {
    margin: 0;
    margin-top: 12px;
    padding: 0 8px;
    max-width: 280px;
    text-align: center;
    font-size: 30px;
    line-height: 1.45;
    font-weight: 500;
    color: rgba(255, 255, 255, 0.95);
    text-shadow:
      -1px -1px 0 #000,
      1px -1px 0 #000,
      -1px 1px 0 #000,
      1px 1px 0 #000,
      0 -1px 0 #000,
      0 1px 0 #000,
      -1px 0 0 #000,
      1px 0 0 #000,
      0 2px 8px rgba(0, 0, 0, 0.55);
    user-select: none;
  }

  .thumb-wrapper {
    padding-top: 100px;
    display: flex;
    flex-direction: column;
    // justify-content: center;
    align-items: center;
    gap: 24px;
    // position: absolute;
    // top: 135px;
    // left: 299px;
    // width: 100%;
    // height: 100%;
    // display: flex;
    // flex-direction: column;
    // gap: $spacing-md;
  }

  .thumb-frame {
    cursor: pointer;
    outline: none;

    &.is-selected {
      outline: 4px solid #ff4d4f;
      outline-offset: 2px;
    }

    .thumb-frame__wrap {
      background-color: bisque;
      position: relative;
      display: flex;
      flex-shrink: 0;
      /* 寬高由 thumbWrapStyle 依框圖比例決定，與右側大圖一致 */

      /* 與右側大圖一致：照片填滿框、無白邊；cover 避免正方形版型（bk03）上下留白 */
      .shoot-page {
        display: block;
        width: 100%;
        height: 100%;
        object-fit: cover;
        object-position: center;
        background-color: white;

        /* 左側縮圖顯示即時 Live View 時也做水平鏡像，與主預覽一致 */
        &.shoot-page--live-view-mirror {
          transform: scaleX(-1);
        }
      }

      .thumb-frame__cover {
        position: absolute;
        top: 0;
        left: 0;
        width: 100%;
        height: 100%;
        pointer-events: none;
        background-size: contain;
        background-position: center;
        background-repeat: no-repeat;
      }
    }




  }

  .right-panel {
    position: relative;
    // width: 100%;
    // background-color: aqua;
    display: flex;
    justify-content: space-between;
    align-items: center;
    flex-direction: column;
    padding: 12px 0px 60px 0px;
    gap: 12px;
    min-width: 0;

    .title-align-wrap {
      display: flex;
      align-items: center;
      gap: 24px;
      width: 100%;
      min-width: 0;
      /* 與下方預覽框保留固定間距 */
      margin-bottom: 50px;
    }

    .title-align-inner {
      flex: 1;
      min-width: 0;
      display: flex;
      justify-content: center;
    }

    .title-align-spacer {
      width: 180px;
      flex-shrink: 0;
    }

    h1 {
      font-size: 72px;
      text-align: center;
      padding: 0;
      margin: 0;
    }

    .shoot-read-title-img,
    .shoot-filter-title-img {
      display: block;
      max-height: 96px;
      width: auto;
      max-width: 100%;
      margin: 0 auto;
      object-fit: contain;
    }
  }

  .picture-and-stickers {
    display: flex;
    align-items: flex-start;
    gap: 24px;
    width: 100%;
    overflow: visible; /* 預覽區 scale(1.5) 外溢仍可顯示 */
    /* 與上方 Readtext／choosetext 同軸：預覽框在橫向置中（列寬常大於 picture-area 時避免靠左） */
    justify-content: center;
  }

  /* 濾鏡＋貼圖：左預覽＋右貼圖欄，維持由左排，與標題列 spacer 對齊 */
  .screen-shoot-content.is-filter-mode .picture-and-stickers:has(.sticker-panel) {
    justify-content: flex-start;
  }

  /* 標題與預覽共用同一寬度，確保對齊預覽框中心 */
  .title-and-picture-column {
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    width: fit-content;
    position: relative;
    overflow: visible;
  }

  .screen-shoot-content.is-filter-mode .right-panel {
    // flex: 1;
    min-width: 0;
  }

  .screen-shoot-content.is-filter-mode .picture-area {
    flex-shrink: 1;
    min-width: 0;
  }

  .picture-area {
    position: relative;
    display: flex;
    align-items: center;
    overflow: visible; /* scale 後外溢；原 hidden 會裁切 1.5 倍預覽 */
    flex-shrink: 0;
    transform: scale(var(--shoot-preview-scale, 0.5));
    transform-origin: center top;

    .shoot-camera-error {
      position: absolute;
      inset: 0;
      z-index: 50;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: $spacing-lg;
      padding: $spacing-xl;
      background: rgba(0, 0, 0, 0.75);
      color: #fff;
      text-align: center;
    }

    .shoot-camera-error__msg {
      margin: 0;
      font-size: 18px;
      line-height: 1.5;
      max-width: 360px;
    }

    .shoot-camera-error__retry {
      padding: 10px 24px;
      font-size: 16px;
      color: #fff;
      background: $color-accent;
      border: none;
      border-radius: $radius-md;
      cursor: pointer;

      &:hover {
        opacity: 0.9;
      }
    }

    .shoot-waiting {
      position: absolute;
      inset: 0;
      z-index: 2;
      display: flex;
      align-items: center;
      justify-content: center;
      color: #fff;
      font-size: 24px;
      background: rgba(0, 0, 0, 0.5);
    }

    .shoot-start-countdown-btn {
      position: absolute;
      left: 50%;
      bottom: 120px;
      transform: translateX(-50%);
      z-index: 15;
      padding: 16px 40px;
      font-size: 24px;
      font-weight: bold;
      color: #fff;
      background: var(--accent, #ff4d4f);
      border: none;
      border-radius: 12px;
      cursor: pointer;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);

      &:hover {
        opacity: 0.95;
      }
    }

    .cover {
      display: block;
    }

    /* 拍完後外匡仍顯示（原圖＋框兩層） */
    &.is-preview .cover {
      display: block;
    }

    /* 大圖與框同尺寸：讀取框圖大小決定容器，cover 填滿遮罩、避免正方形版型（bk03）上下留白 */
    .shoot-main-preview {
      display: none;
      position: absolute;
      top: 0;
      left: 0;
      width: 100%;
      height: 100%;
      object-fit: cover;
      object-position: center;
    }

    &.is-preview .shoot-main-preview {
      display: block;
    }

    .cover {
      position: absolute;
      top: 0;
      left: 0;
      width: 100%;
      height: 100%;
      z-index: 9;
      background-position: center;
      background-repeat: no-repeat;
      background-size: contain;
      pointer-events: none;
    }

    /* EDSDK Live View 由 C# 推送；前端水平鏡像顯示，與拍照擷取結果一致 */
    .host-live-view-img {
      position: absolute;
      top: 0;
      left: 0;
      width: 100%;
      height: 100%;
      object-fit: cover;
      object-position: center;
      z-index: 2;
      transform: scaleX(-1);
    }

    // shoot-shot-tex 樣式改放在 .screen--shoot 層級，避免 img 移出 picture-area 後不套用

    .stickers-layer {
      position: absolute;
      inset: 0;
      z-index: 15;
      pointer-events: none;
    }

    .sticker-instance {
      position: absolute;
      pointer-events: auto;
      cursor: grab;
      transform-origin: center center;
      touch-action: none; /* 觸控時不捲動，讓拖曳與縮放手勢生效 */

      img {
        display: block;
        width: 100%;
        height: auto;
        object-fit: contain;
        pointer-events: none;
      }

      &:active {
        cursor: grabbing;
      }
    }
  }

  .sticker-panel {
    width: 180px;
    flex-shrink: 0;
    padding-top: 40px;
    display: flex;
    flex-direction: column;
    gap: 12px;
    min-height: 0;

    .sticker-panel__title {
      margin: 0 0 8px;
      font-size: 24px;
      font-weight: bold;
      text-align: center;
      color: #333;
      flex-shrink: 0;
    }

    /* 貼圖選項可滑動捲動，超出畫面時可上下滑動 */
    .sticker-panel__options {
      display: flex;
      flex-direction: column;
      gap: 12px;
      overflow-y: auto;
      overflow-x: hidden;
      max-height: min(50vh, 400px);
      min-height: 0;
      padding-right: 4px;
      -webkit-overflow-scrolling: touch;

      &::-webkit-scrollbar {
        width: 6px;
      }
      &::-webkit-scrollbar-thumb {
        background: rgba(0, 0, 0, 0.3);
        border-radius: 3px;
      }
      &::-webkit-scrollbar-track {
        background: rgba(0, 0, 0, 0.05);
      }

      .sticker-option {
        border-radius: 16px;
        background: rgba(255, 255, 255, 0.85);
        padding: 8px;
        box-shadow: 0 2px 6px rgba(0, 0, 0, 0.18);
        cursor: pointer;
        transition: transform 0.12s ease-out, box-shadow 0.12s ease-out;
        flex-shrink: 0;

        img {
          display: block;
          width: 100%;
          height: auto;
          object-fit: contain;
        }

        &:hover {
          transform: translateY(-2px);
          box-shadow: 0 4px 10px rgba(0, 0, 0, 0.22);
        }
      }
    }

    .sticker-panel__hint {
      margin-top: 4px;
      font-size: 14px;
      line-height: 1.5;
      color: #444;
      text-align: center;
    }
  }

  .btns.shoot-btns {
    visibility: hidden;
    /* 整排（重拍＋下一步）略往右，與中央預覽較對齊 */
    margin-left: 48px;

    &.is-visible {
      visibility: visible;
    }

    &.is-reshooting {
      visibility: hidden !important;
      pointer-events: none;
    }

    /* 重拍隱藏時只用 visibility 保留佔位；禁止 display:none，避免下一步按鈕位移（勿用行內 style） */
    .again-btn.is-hidden {
      display: block !important;
      visibility: hidden !important;
      pointer-events: none;
    }
  }

  .btns {
    width: 1000px;
    padding: 0px 30px;
    overflow: visible; /* 倒數在下一步右側可能超出 1000px 寬度 */

    // width: 1000px;
    // position: absolute;
    // bottom: 70px;
    // left: 690px;
    display: flex;
    justify-content: space-between;

    .shoot-btn {
      width: 410px;
      height: 100px;
      border: none;
      background: none;
      outline: none;
      box-shadow: none;
      appearance: none;
      -webkit-appearance: none;
      background-repeat: no-repeat;
    }

    .again-btn {
      background-image: url('#{$path-templates}/ShootPage/reshoot.png');
    }

    .next-btn {
      background-image: url('#{$path-templates}/ShootPage/nextbutton.png');
    }
  }
}


.test-red {
  width: 50%;
  height: 100%;
  background-color: #ff4d4f;
position: absolute;
}
</style>
