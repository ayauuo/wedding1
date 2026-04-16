import { ref, computed } from 'vue'
import type { Template } from '@/types/photobooth'

/** 無版型時的預設擷取尺寸 */
const DEFAULT_CAPTURE_W = 544
const DEFAULT_CAPTURE_H = 471

const COUNTDOWN_AUDIO_URL = '/assets/templates/music/倒數10秒拍照.mp3'
const COUNTDOWN_AUDIO_URL_MINUS5 = '/assets/templates/music/倒數5秒拍照.mp3'

/** 與 VITE_COUNTDOWN_MINUS5_MODE 對應：倒數秒數 -5、對焦僅一次、音檔改為 倒數5秒拍照.mp3 */
export function isCountdownMinus5Mode(): boolean {
  const v = String(import.meta.env.VITE_COUNTDOWN_MINUS5_MODE ?? '').trim()
  return v === '1' || v.toLowerCase() === 'true'
}

/**
 * 在使用者手勢（例如點擊「確認」）的同一呼叫堆疊內呼叫，可解除瀏覽器對音訊的自動播放限制。
 * 建議在進入拍照頁前、按鈕的 click 處理函式開頭呼叫（不 await）。
 */
export function unlockCountdownAudio(): void {
  try {
    const url = isCountdownMinus5Mode() ? COUNTDOWN_AUDIO_URL_MINUS5 : COUNTDOWN_AUDIO_URL
    const audio = new Audio(url)
    audio.volume = 0
    audio.play().catch(() => {})
  } catch {
    // ignore
  }
}

