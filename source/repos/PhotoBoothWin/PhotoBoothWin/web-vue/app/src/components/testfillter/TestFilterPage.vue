<script setup lang="ts">
import { reactive, computed, ref } from 'vue'

defineOptions({ name: 'TestFilterPage' })

const emit = defineEmits<{
  (e: 'close'): void
}>()

const imageUrl = ref('/assets/templates/test/S__29212675.jpg')
const fallbackUrl = '/assets/templates/test/test0.jpg'

const filters = reactive({
  brightness: 1,
  contrast: 1,
  saturate: 1,
  hueRotate: 0,
  grayscale: 0,
  sepia: 0,
  invert: 0,
  opacity: 1,
  blur: 0,
})

const shadow = reactive({
  x: 0,
  y: 0,
  blur: 0,
  color: '#000000',
  alpha: 0,
})

const shadowRgba = computed(() => {
  const hex = shadow.color.replace('#', '')
  const r = parseInt(hex.slice(0, 2), 16) || 0
  const g = parseInt(hex.slice(2, 4), 16) || 0
  const b = parseInt(hex.slice(4, 6), 16) || 0
  const a = Math.min(1, Math.max(0, shadow.alpha))
  return `rgba(${r}, ${g}, ${b}, ${a})`
})

const filterCss = computed(() => {
  return [
    `brightness(${filters.brightness})`,
    `contrast(${filters.contrast})`,
    `saturate(${filters.saturate})`,
    `hue-rotate(${filters.hueRotate}deg)`,
    `grayscale(${filters.grayscale})`,
    `sepia(${filters.sepia})`,
    `invert(${filters.invert})`,
    `opacity(${filters.opacity})`,
    `blur(${filters.blur}px)`,
    `drop-shadow(${shadow.x}px ${shadow.y}px ${shadow.blur}px ${shadowRgba.value})`,
  ].join(' ')
})

const imageStyle = computed(() => ({
  filter: filterCss.value,
}))

function resetAll() {
  filters.brightness = 1
  filters.contrast = 1
  filters.saturate = 1
  filters.hueRotate = 0
  filters.grayscale = 0
  filters.sepia = 0
  filters.invert = 0
  filters.opacity = 1
  filters.blur = 0
  shadow.x = 0
  shadow.y = 0
  shadow.blur = 0
  shadow.color = '#000000'
  shadow.alpha = 0
}

function onImgError() {
  imageUrl.value = fallbackUrl
}
</script>

