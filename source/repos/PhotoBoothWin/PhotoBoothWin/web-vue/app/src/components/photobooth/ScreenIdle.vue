<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import SecretKeypad from '@/components/photobooth/SecretKeypad.vue'

defineOptions({ name: 'ScreenIdle' })

const props = withDefaults(
  defineProps<{
    /** 是否啟用紙鈔機或投幣器；false 時可點擊螢幕進入選版型 */
    isPaymentsEnabled?: boolean
  }>(),
  { isPaymentsEnabled: true }
)
const emit = defineEmits<{
  clickToStart: []
}>()

const CAROUSEL_INTERVAL_MS = 5000
const basePath = '/assets/templates/IdlePage'
/** 輪播圖（相對 basePath），對應 public/assets/templates/IdlePage/cover/ */
const slideImages = ['cover/cover.png', 'cover/cover_2.png']

const isCarousel = computed(() => {
  const v = import.meta.env.VITE_IDLE_CAROUSEL
  return v === '1' || v === 'true'
})

/** 取得單張圖的 URL（支援以 / 開頭的絕對路徑或相對檔名） */
function getSlideUrl(img: string): string {
  return img.startsWith('/') ? img : `${basePath}/${img}`
}

/** 無輪播時用的靜態背景：用第一張圖 */
const staticBackgroundUrl = computed(() =>
  slideImages[0] ? getSlideUrl(slideImages[0]) : ''
)

const currentIndex = ref(0)
let intervalId: ReturnType<typeof setInterval> | null = null

function startCarousel() {
  if (!isCarousel.value || slideImages.length <= 1) return
  intervalId = setInterval(() => {
    currentIndex.value = (currentIndex.value + 1) % slideImages.length
  }, CAROUSEL_INTERVAL_MS)
}

function stopCarousel() {
  if (intervalId) {
    clearInterval(intervalId)
    intervalId = null
  }
}

onMounted(() => {
  if (isCarousel.value) startCarousel()
})

onUnmounted(() => {
  stopCarousel()
})
</script>

<template>
  <div
    class="screen screen--idle"
    :class="{ 'is-click-to-start': !props.isPaymentsEnabled }"
    role="region"
    aria-label="待機畫面"
    @click="!props.isPaymentsEnabled && emit('clickToStart')"
  >
    <!-- 無輪播：用第一張圖當靜態背景 -->
    <div
      v-if="!isCarousel"
      class="idle-static"
      :style="{
        backgroundImage: `url('${staticBackgroundUrl}')`,
      }"
    />
    <!-- 有輪播：多張輪播（目前同一張圖） -->
    <div v-else class="idle-carousel">
      <div
        v-for="(img, i) in slideImages"
        :key="i"
        class="idle-carousel__slide"
        :class="{ 'is-active': currentIndex === i }"
        :style="{
          backgroundImage: `url('${getSlideUrl(img)}')`,
        }"
      />
    </div>
    <!-- 阻止 SecretKeypad 區域的點擊冒泡，避免點擊管理熱點時誤觸進入選版型 -->
    <div @click.stop>
      <SecretKeypad />
    </div>
  </div>
</template>

<style lang="scss" scoped>
@use '@/styles/variables' as *;
@use '@/styles/mixins' as *;

.screen--idle {
  position: absolute;
  width: 1920px;
  height: 1080px;
  overflow: hidden;

  &.is-click-to-start {
    cursor: pointer;
  }
}

.idle-click-hint {
  position: absolute;
  bottom: 80px;
  left: 0;
  right: 0;
  text-align: center;
  font-size: 48px;
  font-weight: bold;
  color: rgba(255, 255, 255, 0.9);
  text-shadow: 0 2px 8px rgba(0, 0, 0, 0.6);
  pointer-events: none;
}

.idle-static,
.idle-carousel__slide {
  position: absolute;
  inset: 0;
  background-repeat: no-repeat;
  background-size: contain;
  background-position: center;
}

.idle-static {
  width: 100%;
  height: 100%;
}

.idle-carousel {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;

  .idle-carousel__slide {
    opacity: 0;
    transition: opacity 0.8s ease-in-out;
    pointer-events: none;

    &.is-active {
      opacity: 1;
      pointer-events: auto;
    }
  }
}
</style>
