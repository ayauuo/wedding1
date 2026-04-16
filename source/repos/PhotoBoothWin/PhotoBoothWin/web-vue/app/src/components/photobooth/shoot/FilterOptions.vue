<script setup lang="ts">
import { usePhotobooth } from '@/composables/usePhotobooth'
import type { FilterId } from '@/types/photobooth'

defineOptions({ name: 'FilterOptions' })

const { selectedFilter, selectFilter } = usePhotobooth()

const FILTER_OPTIONS: { id: FilterId; label: string }[] = [
  { id: 'baby-pink', label: '粉嫩嬰兒' },
  { id: 'clear-blue', label: '清透藍色' },
  { id: 'vintage-retro', label: '懷舊復古' },
  { id: 'fresh-korean', label: '清新韓系' },
  { id: 'soft-milk-tea', label: '溫柔奶茶' },
  { id: 'neutral-gray', label: '中性灰度' },
]

function onFilterClick(id: FilterId) {
  selectFilter(selectedFilter.value === id ? null : id)
}
</script>

<template>
  <div class="filter-options" role="group" aria-label="選擇濾鏡">
    <div class="filter-options__scroll">
    <button
      v-for="opt in FILTER_OPTIONS"
      :key="opt.id"
      type="button"
      class="filter-options__item"
      :class="{ 'is-selected': selectedFilter === opt.id }"
      @click="onFilterClick(opt.id)"
    >
      {{ opt.label }}
    </button>
    </div>
    <p class="filter-options__hint">
      再次點擊選項即可消除濾鏡
    </p>
  </div>
</template>

<style lang="scss" scoped>
@use '@/styles/variables' as *;

.filter-options {
  display: flex;
  flex-direction: column;
  padding: 0;
  width: 250px;
  max-height: 100vh;
  overflow: hidden;
}

.filter-options__scroll {
  display: flex;
  flex-direction: column;
  gap: 48px;
  padding: 8px 0;
  max-height: 100%;
  overflow-y: auto;
  overflow-x: hidden;
  -webkit-overflow-scrolling: touch;
  scrollbar-width: none;
  -ms-overflow-style: none;

  &::-webkit-scrollbar {
    display: none;
  }
}

.filter-options__hint {
  margin: 16px 0 0;
  padding: 0 4px;
  font-size: 30px;
  line-height: 1.45;
  font-weight: 500;
  color: rgba(255, 255, 255, 0.9);
  text-align: center;
  /* 文字黑邊（描邊）＋輕微陰影 */
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
  flex-shrink: 0;
}

.filter-options__item {
  flex: 0 0 auto;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 250px;
  height: 100px;
  padding: 0 16px;
  font-size: 32px;
  color: #fff;
  text-align: center;
  background: #000;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  box-shadow: 0 2px 6px rgba(0, 0, 0, 0.35);
  transition: background 0.2s, box-shadow 0.2s;

  &:hover {
    background: #1a1a1a;
    box-shadow: 0 3px 8px rgba(0, 0, 0, 0.45);
  }

  &.is-selected {
    background: #1a1a1a;
    box-shadow: 0 0 0 2px #ff4d4f;
  }
}
</style>
