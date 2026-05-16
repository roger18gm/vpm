<template>
  <section class="bg-surface border border-border rounded-lg p-4 md:p-5">
    <h2 v-if="$slots.title" class="text-sm font-semibold text-muted mb-3">
      <slot name="title" />
    </h2>
    <slot />
  </section>
</template>
