import { createApp } from "vue";
import { createPinia } from "pinia";
import App from "./App.vue";
import { installSentry } from "./lib/sentry";
import router from "./router";
import "./styles.css";

const app = createApp(App);
installSentry(app, router);
app.use(createPinia());
app.use(router);
app.mount("#app");
