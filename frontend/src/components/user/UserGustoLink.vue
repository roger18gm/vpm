<script setup lang="ts">
import { computed, ref, watch } from "vue";
import VpButton from "@/components/ui/VpButton.vue";
import { useUsersStore } from "@/stores/users";
import type { GustoDirectoryEmployee, GustoDirectoryJob, GustoEmployeeLink, UserAdmin } from "@/types/user";

const props = defineProps<{
  user: UserAdmin;
  employees: GustoDirectoryEmployee[];
  links: GustoEmployeeLink[];
  users: UserAdmin[];
  directoryLoading: boolean;
  directoryError: string | null;
}>();

const emit = defineEmits<{
  saved: [link: GustoEmployeeLink];
}>();

const usersStore = useUsersStore();
const selectedEmployeeUuid = ref("");
const selectedJobUuid = ref("");
const saving = ref(false);
const error = ref("");

const link = computed(() => props.links.find((row) => row.personId === props.user.personId) ?? null);
const selectedEmployee = computed(() =>
  props.employees.find((employee) => employee.uuid === selectedEmployeeUuid.value) ?? null);
const storedMissing = computed(() =>
  Boolean(link.value)
  && props.employees.length > 0
  && !props.employees.some((employee) => employee.uuid === link.value?.gustoEmployeeUuid));

const savedMatches = computed(() =>
  link.value !== null
  && link.value.gustoEmployeeUuid === selectedEmployeeUuid.value
  && link.value.gustoJobUuid === selectedJobUuid.value
  && selectedEmployeeUuid.value !== ""
  && selectedJobUuid.value !== "");

const canSave = computed(() =>
  !saving.value
  && selectedEmployee.value !== null
  && selectedEmployee.value.jobs.some((job) => job.uuid === selectedJobUuid.value)
  && !savedMatches.value);

function linkedName(employeeUuid: string): string | null {
  const other = props.links.find((row) => row.gustoEmployeeUuid === employeeUuid && row.personId !== props.user.personId);
  if (!other) {
    return null;
  }

  return props.users.find((user) => user.personId === other.personId)?.name || "another user";
}

function employeeLabel(employee: GustoDirectoryEmployee): string {
  const name = `${employee.firstName} ${employee.lastName}`.trim();
  const who = name || employee.email || "Gusto employee";
  const email = employee.email && name ? ` (${employee.email})` : "";
  const other = linkedName(employee.uuid);
  return other ? `${who}${email} — linked to ${other}` : `${who}${email}`;
}

function jobLabel(job: GustoDirectoryJob): string {
  const title = job.title || "Compensation job";
  return job.primary ? `${title} (primary)` : title;
}

function applyStoredSelection() {
  const storedEmployee = link.value?.gustoEmployeeUuid ?? "";
  const match = props.employees.some((employee) => employee.uuid === storedEmployee);
  selectedEmployeeUuid.value = match ? storedEmployee : "";
  const jobs = props.employees.find((employee) => employee.uuid === selectedEmployeeUuid.value)?.jobs ?? [];
  const storedJob = match ? (link.value?.gustoJobUuid ?? "") : "";
  if (jobs.some((job) => job.uuid === storedJob)) {
    selectedJobUuid.value = storedJob;
    return;
  }

  selectedJobUuid.value = jobs.length === 1 ? jobs[0].uuid : "";
}

function onEmployeeChange(uuid: string) {
  selectedEmployeeUuid.value = uuid;
  error.value = "";
  const jobs = props.employees.find((employee) => employee.uuid === uuid)?.jobs ?? [];
  if (jobs.length === 1) {
    selectedJobUuid.value = jobs[0].uuid;
    return;
  }

  selectedJobUuid.value = jobs.find((job) => job.primary)?.uuid ?? "";
}

async function save() {
  if (!canSave.value) {
    return;
  }

  saving.value = true;
  error.value = "";
  try {
    const saved = await usersStore.saveGustoLink(props.user.personId, {
      gustoEmployeeUuid: selectedEmployeeUuid.value,
      gustoJobUuid: selectedJobUuid.value,
    });
    emit("saved", saved);
  } catch (err) {
    error.value = err instanceof Error ? err.message : "Unable to link this user to Gusto.";
  } finally {
    saving.value = false;
  }
}

watch(
  () => [props.directoryLoading, link.value?.gustoEmployeeUuid, link.value?.gustoJobUuid, props.employees] as const,
  () => {
    if (props.directoryLoading) {
      return;
    }

    applyStoredSelection();
  },
  { immediate: true },
);
</script>

<template>
  <span v-if="directoryLoading" class="text-muted">Loading Gusto…</span>
  <span v-else-if="directoryError || user.membershipStatus !== 'active'" class="text-muted">—</span>
  <span v-else-if="employees.length === 0 && link" class="text-muted">Linked employee is no longer in Gusto.</span>
  <span v-else-if="employees.length === 0" class="text-muted">No Gusto employees are ready to link.</span>
  <div v-else class="min-w-[240px]">
    <select
      :value="selectedEmployeeUuid"
      :disabled="saving"
      class="w-full border border-border rounded-md px-2 py-2 text-sm min-h-[44px] bg-surface"
      @change="onEmployeeChange(($event.target as HTMLSelectElement).value)"
    >
      <option value="" disabled>
        {{ storedMissing ? "Linked employee is no longer in Gusto" : "Choose a Gusto employee" }}
      </option>
      <option
        v-for="employee in employees"
        :key="employee.uuid"
        :value="employee.uuid"
        :disabled="linkedName(employee.uuid) !== null"
      >
        {{ employeeLabel(employee) }}
      </option>
    </select>

    <p v-if="selectedEmployee && selectedEmployee.jobs.length === 0" class="text-xs text-muted mt-1">
      No compensation job
    </p>
    <p v-else-if="selectedEmployee && selectedEmployee.jobs.length === 1" class="text-xs text-muted mt-1">
      {{ jobLabel(selectedEmployee.jobs[0]) }}
    </p>
    <select
      v-else-if="selectedEmployee && selectedEmployee.jobs.length > 1"
      v-model="selectedJobUuid"
      :disabled="saving"
      class="w-full border border-border rounded-md px-2 py-2 text-sm min-h-[44px] bg-surface mt-2"
    >
      <option value="" disabled>Choose a compensation job</option>
      <option v-for="job in selectedEmployee.jobs" :key="job.uuid" :value="job.uuid">
        {{ jobLabel(job) }}
      </option>
    </select>

    <div class="mt-2 flex items-center gap-2">
      <VpButton variant="secondary" :disabled="!canSave" @click="save">Save</VpButton>
      <span v-if="savedMatches" class="text-xs text-muted">Saved</span>
    </div>
    <p v-if="error" class="text-xs text-error mt-1">{{ error }}</p>
  </div>
</template>
