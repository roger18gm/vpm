<script setup lang="ts">
import { computed } from "vue";
import { RouterLink, useRoute } from "vue-router";
import { Icon } from "@iconify/vue";
import { useAuthStore } from "@/stores/auth";
import { useClockStore } from "@/stores/clock";

const route = useRoute();
const auth = useAuthStore();
const clock = useClockStore();

const items = computed(() => {
  if (auth.isManager) {
    return [
      { name: "dashboard", to: "/dashboard", label: "Dashboard", icon: "mdi:view-dashboard-outline" },
      { name: "jobs", to: "/jobs", label: "Jobs", icon: "mdi:clipboard-list-outline" },
      { name: "clock", to: "/clock", label: "Clock", icon: "mdi:clock-outline" },
      { name: "account", to: "/account", label: "Account", icon: "mdi:account-outline" },
    ];
  }
  return [
    { name: "jobs", to: "/jobs", label: "Jobs", icon: "mdi:clipboard-list-outline" },
    { name: "clock", to: "/clock", label: "Clock", icon: "mdi:clock-outline" },
    { name: "account", to: "/account", label: "Account", icon: "mdi:account-outline" },
  ];
});

function isActive(to: string) {
  return route.path === to || route.path.startsWith(`${to}/`);
}
</script>

<template>
  <nav class="md:hidden fixed bottom-0 left-0 right-0 bg-surface border-t border-border flex z-20">
    <RouterLink
      v-for="item in items"
      :key="item.name"
      :to="item.to"
      class="flex-1 py-3 flex flex-col items-center gap-0.5 text-xs relative"
      :class="isActive(item.to) ? 'text-primary font-semibold border-t-2 border-primary' : 'text-muted'"
    >
      <Icon :icon="item.icon" class="text-xl" />
      {{ item.label }}
      <span
        v-if="item.name === 'clock' && clock.isClockedIn"
        class="absolute top-2 right-1/4 w-2 h-2 bg-primary rounded-full"
      />
    </RouterLink>
  </nav>
</template>
