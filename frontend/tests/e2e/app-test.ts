import { test as base, expect } from "@playwright/test";
import { resetE2eData } from "./prepare-database";

export const test = base.extend({
  page: async ({ page }, use) => {
    await resetE2eData();
    await use(page);
  },
});

export { expect };
