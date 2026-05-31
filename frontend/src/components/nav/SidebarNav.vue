<script setup lang="ts">
import { computed } from "vue";
import { RouterLink, useRoute } from "vue-router";
import { Icon } from "@iconify/vue";
import { useAuthStore } from "@/stores/auth";

const auth = useAuthStore();
const route = useRoute();

const links = computed(() => {
  const base = [
    { to: "/dashboard", label: "Dashboard", icon: "mdi:view-dashboard-outline" },
    { to: "/jobs", label: "Jobs", icon: "mdi:clipboard-list-outline" },
    { to: "/clock", label: "Clock", icon: "mdi:clock-outline" },
  ];
  if (auth.isAdmin) {
    base.push({ to: "/users", label: "Users", icon: "mdi:account-group-outline" });
  }
  base.push({ to: "/account", label: "Account", icon: "mdi:account-outline" });
  return base;
});

function isActive(to: string) {
  return route.path === to || (to !== "/dashboard" && route.path.startsWith(to));
}
</script>

<template>
  <aside class="hidden md:block w-52 bg-surface border-r border-border p-4 shrink-0 min-h-screen">
    <p class="text-xs uppercase text-primary font-semibold mb-4">VisionPaint</p>
    <nav class="space-y-1 text-sm">
      <RouterLink
        v-for="link in links"
        :key="link.to"
        :to="link.to"
        class="flex items-center gap-2 px-3 py-2 rounded-md"
        :class="isActive(link.to) ? 'bg-primary/10 text-primary font-semibold' : 'text-text hover:bg-page'"
      >
        <Icon :icon="link.icon" class="text-lg" />
        {{ link.label }}
      </RouterLink>
    </nav>
  </aside>
</template>
