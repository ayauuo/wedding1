/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** 開發時起始頁面：0=待機 1=選版型 2=拍照 3=結果 4=列印中，空則不跳轉 */
  readonly VITE_DEV_START_PAGE?: string
  /** 預設版型索引：0=bk01 1=bk02 2=bk03 3=bk04 */
  readonly VITE_DEFAULT_TEMPLATE_INDEX?: string
  /** 拍照預覽畫面是否使用預設測試圖：1/true 時用 test0.jpg～test3.jpg 合成預覽 */
  readonly VITE_PREVIEW_USE_TEST_IMAGES?: string
  /** 網站靜音：1/true 時不播放倒數音效 */
  readonly VITE_MUTE?: string
  /** 待機畫面輪播：1/true 時啟用輪播 */
  readonly VITE_IDLE_CAROUSEL?: string
  /** 上傳目標網址（主機端上傳圖片／影片時使用） */
  readonly VITE_UPLOAD_URL?: string
  /** 掃描 QR 後顯示相片／影片下載頁的基底網址（不含查詢參數） */
  readonly VITE_DOWNLOAD_PAGE_BASE_URL?: string
  /** 離線模式：1/true 時不上傳、不產生 QR code（需在 buildFinalOutput 內實作） */
  readonly VITE_OFFLINE_MODE?: string
  /** 結果頁是否顯示 QR code：1/true 時顯示；0/false 時隱藏，預設 1 */
  readonly VITE_QRCODE_ENABLED?: string
  /** 合成用座標：每格「寬,高,x,y」逗號串接，與版型張數一致。例：VITE_BK01_SYNTHESIS=544,471,43,244,... */
  readonly VITE_BK01_SYNTHESIS?: string
  readonly VITE_BK02_SYNTHESIS?: string
  readonly VITE_BK03_SYNTHESIS?: string
  readonly VITE_BK04_SYNTHESIS?: string
  /** 測試模式快速倒數：1/true 時拍照改為 2 秒倒數 */
  readonly VITE_TEST_FAST_COUNTDOWN?: string
  /** 立刻拍：1/true 時跳過倒數，啟動後強制立即拍攝 */
  readonly VITE_SHOOT_IMMEDIATE?: string
  /** 專案名稱（用於 daily report CSV 記錄） */
  readonly VITE_PROJECT_NAME?: string
  /** 機器名稱（用於列印紀錄 CSV 與 Google 試算表） */
  readonly VITE_MACHINE_NAME?: string
  /** 是否寫入使用記錄 CSV（1/true 時在非測試模式下寫入 daily report），不設則預設不寫 */
  readonly VITE_LOG_USAGE?: string
  /** 結果畫面無合成圖時是否顯示版型圖當占位（1/true 時顯示 QRcodePage 版型，避免空白） */
  readonly VITE_RESULT_SHOW_TEMPLATE_PLACEHOLDER?: string
  /** 結果頁幾秒沒按列印就自動列印（預設 60） */
  readonly VITE_RESULT_AUTO_PRINT_SEC?: string
  /** 列印中畫面顯示幾秒後回待機並還原（DNP 通常不通知 C#，用此秒數模擬印完；預設 20） */
  readonly VITE_PRINTING_SHOW_SEC?: string
  /** 測試時不實際列印：1/true 時只顯示列印中畫面、不送 DNP，也不寫入列印紀錄 CSV */
  readonly VITE_SKIP_PRINT?: string
  /** 每筆列印紀錄的收款金額（固定值，寫入 CSV 用） */
  readonly VITE_RECEIPT_AMOUNT?: string
  /** 測試時不列印（VITE_SKIP_PRINT=1）時仍寫入列印紀錄 CSV：1/true 時測試也會寫 report，方便確認 */
  readonly VITE_LOG_PRINT_RECORD_WHEN_SKIP?: string
  /** 是否允許雙擊空白鍵開啟開發者測試面板：正式環境設 0 或 false，避免被跳過流程 */
  readonly VITE_ALLOW_DEV_PANEL?: string
  /** 倒數秒數（畫面上顯示 N～1，預設 10） */
  readonly VITE_COUNTDOWN_SECONDS?: string
  /** 1/true：倒數總秒數 = VITE_COUNTDOWN_SECONDS - 5（至少 1）、對焦只一次、音檔改為 倒數5秒拍照.mp3 */
  readonly VITE_COUNTDOWN_MINUS5_MODE?: string
  /** 在哪些秒數時觸發單眼對焦（逗號分隔，預設 10,5；例：10,5 或 10,5,1） */
  readonly VITE_FOCUS_AT_SECONDS?: string
  /** 對焦後 C# 等待鏡頭完成的毫秒數 */
  readonly VITE_FOCUS_WAIT_AFTER_MS?: string
  /** 第一次對焦完成就拍照：1/true 時只對焦一次、立即拍照 */
  readonly VITE_SHOOT_AFTER_FIRST_FOCUS?: string
  /** 只半按快門：1/true 時進入拍照畫面只做半按快門對焦、不連拍 */
  readonly VITE_SHOOT_ONLY_HALF_PRESS?: string
  /** 強制拍攝不等待對焦：1/true 時到時機就拍，不管有無對焦成功 */
  readonly VITE_FORCE_CAPTURE_WITHOUT_AF?: string
  /** 紙鈔機開關：1/true 時啟用；0/false 時不啟動（與投幣器皆關閉時，點擊螢幕即可進入選版型） */
  readonly VITE_BILL_ACCEPTOR_ENABLED?: string
  /** 投幣器開關：1/true 時啟用；0/false 時不啟動 */
  readonly VITE_COIN_ACCEPTOR_ENABLED?: string
}