export function useTakePicture(selectedTemplate: () => Template | null) {
  const countdownVisible = ref(false)
  const countdownNum = ref(10)
  const shootingDone = ref(false)
  const isBurstShooting = ref(false)
  const reshootUsedSlots = ref<Set<number>>(new Set())
  const currentMainIndex = ref(1)
  const coverFrameUrl = ref('')
  const pictureAreaWidth = ref(DEFAULT_CAPTURE_W)
  const pictureAreaHeight = ref(DEFAULT_CAPTURE_H)
  /** 已為哪個版型設定過拍照區尺寸（同版型只載入第一次，後續沿用不重算） */
  const lastSizeTemplateId = ref<string | null>(null)

  const shotCount = computed(() => {
    const t = selectedTemplate()
    return t?.shotCount ?? 4
  })

  /** 從版型設定的擷取／顯示寬高（displayW/displayH 或 captureW/captureH），作為載入圖前的 fallback */
  function getTemplateCaptureSize(): { w: number; h: number } {
    const t = selectedTemplate()
    const layout = t?.shootLayout
    const w = layout?.displayW ?? layout?.captureW ?? t?.captureW ?? DEFAULT_CAPTURE_W
    const h = layout?.displayH ?? layout?.captureH ?? t?.captureH ?? DEFAULT_CAPTURE_H
    return { w, h }
  }

  /** 目前使用的寬高（首次依框圖載入，同版型沿用），供 video 與 canvas 使用 */
  function getCaptureSize(): { w: number; h: number } {
    return { w: pictureAreaWidth.value, h: pictureAreaHeight.value }
  }

  /** 載入框圖並回傳其自然寬高（同版型只呼叫一次，用於取得顯示尺寸） */
  function loadFrameDimensions(url: string): Promise<{ w: number; h: number }> {
    return new Promise((resolve, reject) => {
      const img = new Image()
      img.onload = () => resolve({ w: img.naturalWidth, h: img.naturalHeight })
      img.onerror = () => reject(new Error('Frame image load failed'))
      img.src = url
    })
  }

  function getCurrentFrameUrl(index: number): string {
    const t = selectedTemplate()
    const id = t?.id ?? 'bk04'
    const num = String(index + 1).padStart(2, '0')
    return `/assets/templates/ShootPage/${id}/${id}_view${num}.png`
  }

  /**
   * 設定框圖 URL 與拍照區尺寸。同版型下框圖大小一致，只在一開始載入第一次框圖並用其實際尺寸，
   * 後續只換框圖不重算，避免攝影機區塊重算導致閃爍。第一次會先 await 載圖再設尺寸，不先設 fallback 再更新，避免閃一下。
   */
  async function setCoverAndVideoSize(index = 0): Promise<void> {
    const t = selectedTemplate()
    const templateId = t?.id ?? null
    if (templateId === lastSizeTemplateId.value) {
      setCoverFrameOnly(index)
      return
    }
    const url = getCurrentFrameUrl(0)
    let w: number
    let h: number
    try {
      const size = await loadFrameDimensions(url)
      w = size.w
      h = size.h
    } catch {
      const fallback = getTemplateCaptureSize()
      w = fallback.w
      h = fallback.h
    }
    pictureAreaWidth.value = w
    pictureAreaHeight.value = h
    lastSizeTemplateId.value = templateId
    coverFrameUrl.value = getCurrentFrameUrl(index)
  }

  /**
   * 只換框圖 URL，不重算預覽區尺寸（版型內框圖大小一致，連拍／重拍時只換圖不觸發 reflow）
   */
  function setCoverFrameOnly(index: number): void {
    coverFrameUrl.value = getCurrentFrameUrl(index)
  }

  /** 倒數從 11 開始（音檔前段「開始拍照了」對齊 11），顯示 11 → 10 → … → 1；在音檔結束前 0.5 秒就 resolve，讓拍照提早觸發 */
  const CAPTURE_EARLY_SEC = 0.5

  const isMute = (): boolean => {
    const v = import.meta.env.VITE_MUTE
    return v === '1' || String(v).toLowerCase() === 'true'
  }

  const isTestFastCountdown = (): boolean => {
    const v = import.meta.env.VITE_TEST_FAST_COUNTDOWN
    return v === '1' || String(v).toLowerCase() === 'true'
  }

  const isShootImmediate = (): boolean => {
    const v = import.meta.env.VITE_SHOOT_IMMEDIATE
    return v === '1' || String(v).toLowerCase() === 'true'
  }

  function playCountdownAudio(): Promise<void> {
    if (isShootImmediate()) {
      return Promise.resolve()
    }
    if (isTestFastCountdown()) {
      return new Promise((resolve) => {
        countdownVisible.value = true
        countdownNum.value = 2
        let n = 2
        const iv = setInterval(() => {
          n -= 1
          if (n >= 1) {
            countdownNum.value = n
          } else {
            countdownNum.value = 0
            clearInterval(iv)
            countdownVisible.value = false
            countdownNum.value = 2
            resolve()
          }
        }, 1000)
      })
    }

    if (isMute()) {
      return new Promise((resolve) => {
        countdownVisible.value = true
        countdownNum.value = 11
        let n = 11
        const iv = setInterval(() => {
          n -= 1
          if (n >= 1) {
            countdownNum.value = n
          } else {
            countdownNum.value = 0
            clearInterval(iv)
            countdownVisible.value = false
            countdownNum.value = 11
            resolve()
          }
        }, 1000)
      })
    }

    return new Promise((resolve, reject) => {
      let resolved = false
      const doResolve = () => {
        if (resolved) return
        resolved = true
        countdownVisible.value = false
        countdownNum.value = 11
        resolve()
      }
      const audio = new Audio(COUNTDOWN_AUDIO_URL)
      audio.addEventListener('ended', doResolve, { once: true })
      audio.addEventListener('error', (e) => {
        countdownVisible.value = false
        reject(e)
      }, { once: true })
      const startTime = performance.now()
      audio.play().catch((e) => {
        countdownVisible.value = false
        reject(e)
      })
      countdownVisible.value = true
      countdownNum.value = 11
      let n = 11
      const iv = setInterval(() => {
        n -= 1
        if (n >= 1) {
          countdownNum.value = n
        } else {
          countdownNum.value = 0
          clearInterval(iv)
        }
      }, 1000)
      const scheduleEarlyResolve = () => {
        const d = audio.duration
        if (typeof d === 'number' && isFinite(d) && d > CAPTURE_EARLY_SEC) {
          const elapsed = (performance.now() - startTime) / 1000
          const remaining = (d - CAPTURE_EARLY_SEC) - elapsed
          if (remaining > 0) {
            setTimeout(doResolve, remaining * 1000)
          } else {
            doResolve()
          }
        }
      }
      if (typeof audio.duration === 'number' && isFinite(audio.duration)) {
        scheduleEarlyResolve()
      } else {
        audio.addEventListener('loadedmetadata', scheduleEarlyResolve, { once: true })
      }
    })
  }

  /**
   * 倒數由 Vue 處理並顯示在畫面上，在指定秒數時由 C# 驅動單眼對焦（trigger_evf_af_with_pause）。
   * 用於 EDSDK 流程：倒數結束後再由呼叫端執行 take_one_shot_edsdk。
   * 若 shootAfterFirstFocus 為 true：只對焦一次後立即 return，讓呼叫端馬上拍照。
   * @param callHost - 呼叫 C# 的函式
   * @param options - 倒數與對焦參數（可從前端 .env 讀取）
   */
  async function runCountdownWithEvfAf(
    callHost: (cmd: string, data?: Record<string, unknown>) => Promise<unknown>,
    options?: {
      countdownSeconds?: number
      focusAtSeconds?: number[]
      focusWaitAfterMs?: number
      shootAfterFirstFocus?: boolean
      /** 傳給 C# play_countdown_audio，檔名須在 BoothBridge 白名單內（例：倒數5秒拍照.mp3） */
      countdownAudioFile?: string
    }
  ): Promise<void> {
    const countdownSec = Math.min(30, Math.max(1, options?.countdownSeconds ?? 10))
    const focusAt = new Set(options?.focusAtSeconds ?? [10])
    const waitAfterMs = Math.min(2000, Math.max(100, options?.focusWaitAfterMs ?? 350))
    const shootAfterFirstFocus = options?.shootAfterFirstFocus ?? false
    const countdownAudioFile = options?.countdownAudioFile

    const shootLog = (msg: string) => {
      console.log('[Shoot]', msg)
      callHost('append_shoot_log', { msg }).catch(() => {})
    }
    shootLog(`runCountdown 開始 countdownSec=${countdownSec} focusAt=[${[...focusAt].join(',')}] shootAfterFirstFocus=${shootAfterFirstFocus}`)

    if (!isMute() && !isTestFastCountdown()) {
      const audioPayload =
        countdownAudioFile && countdownAudioFile.length > 0 ? { fileName: countdownAudioFile } : {}
      callHost('play_countdown_audio', audioPayload).catch(() => {})
    }

    // 手機式倒數：倒數是 UX 節拍器，不等待 AF；在指定秒數僅「觸發」對焦（fire-and-forget），不阻塞倒數
    // 先停 1 秒再顯示倒數數字，讓聲音和數字結束時間對齊（僅影響數字顯示時機）
    await new Promise((r) => setTimeout(r, 1500))
    countdownVisible.value = true
    for (let sec = countdownSec; sec >= 1; sec--) {
      countdownNum.value = sec
      if (focusAt.has(sec)) {
        shootLog(`倒數 sec=${sec} 觸發 focusAt（不等待，背景對焦）`)
        callHost('trigger_evf_af_with_pause', { waitAfterMs }).catch(() => {})
        if (shootAfterFirstFocus) {
          shootLog(`shootAfterFirstFocus 提早結束，return（將立即拍照）`)
          countdownNum.value = 0
          countdownVisible.value = false
          countdownNum.value = countdownSec
          callHost('stop_countdown_audio', {}).catch(() => {})
          return
        }
      }
      await new Promise((r) => setTimeout(r, 1000))
    }
    shootLog(`runCountdown 結束，倒數完成`)
    countdownNum.value = 0
    countdownVisible.value = false
    countdownNum.value = countdownSec
    // 倒數結束就卡掉音效，不等待音檔播完
    callHost('stop_countdown_audio', {}).catch(() => {})
  }

  /**
   * 只擷取原畫面（不畫框），保留原圖供之後選濾鏡再與框合成。
   * frameImageUrl 保留參數相容，呼叫端可不改；實際不再畫框。
   * 從 video 擷取並做水平鏡像。
   */
  async function captureFrame(
    videoEl: HTMLVideoElement | null,
    _frameImageUrl?: string
  ): Promise<string> {
    const { w, h } = getCaptureSize()
    const canvas = document.createElement('canvas')
    canvas.width = w
    canvas.height = h
    const ctx = canvas.getContext('2d')
    if (!ctx || !videoEl) throw new Error('No context or video')
    ctx.save()
    ctx.translate(w, 0)
    ctx.scale(-1, 1)
    ctx.drawImage(videoEl, 0, 0, w, h)
    ctx.restore()
    return canvas.toDataURL('image/jpeg', 0.9)
  }

  /**
   * 從圖片 data URL（例如 EDSDK Live View 的 hostLiveViewDataUrl）擷取並做水平鏡像，
   * 供拍照當下擷取預覽畫面並與 captureFrame 相同鏡像效果。
   */
  async function captureFrameFromImage(dataUrl: string): Promise<string> {
    if (!dataUrl || !dataUrl.startsWith('data:')) {
      throw new Error('Invalid image data URL')
    }
    const img = new Image()
    img.crossOrigin = 'anonymous'
    await new Promise<void>((resolve, reject) => {
      img.onload = () => resolve()
      img.onerror = () => reject(new Error('Image load failed'))
      img.src = dataUrl
    })
    const w = img.naturalWidth
    const h = img.naturalHeight
    const canvas = document.createElement('canvas')
    canvas.width = w
    canvas.height = h
    const ctx = canvas.getContext('2d')
    if (!ctx) throw new Error('No canvas context')
    ctx.save()
    ctx.translate(w, 0)
    ctx.scale(-1, 1)
    ctx.drawImage(img, 0, 0, w, h)
    ctx.restore()
    return canvas.toDataURL('image/jpeg', 0.9)
  }

  return {
    getCaptureSize,
    countdownVisible,
    countdownNum,
    shootingDone,
    isBurstShooting,
    reshootUsedSlots,
    currentMainIndex,
    coverFrameUrl,
    pictureAreaWidth,
    pictureAreaHeight,
    shotCount,
    getCurrentFrameUrl,
    setCoverAndVideoSize,
    setCoverFrameOnly,
    playCountdownAudio,
    runCountdownWithEvfAf,
    captureFrame,
    captureFrameFromImage,
  }
}
