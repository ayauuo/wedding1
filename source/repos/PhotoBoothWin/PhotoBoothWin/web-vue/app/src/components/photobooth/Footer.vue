<script setup lang="ts">
defineOptions({ name: 'Footer' })
</script>

<template>
  <footer class="app-footer" role="contentinfo">
    <img
      class="app-footer__img"
      src="/assets/templates/footer/footer-copyright.png"
      alt=""
      aria-hidden="true"
    />
  </footer>
</template>

<style lang="scss" scoped>
.app-footer {
  position: fixed;
  bottom: 0;
  left: 0;
  width: 100%;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 100;
}

.app-footer__img {
  max-height: 100%;
  max-width: 100%;
  width: auto;
  height: auto;
  object-fit: contain;
  display: block;
}
</style>
