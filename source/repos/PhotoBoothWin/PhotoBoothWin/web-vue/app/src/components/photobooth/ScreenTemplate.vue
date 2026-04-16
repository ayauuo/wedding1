<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import type { Template } from '@/types/photobooth'
import { usePhotobooth } from '@/composables/usePhotobooth'
import { unlockCountdownAudio } from '@/composables/useTakePicture'

const { templates, selectedTemplate, selectTemplate, showScreen } = usePhotobooth()

const msgboxVisible = ref(false)
const templateListRef = ref<HTMLElement | null>(null)
/** 橫向版型列的捲動容器；需用非 passive 的 wheel 才能把垂直滾輪轉成左右捲動 */
const templateScrollRef = ref<HTMLElement | null>(null)
const hasSelection = computed(() => !!selectedTemplate.value)

const orderedTemplates = computed(() => {
  const order = ['bk03', 'bk04', 'bk01', 'bk02']
  const byId = new Map(templates.value.map((t) => [t.id, t] as const))
  const ordered = order.map((id) => byId.get(id)).filter((t): t is Template => !!t)
  const rest = templates.value.filter((t) => !order.includes(t.id))
  return [...ordered, ...rest]
})

function getTemplateCardClass(index: number) {
  if (index === 0) return 'screen-template__card--tall-1'
  if (index === 1) return 'screen-template__card--tall-2'
  if (index === 2) return 'screen-template__card--wide-top'
  if (index === 3) return 'screen-template__card--wide-bottom'
  return ''
}

function onCardClick(t: Template) {
  if (selectedTemplate.value?.id === t.id) {
    msgboxVisible.value = true
    return
  }
  selectTemplate(t)
}

function confirmTemplate() {
  // #region agent log
  fetch('http://127.0.0.1:7242/ingest/60461173-9774-483b-a750-822bb1590c42', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ location: 'ScreenTemplate.vue:confirmTemplate', message: 'confirmTemplate_called', data: { hasSelection: !!selectedTemplate.value }, timestamp: Date.now(), sessionId: 'debug-session', hypothesisId: 'H1' }) }).catch(() => {})
  // #endregion
  unlockCountdownAudio()
  msgboxVisible.value = false
  // 立即切到拍照頁；若仍用 nextTick 導致未切換，改同步呼叫確保進入拍攝畫面
  showScreen('shoot')
}

function repeatChoose() {
  msgboxVisible.value = false
}

watch(selectedTemplate, (v) => {
  if (!v) msgboxVisible.value = false
})

function onTemplateScrollWheel(e: WheelEvent) {
  const el = templateScrollRef.value
  if (!el || el.scrollWidth <= el.clientWidth) return
  const horizontal = Math.abs(e.deltaX) > Math.abs(e.deltaY) ? e.deltaX : e.deltaY
  if (horizontal === 0) return
  el.scrollLeft += horizontal
  e.preventDefault()
}

let templateScrollEl: HTMLElement | null = null
onMounted(() => {
  templateScrollEl = templateScrollRef.value
  templateScrollEl?.addEventListener('wheel', onTemplateScrollWheel, { passive: false })
})

onUnmounted(() => {
  templateScrollEl?.removeEventListener('wheel', onTemplateScrollWheel)
  templateScrollEl = null
})

// 相機改由 ScreenShoot.vue 進入拍照頁時才啟動，不在此預熱
</script>

<template>
  <div class="screen screen--template" role="region" aria-label="選版型畫面">
    <!-- 標題在捲動區外，橫向捲動版型列時不會跟著位移，永遠對齊畫面水平中央 -->
    <h1 class="screen-template__title">選擇相框版型</h1>
    <div ref="templateScrollRef" class="screen-template__scroll">
      <!-- <button
        v-show="hasSelection"
        type="button"
        class="screen-template__start-btn"
        @click="confirmTemplate"
      >
        開始拍照
      </button> -->
      <div class="screen-template__row-wrap">
        <div
          ref="templateListRef"
          class="screen-template__grid"
          :class="{ 'has-selection': hasSelection }"
        >
          <button
            v-for="(t, index) in orderedTemplates"
            :key="t.id"
            type="button"
            class="screen-template__card"
            :class="[getTemplateCardClass(index), { 'is-selected': selectedTemplate?.id === t.id }]"
            @click="onCardClick(t)"
          >
            <div class="screen-template__card-preview">
              <img
                class="screen-template__card-img"
                :src="t.preview"
                :alt="t.id"
                loading="lazy"
              />
            </div>
          </button>
        </div>
      </div>
    </div>
    <div
      class="screen-template__msgbox"
      :class="{ 'screen-template__msgbox--hidden': !msgboxVisible }"
      role="dialog"
      aria-modal="true"
      aria-label="確認版型"
    >
      <div class="screen-template__msgbox-backdrop" @click="msgboxVisible = false" />
      <div class="screen-template__msgbox-window">
        <img
          class="screen-template__msgbox-window-bg"
          src="/assets/templates/chooselayout/msgbox/window.png"
          alt=""
        />
        <div class="screen-template__msgbox-btns">
          <button
            type="button"
            class="screen-template__msgbox-btn screen-template__msgbox-btn--confirm"
            aria-label="確認"
            @click="confirmTemplate"
          >
            <img src="/assets/templates/chooselayout/msgbox/confirm.png" alt="確認" />
          </button>
          <button
            type="button"
            class="screen-template__msgbox-btn screen-template__msgbox-btn--repeat"
            aria-label="重選"
            @click="repeatChoose"
          >
            <img src="/assets/templates/chooselayout/msgbox/repeat.png" alt="重選" />
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" scoped>
@use '@/styles/variables' as *;

