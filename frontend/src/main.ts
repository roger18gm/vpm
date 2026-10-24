import { createApp } from "vue";
import { createPinia } from "pinia";
import posthog from "posthog-js";
import App from "./App.vue";
import { installSentry } from "./lib/sentry";
import router from "./router";
import "./styles.css";

const posthogProjectToken = import.meta.env.VITE_POSTHOG_PROJECT_TOKEN;
const posthogHost = import.meta.env.VITE_POSTHOG_HOST;

if (!posthogProjectToken) {
  if (import.meta.env.DEV) {
    throw new Error(
      "VITE_POSTHOG_PROJECT_TOKEN variable required by PostHog is missing or un-configured, this causes events to be silently missed. This error stops appearing once VITE_POSTHOG_PROJECT_TOKEN is configured",
    );
  }
} else if (!posthogHost) {
  if (import.meta.env.DEV) {
    throw new Error(
      "VITE_POSTHOG_HOST variable required by PostHog is missing or un-configured, this causes events to be silently missed. This error stops appearing once VITE_POSTHOG_HOST is configured",
    );
  }
} else {
  posthog.init(posthogProjectToken, {
    api_host: posthogHost,
    logs: {
      serviceName: "visionpaint-frontend",
      environment: import.meta.env.MODE,
    },
  });
}

const app = createApp(App);
installSentry(app, router);

const existingErrorHandler = app.config.errorHandler;
app.config.errorHandler = (error, instance, info) => {
  existingErrorHandler?.(error, instance, info);

  if (posthogProjectToken && posthogHost) {
    posthog.captureException(error);
  }
};

app.use(createPinia());
app.use(router);
app.mount("#app");
