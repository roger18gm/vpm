<script setup lang="ts">
import { onMounted, onUnmounted, ref } from "vue";
import VpLoadingAnimation from "./VpLoadingAnimation.vue";

const props = withDefaults(
  defineProps<{
    messages?: string[];
    intervalMs?: number;
  }>(),
  {
    messages: () => ["Loading…", "Getting things ready…", "Almost there…"],
    intervalMs: 2400,
  }
);

const messageIndex = ref(0);
let timer: ReturnType<typeof setInterval> | null = null;

onMounted(() => {
  if (props.messages.length <= 1) return;
  timer = setInterval(() => {
    messageIndex.value = (messageIndex.value + 1) % props.messages.length;
  }, props.intervalMs);
});

onUnmounted(() => {
  if (timer) clearInterval(timer);
});
</script>

<template>
  <div
    class="min-h-screen bg-page flex flex-col items-center justify-center gap-5 px-6"
    role="status"
    aria-live="polite"
    aria-busy="true"
  >
    <VpLoadingAnimation />
    <p class="text-sm text-muted text-center min-h-[1.25rem]">
      <Transition name="vp-loading-fade" mode="out-in">
        <span :key="messageIndex">{{ messages[messageIndex] }}</span>
      </Transition>
    </p>
  </div>
</template>

<style scoped>
.vp-loading-fade-enter-active,
.vp-loading-fade-leave-active {
  transition: opacity 0.35s ease;
}

.vp-loading-fade-enter-from,
.vp-loading-fade-leave-to {
  opacity: 0;
}
</style>