<template>
  <div class="screen screen--test-filter" role="region" aria-label="測試濾鏡頁">
    <div class="test-filter__header">
      <h1>測試濾鏡</h1>
      <div class="test-filter__actions">
        <button type="button" class="btn" @click="resetAll">重置參數</button>
        <button type="button" class="btn primary" @click="emit('close')">返回待機</button>
      </div>
    </div>
    <div class="test-filter__content">
      <div class="test-filter__preview">
        <img
          class="test-filter__image"
          :src="imageUrl"
          :style="imageStyle"
          alt="濾鏡測試圖"
          @error="onImgError"
        />
      </div>
      <div class="test-filter__controls">
        <div class="control">
          <label>亮度 brightness：{{ filters.brightness.toFixed(2) }}</label>
          <input v-model.number="filters.brightness" type="range" min="0" max="2" step="0.01" />
        </div>
        <div class="control">
          <label>對比 contrast：{{ filters.contrast.toFixed(2) }}</label>
          <input v-model.number="filters.contrast" type="range" min="0" max="3" step="0.01" />
        </div>
        <div class="control">
          <label>飽和 saturate：{{ filters.saturate.toFixed(2) }}</label>
          <input v-model.number="filters.saturate" type="range" min="0" max="3" step="0.01" />
        </div>
        <div class="control">
          <label>色相 hue-rotate：{{ filters.hueRotate }}deg</label>
          <input v-model.number="filters.hueRotate" type="range" min="0" max="360" step="1" />
        </div>
        <div class="control">
          <label>灰階 grayscale：{{ filters.grayscale.toFixed(2) }}</label>
          <input v-model.number="filters.grayscale" type="range" min="0" max="1" step="0.01" />
        </div>
        <div class="control">
          <label>復古 sepia：{{ filters.sepia.toFixed(2) }}</label>
          <input v-model.number="filters.sepia" type="range" min="0" max="1" step="0.01" />
        </div>
        <div class="control">
          <label>反相 invert：{{ filters.invert.toFixed(2) }}</label>
          <input v-model.number="filters.invert" type="range" min="0" max="1" step="0.01" />
        </div>
        <div class="control">
          <label>透明 opacity：{{ filters.opacity.toFixed(2) }}</label>
          <input v-model.number="filters.opacity" type="range" min="0" max="1" step="0.01" />
        </div>
        <div class="control">
          <label>模糊 blur：{{ filters.blur.toFixed(1) }}px</label>
          <input v-model.number="filters.blur" type="range" min="0" max="10" step="0.1" />
        </div>
        <div class="control-group">
          <label>陰影 drop-shadow</label>
          <div class="control-inline">
            <span>X</span>
            <input v-model.number="shadow.x" type="range" min="-20" max="20" step="1" />
            <span>{{ shadow.x }}px</span>
          </div>
          <div class="control-inline">
            <span>Y</span>
            <input v-model.number="shadow.y" type="range" min="-20" max="20" step="1" />
            <span>{{ shadow.y }}px</span>
          </div>
          <div class="control-inline">
            <span>模糊</span>
            <input v-model.number="shadow.blur" type="range" min="0" max="30" step="1" />
            <span>{{ shadow.blur }}px</span>
          </div>
          <div class="control-inline">
            <span>顏色</span>
            <input v-model="shadow.color" type="color" />
            <span>透明度 {{ shadow.alpha.toFixed(2) }}</span>
            <input v-model.number="shadow.alpha" type="range" min="0" max="1" step="0.01" />
          </div>
        </div>
        <div class="control">
          <label>目前 filter 字串</label>
          <textarea class="filter-output" readonly :value="filterCss" />
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" scoped>
@use '@/styles/mixins' as *;

.screen--test-filter {
  position: absolute;
  inset: 0;
  background: #111;
  color: #fff;
  padding: 24px 32px;
}

.test-filter__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;

  h1 {
    margin: 0;
    font-size: 40px;
  }
}

.test-filter__actions {
  display: flex;
  gap: 12px;

  .btn {
    padding: 10px 18px;
    font-size: 16px;
  }
}

.test-filter__content {
  display: grid;
  grid-template-columns: 1.1fr 1fr;
  gap: 24px;
  margin-top: 24px;
  height: calc(100% - 80px);
}

.test-filter__preview {
  @include flex-center;
  background: #000;
  border-radius: 12px;
  padding: 16px;
}

.test-filter__image {
  max-width: 100%;
  max-height: 100%;
  border-radius: 12px;
}

.test-filter__controls {
  overflow-y: auto;
  padding-right: 8px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.control,
.control-group {
  background: #1c1c1c;
  border-radius: 10px;
  padding: 12px 16px;
  display: flex;
  flex-direction: column;
  gap: 8px;

  label {
    font-size: 14px;
    color: #ccc;
  }
}

.control-inline {
  display: grid;
  grid-template-columns: 40px 1fr 70px;
  align-items: center;
  gap: 8px;
  font-size: 12px;
  color: #bbb;

  input[type='color'] {
    width: 42px;
    height: 28px;
    border: none;
    background: transparent;
    padding: 0;
  }
}

.filter-output {
  width: 100%;
  min-height: 120px;
  background: #0e0e0e;
  color: #9ef8b5;
  border: 1px solid #333;
  border-radius: 8px;
  padding: 8px;
  font-size: 12px;
  line-height: 1.4;
  resize: vertical;
}
</style>
