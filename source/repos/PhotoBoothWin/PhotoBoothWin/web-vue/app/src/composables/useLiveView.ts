import { ref } from 'vue'

/** 單例：C# 推送的 EDSDK Live View 幀 dataUrl，供拍照頁顯示 */
const hostLiveViewDataUrl = ref('')
/** 除錯用：已收到的幀數，畫面上可顯示「已收到 N 幀」確認即時串流有進來 */
const liveViewFrameCount = ref(0)

export function useLiveView() {
  function setHostLiveViewDataUrl(url: string) {
    hostLiveViewDataUrl.value = url
    liveViewFrameCount.value += 1
    // #region agent log
    if (liveViewFrameCount.value === 1 || liveViewFrameCount.value % 60 === 0) {
      fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          location: 'useLiveView.ts:setHostLiveViewDataUrl',
          message: 'live_view_frame',
          data: { frameCount: liveViewFrameCount.value, hasUrl: !!url },
          timestamp: Date.now(),
          sessionId: 'debug-session',
          runId: 'run1',
          hypothesisId: 'H3',
        }),
      }).catch(() => {})
    }
    // #endregion
  }

  function clearHostLiveViewDataUrl(reason?: string) {
    hostLiveViewDataUrl.value = ''
    // #region agent log
    fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        location: 'useLiveView.ts:clearHostLiveViewDataUrl',
        message: 'clear_live_view',
        data: { reason: reason ?? '' },
        timestamp: Date.now(),
        sessionId: 'debug-session',
        runId: 'run1',
        hypothesisId: 'H1',
      }),
    }).catch(() => {})
    // #endregion
  }

  return {
    hostLiveViewDataUrl,
    liveViewFrameCount,
    setHostLiveViewDataUrl,
    clearHostLiveViewDataUrl,
  }
}
