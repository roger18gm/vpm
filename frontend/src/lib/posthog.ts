import posthog from "posthog-js";

type EventProperties = Record<string, string | number | boolean>;

const isPostHogConfigured = Boolean(
  import.meta.env.VITE_POSTHOG_PROJECT_TOKEN && import.meta.env.VITE_POSTHOG_HOST,
);

export function capturePostHogEvent(event: string, properties?: EventProperties) {
  if (!isPostHogConfigured) {
    return;
  }

  posthog.capture(event, properties);
}
