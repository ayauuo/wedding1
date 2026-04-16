<script setup lang="ts">
defineOptions({ name: 'ScreenUploading' })
</script>

<template>
  <div class="screen screen--uploading" role="region" aria-label="照片上傳中">
    <div class="uploading-content">
      <!-- uploadGif.gif 暫停顯示
      <img
        class="uploading-gif"
        src="/assets/templates/uploadPage/uploadGif.gif"
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

.screen--uploading {
  display: none;
  min-height: 100vh;
  background-image: url('#{$path-templates}/uploadPage/background.png');
  background-repeat: no-repeat;
  background-position: center center;
  background-size: cover;

  &.active {
    display: flex !important;
    @include flex-center;
  }
}

.uploading-content {
  @include flex-column;
  @include flex-center;
  gap: $spacing-lg;
}

/* uploadGif.gif 暫停顯示
.uploading-gif {
  max-width: min(90vw, 480px);
  max-height: min(80vh, 480px);
  width: auto;
  height: auto;
  object-fit: contain;
  display: block;
}
*/
</style>
