<script setup lang="ts">
import { RouterView } from "vue-router";
import BottomNav from "@/components/nav/BottomNav.vue";
import SidebarNav from "@/components/nav/SidebarNav.vue";
import ToastHost from "@/components/ui/ToastHost.vue";
import { useAuthStore } from "@/stores/auth";

const auth = useAuthStore();
</script>

<template>
  <div class="min-h-screen bg-page">
    <div class="md:flex">
      <SidebarNav v-if="auth.isManager" />
      <div class="flex-1 max-w-5xl mx-auto w-full p-4 md:p-6 pb-24 md:pb-6">
        <RouterView />
      </div>
    </div>
    <BottomNav />
    <ToastHost />
  </div>
</template>
