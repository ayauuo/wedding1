<script setup lang="ts">
import { computed } from 'vue'
import { usePhotobooth } from '@/composables/usePhotobooth'

const { loading, currentScreen } = usePhotobooth()
const show = computed(() => loading.value && currentScreen.value !== 'uploading')
</script>

<template>
  <div v-show="show" class="loading-overlay" role="status" aria-live="polite">
    <div class="spinner" />
  </div>
</template>

<style lang="scss" scoped>
.loading-overlay {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  background: rgba(0, 0, 0, 0.4);
  z-index: 9999;
  display: flex;
  align-items: center;
  justify-content: center;
}

.spinner {
  width: 50px;
  height: 50px;
  border: 5px solid rgba(255, 255, 255, 0.3);
  border-top: 5px solid #fff;
  border-radius: 50%;
  animation: spin 1s linear infinite;
}

@keyframes spin {
  0% { transform: rotate(0deg); }
  100% { transform: rotate(360deg); }
}
</style>
