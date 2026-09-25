<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { RouterLink } from "vue-router";
import VpCard from "@/components/ui/VpCard.vue";
import { useJobChecklistStore } from "@/stores/jobChecklist";

const props = defineProps<{ jobId: number }>();
const store = useJobChecklistStore();
const loading = ref(true);
const checklist = computed(() => store.list(props.jobId));
const doneCount = computed(() => checklist.value.items.filter((item) => item.status === "done").length);
const notApplicableCount = computed(() => checklist.value.items.filter((item) => item.status === "not_applicable").length);
const summary = computed(() => {
  const base = `${doneCount.value} of ${checklist.value.items.length} done`;
  return notApplicableCount.value > 0 ? `${base} · ${notApplicableCount.value} not applicable` : base;
});

onMounted(async () => {
  try {
    await store.fetchChecklist(props.jobId);
  } finally {
    loading.value = false;
  }
});
</script>

<template>
  <VpCard>
    <template #title>Checklist</template>
    <p v-if="loading" class="text-sm text-muted">Loading…</p>
    <p v-else-if="checklist.items.length" class="text-sm mb-3">{{ summary }}</p>
    <p v-else class="text-sm text-muted mb-3">No checklist yet</p>
    <RouterLink
      :to="{ name: 'job-checklist', params: { id: jobId } }"
      class="text-sm text-primary font-semibold"
    >
      View checklist
    </RouterLink>
  </VpCard>
</template>
