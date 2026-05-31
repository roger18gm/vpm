<script setup lang="ts">
import loadingSvgMarkup from "./vp-loading-animation.svg?raw";
</script>

<template>
  <div
    class="vp-loading-animation w-44 h-44 sm:w-52 sm:h-52 rounded-2xl shadow-sm border border-border overflow-hidden bg-surface shrink-0"
    v-html="loadingSvgMarkup"
    aria-hidden="true"
  />
</template>

<style scoped>
.vp-loading-animation :deep(svg) {
  display: block;
  width: 100%;
  height: 100%;
}
</style>
