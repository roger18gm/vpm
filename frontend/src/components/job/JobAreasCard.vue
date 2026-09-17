<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { RouterLink } from "vue-router";
import VpCard from "@/components/ui/VpCard.vue";
import { useJobAreasStore } from "@/stores/jobAreas";

const props = defineProps<{ jobId: number }>();
const areasStore = useJobAreasStore();
const loading = ref(true);

const areas = computed(() => areasStore.list(props.jobId));
const completedCount = computed(() => areas.value.filter((area) => area.status === "completed").length);

onMounted(async () => {
  try {
    await areasStore.fetchAreas(props.jobId);
  } finally {
    loading.value = false;
  }
});
</script>

<template>
  <VpCard>
    <template #title>Areas</template>
    <p v-if="loading" class="text-sm text-muted">Loading…</p>
    <p v-else-if="areas.length" class="text-sm mb-3">
      {{ completedCount }} of {{ areas.length }} completed
    </p>
    <p v-else class="text-sm text-muted mb-3">No areas yet</p>
    <RouterLink
      :to="{ name: 'job-areas', params: { id: jobId } }"
      class="text-sm text-primary font-semibold"
    >
      View areas
    </RouterLink>
  </VpCard>
</template>
