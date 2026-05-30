<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { RouterLink } from "vue-router";
import VpButton from "@/components/ui/VpButton.vue";
import VpCard from "@/components/ui/VpCard.vue";
import { request } from "@/lib/api";
import type { Job } from "@/types/job";
import { useJobsStore } from "@/stores/jobs";

const props = defineProps<{ id: number }>();
const jobsStore = useJobsStore();
const job = ref<Job | null>(null);
const people = ref<{ personId: number; name: string }[]>([]);
const selected = ref<Set<number>>(new Set());
const busy = ref(false);
const error = ref<string | null>(null);

onMounted(async () => {
  try {
    const detail = await jobsStore.fetchJob(props.id);
    job.value = detail;
    selected.value = new Set((detail.assignments ?? []).map((a) => a.personId));
    const roster = await request<{ personId: number; name: string }[]>("/people?role=crew");
    people.value = roster;
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to load crew.";
  }
});

const selectedIds = computed(() => [...selected.value]);

function toggle(personId: number) {
  const next = new Set(selected.value);
  if (next.has(personId)) {
    next.delete(personId);
  } else {
    next.add(personId);
  }
  selected.value = next;
}

async function save() {
  busy.value = true;
  error.value = null;
  try {
    await request(`/jobs/${props.id}/assignments`, {
      method: "PUT",
      body: JSON.stringify({ personIds: selectedIds.value, assignmentRole: "crew" }),
    });
    await jobsStore.fetchJobs();
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to save assignments.";
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <RouterLink :to="{ name: 'job-detail', params: { id } }" class="text-sm text-primary mb-3 inline-block">← Job</RouterLink>
  <h1 class="text-xl font-bold mb-2">Assign crew</h1>
  <p class="text-sm text-muted mb-4">{{ job?.title }}</p>
  <p v-if="error" class="text-sm text-error mb-3">{{ error }}</p>

  <VpCard class="mb-4">
    <p v-if="!people.length" class="text-sm text-muted">No crew members found. Add active company members first.</p>
    <ul v-else class="space-y-2">
      <li v-for="person in people" :key="person.personId">
        <label class="flex items-center gap-3 min-h-[44px] cursor-pointer">
          <input
            type="checkbox"
            class="w-5 h-5"
            :checked="selected.has(person.personId)"
            @change="toggle(person.personId)"
          />
          <span class="text-sm font-medium">{{ person.name }}</span>
        </label>
      </li>
    </ul>
  </VpCard>

  <VpButton block :disabled="busy" @click="save">{{ busy ? "Saving…" : "Save assignments" }}</VpButton>
</template>
