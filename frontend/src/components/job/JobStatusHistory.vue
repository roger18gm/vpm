<script setup lang="ts">
import { onMounted, ref } from "vue";
import { request } from "@/lib/api";
import type { JobStatusHistoryEntry } from "@/types/job";
import { statusLabel } from "@/utils/job";

const props = defineProps<{ jobId: number }>();
const rows = ref<JobStatusHistoryEntry[]>([]);
const loading = ref(true);

function normalize(raw: Record<string, unknown>): JobStatusHistoryEntry {
  return {
    fromStatus: (raw.fromStatus ?? raw.FromStatus ?? null) as string | null,
    toStatus: String(raw.toStatus ?? raw.ToStatus ?? ""),
    changedAt: String(raw.changedAt ?? raw.ChangedAt ?? ""),
    changedByName: String(raw.changedByName ?? raw.ChangedByName ?? "Unknown"),
    reason: (raw.reason ?? raw.Reason ?? null) as string | null,
  };
}

onMounted(async () => {
  try {
    const data = await request<Record<string, unknown>[]>(`/jobs/${props.jobId}/status-history`);
    rows.value = data.map(normalize);
  } finally {
    loading.value = false;
  }
});
</script>

<template>
  <p v-if="loading" class="text-sm text-muted">Loading history…</p>
  <ul v-else-if="rows.length" class="space-y-2">
    <li v-for="(row, index) in rows" :key="index" class="text-sm">
      <span class="font-medium">{{ statusLabel(row.toStatus) }}</span>
      <span v-if="row.fromStatus" class="text-muted"> ← {{ statusLabel(row.fromStatus) }}</span>
      <p class="text-xs text-muted">
        {{ new Date(row.changedAt).toLocaleString() }} · {{ row.changedByName }}
      </p>
    </li>
  </ul>
  <p v-else class="text-sm text-muted">No status changes recorded.</p>
</template>
