<script setup lang="ts">
defineOptions({ name: 'ScreenProcessing' })
</script>

<template>
  <div class="screen screen--processing" role="region" aria-label="列印中">
    <div class="processing-content">
      <!-- printGif.gif 暫停顯示
      <img
        class="processing-gif"
        src="/assets/templates/printPage/printGif.gif"
        alt=""
        aria-hidden="true"
      />
      -->
    </div>
  </div>
</template>

<style lang="scss" scoped>
@use '@/styles/variables' as *;
@use '@/styles/mixins' as *;

.screen--processing {
  display: none;
  min-height: 100vh;
  background-image: url('#{$path-templates}/printPage/background.png');
  background-repeat: no-repeat;
  background-position: center center;
  background-size: cover;

  &.active {
    display: flex !important;
    @include flex-center;
  }
}

.processing-content {
  @include flex-column;
  @include flex-center;
  gap: $spacing-lg;
}

/* printGif.gif 暫停顯示
.processing-gif {
  max-width: min(90vw, 480px);
  max-height: min(80vh, 480px);
  width: auto;
  height: auto;
  object-fit: contain;
  display: block;
}
*/
</style>
