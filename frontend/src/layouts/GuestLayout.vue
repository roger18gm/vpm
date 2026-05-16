<script setup lang="ts">
import { RouterView } from "vue-router";
</script>

<template>
  <div class="min-h-screen bg-page flex flex-col">
    <main class="flex-1 flex items-center justify-center p-4">
      <RouterView />
    </main>
  </div>
</template>
