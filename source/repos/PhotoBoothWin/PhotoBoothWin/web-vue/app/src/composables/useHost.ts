const UPLOAD_URL = typeof import.meta !== 'undefined' && import.meta.env?.VITE_UPLOAD_URL

/** 無 WebView 時改走 HTTP 上傳（例如 PHP）：POST JSON 到 VITE_UPLOAD_URL */
async function uploadViaHttp(payload: { imageData?: string; videoData?: string }): Promise<Record<string, unknown>> {
  const url = typeof UPLOAD_URL === 'string' && UPLOAD_URL.trim() ? UPLOAD_URL.trim() : null
  if (!url) return Promise.resolve({ url: 'https://example.com/download/mock.jpg', videoUrl: undefined })
  const kind = payload.videoData ? 'video' : 'image'
  try {
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })
    if (!res.ok) {
      const body = await res.text()
      const errMsg = `[拍貼機] 上傳${kind}失敗: ${res.status} ${res.statusText}`
      console.error(errMsg, { url, status: res.status, statusText: res.statusText, body: body.slice(0, 500) })
      throw new Error(`${errMsg}${body ? ` | 回應: ${body.slice(0, 200)}` : ''}`)
    }
    const json = (await res.json()) as Record<string, unknown>
    return json
  } catch (e) {
    const err = e instanceof Error ? e : new Error(String(e))
    console.error(`[拍貼機] 上傳${kind}請求錯誤:`, err.message, { url, payloadKeys: Object.keys(payload) })
    throw err
  }
}

export function callHost(cmd: string, data: Record<string, unknown> = {}): Promise<Record<string, unknown>> {
  const win = typeof window !== 'undefined' ? window : null
  const hasWebView = !!(win && (win as unknown as { chrome?: { webview?: unknown } }).chrome?.webview)
  // #region agent log
  if (cmd === 'log_print_record') {
    const fileName = typeof data.fileName === 'string' ? data.fileName : ''
    const machineName = typeof data.machineName === 'string' ? data.machineName : ''
    fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ location: 'useHost.ts:callHost', message: 'log_print_record_call', data: { hasWebView, keys: Object.keys(data), fileNameLen: fileName.length, machineNameLen: machineName.length }, timestamp: Date.now(), sessionId: 'debug-session', runId: 'run1', hypothesisId: 'H1' }) }).catch(() => {})
  }
  // #endregion
  if (!hasWebView) {
    if (cmd === 'save_image') return Promise.resolve({ filePath: 'C:\\PhotoBooth\\Out\\mock.jpg' })
    if (cmd === 'upload') {
      const imageData = data.imageData as string | undefined
      if (imageData) return uploadViaHttp({ imageData })
      return Promise.resolve({ url: 'https://example.com/download/mock.jpg' })
    }
    if (cmd === 'upload_video') {
      const videoData = data.videoData as string | undefined
      if (videoData) return uploadViaHttp({ videoData })
      return Promise.resolve({ url: 'https://example.com/download/mock.mp4' })
    }
    if (cmd === 'upload_file') return Promise.resolve({ url: 'https://example.com/download/mock.jpg' })
    if (cmd === 'print_hotfolder') return Promise.resolve({ copies: (data.copies as number) || 1 })
    if (cmd === 'save_test_captures') return Promise.resolve({})
    if (cmd === 'load_test_captures') return Promise.resolve({ urls: [] as string[] })
    if (cmd === 'clear_captures') return Promise.resolve({})
    if (cmd === 'load_captures') return Promise.resolve({ urls: [] as string[] })
    if (cmd === 'start_liveview') return Promise.resolve({})
    if (cmd === 'stop_liveview') return Promise.resolve({})
    if (cmd === 'half_press_shutter') return Promise.resolve({})
    if (cmd === 'trigger_evf_af_with_pause') return Promise.resolve({})
    if (cmd === 'get_evf_drive_focus_state') return Promise.resolve({ step: 0, maxNearSteps: 10 })
    if (cmd === 'set_evf_drive_focus_max_steps') return Promise.resolve({ step: 0, maxNearSteps: (data.maxNearSteps as number) ?? 10 })
    if (cmd === 'calibrate_evf_drive_focus_far')
      return Promise.resolve({ step: 0, maxNearSteps: 10, far3RepeatCount: (data.far3RepeatCount as number) ?? 24 })
    if (cmd === 'drive_evf_focus_near1') return Promise.resolve({ ok: false, step: 0, maxNearSteps: 10 })
    if (cmd === 'drive_evf_focus_far1') return Promise.resolve({ ok: false, step: 0, maxNearSteps: 10 })
    if (cmd === 'play_countdown_audio') return Promise.resolve({})
    if (cmd === 'stop_countdown_audio') return Promise.resolve({})
    if (cmd === 'recover_camera_after_error') return Promise.resolve({})
    if (cmd === 'open_wpf_shoot') return Promise.resolve({})
    if (cmd === 'take_one_shot_edsdk') return Promise.resolve({ photoUrl: '', filePath: '' })
    if (cmd === 'wait_for_capture_ready') return Promise.resolve({})
    if (cmd === 'get_camera_status') return Promise.resolve({ isConnected: false })
    if (cmd === 'append_usage_log') return Promise.resolve({})
    if (cmd === 'append_shoot_log') return Promise.resolve({})
    if (cmd === 'result_image_ready') return Promise.resolve({})
    if (cmd === 'log_print_record') return Promise.resolve({})
    if (cmd === 'upload_to_google') return Promise.resolve({ uploaded: true, summaryCount: 0 })
    if (cmd === 'clear_photo_detail_db') return Promise.resolve({ detailDeleted: 0, printRecordDeleted: 0 })
    if (cmd === 'get_print_records') return Promise.resolve({ rows: [] as unknown[], totalPrintSheets: 0, totalTestSheets: 0 })
    if (cmd === 'seed_fake_data') return Promise.resolve({ inserted: 0 })
    if (cmd === 'shutdown') return Promise.resolve({})
    return Promise.resolve({})
  }
  const chrome = (win as unknown as { chrome: { webview: { postMessage: (msg: string) => void; addEventListener: (type: string, handler: (ev: { data: string }) => void) => void; removeEventListener: (type: string, handler: (ev: { data: string }) => void) => void } } }).chrome
  const id = crypto.randomUUID()
  const req = { id, cmd, data }
  return new Promise((resolve, reject) => {
    const handler = (ev: { data: string }) => {
      try {
        const res = JSON.parse(ev.data) as { id?: string; ok?: boolean; data?: Record<string, unknown>; error?: string }
        if (!res.id || res.id !== id) return
        chrome.webview.removeEventListener('message', handler)
        res.ok ? resolve(res.data ?? {}) : reject(res.error)
      } catch {
        // ignore
      }
    }
    chrome.webview.addEventListener('message', handler)
    chrome.webview.postMessage(JSON.stringify(req))
    setTimeout(() => reject(new Error('timeout')), 60000)
  })
}