.screen--template {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  padding: 0;
  background: #fff;
  background-image: url('#{$path-templates}/chooselayout/background.png');
  background-repeat: no-repeat;
  background-position: center bottom;
  background-size: cover;
}

.screen-template__scroll {
  flex: 1;
  overflow: hidden;
  padding: calc(120px + env(safe-area-inset-top, 0px)) 48px max(48px, env(safe-area-inset-bottom, 0px))
    48px;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  width: 100%;
  min-width: 0;
  min-height: 0;
  position: relative;
  z-index: 1;
}

/* 橫向捲動時整排仍可置中（較窄視窗）；內容較寬時由外層 overflow-x 捲動 */
.screen-template__row-wrap {
  display: flex;
  justify-content: center;
  width: 100%;
  min-width: 0;
}

.screen-template__title {
  position: absolute;
  top: calc(12px + env(safe-area-inset-top, 0px));
  left: 0;
  right: 0;
  width: 100%;
  margin: 0;
  padding: 0 max(16px, env(safe-area-inset-left, 0px)) 0 max(16px, env(safe-area-inset-right, 0px));
  box-sizing: border-box;
  font-size: 72px;
  font-weight: bold;
  color: rgb(255, 255, 255);
  text-align: center;
  line-height: 1.2;
  letter-spacing: 10px;
  /* 大字級用 2px 黑邊，與其他頁面提示一致 */
  text-shadow:
    -2px -2px 0 #000,
    2px -2px 0 #000,
    -2px 2px 0 #000,
    2px 2px 0 #000,
    0 -2px 0 #000,
    0 2px 0 #000,
    -2px 0 0 #000,
    2px 0 0 #000,
    0 4px 16px rgba(0, 0, 0, 0.45);
  z-index: 2;
  /* 不攔截點擊，下方橫向捲動區可正常操作 */
  pointer-events: none;
}

.screen-template__start-btn {
  position: absolute;
  top: 140px;
  left: 50%;
  transform: translateX(-50%);
  z-index: 5;
  padding: 16px 48px;
  font-size: 28px;
  font-weight: bold;
  color: #fff;
  background: var(--accent, #ff4d4f);
  border: none;
  border-radius: 12px;
  cursor: pointer;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.2);

  &:hover {
    opacity: 0.95;
  }
}

.screen-template__grid {
  display: grid;
  grid-template-columns: 440px 440px 640px;
  grid-template-rows: 400px 400px;
  grid-template-areas:
    'tall1 tall2 wide1'
    'tall1 tall2 wide2';
  column-gap: 56px;
  row-gap: 48px;
  align-items: center;
  justify-content: center;
  width: fit-content;
  margin-inline: auto;
  padding: 0;

  &.has-selection :deep(.screen-template__card:not(.is-selected)) {
    transform: scale(0.94);
  }
}

.screen-template__card {
  border: none;
  border-radius: $radius-xl;
  background: transparent;
  padding: 0;
  cursor: pointer;
  transition: transform 0.3s ease;
  z-index: 1;
  align-self: stretch;
  justify-self: stretch;

  &.is-selected {
    outline: 6px solid var(--accent, #ff4d4f);
    transform: scale(1.06);
    z-index: 10;
    position: relative;
  }
}

.screen-template__card--tall-1 {
  grid-area: tall1;
}

.screen-template__card--tall-2 {
  grid-area: tall2;
}

.screen-template__card--wide-top {
  grid-area: wide1;
}

.screen-template__card--wide-bottom {
  grid-area: wide2;
}

.screen-template__card-preview {
  width: 100%;
  height: 100%;
  display: block;
  transition: transform 0.3s ease;
}

.screen-template__card--tall-1 .screen-template__card-preview,
.screen-template__card--tall-2 .screen-template__card-preview {
  width: 440px;
  /* tall card 佔用兩個 row：400 + 400 + row-gap(48) = 848 */
  height: 848px;
}

.screen-template__card--wide-top .screen-template__card-preview,
.screen-template__card--wide-bottom .screen-template__card-preview {
  width: 640px;
  height: 400px;
}

.screen-template__card-img {
  width: 100%;
  height: 100%;
  display: block;
  object-fit: contain;
}

.screen-template__msgbox {
  position: absolute;
  inset: 0;
  z-index: 100;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 20px;

  &.screen-template__msgbox--hidden {
    display: none !important;
  }
}

.screen-template__msgbox-backdrop {
  position: absolute;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
}

.screen-template__msgbox-window {
  position: relative;
  z-index: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 20px;
}

.screen-template__msgbox-window-bg {
  display: block;
  max-width: 100%;
  height: auto;
}

.screen-template__msgbox-btns {
  display: flex;
  gap: $spacing-5xl;
  justify-content: center;
  align-items: center;
  margin-top: -150px;
}

.screen-template__msgbox-btn {
  padding: 0;
  border: none;
  background: transparent;
  cursor: pointer;
  display: block;
  transition: opacity 0.2s;

  img {
    display: block;
    width: auto;
    height: auto;
    max-height: 80px;
  }

  &:hover {
    opacity: 0.9;
  }
}
</style>
