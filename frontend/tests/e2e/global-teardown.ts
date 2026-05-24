import { stopE2eServers } from "./stop-e2e-servers";

export default async function globalTeardown() {
  stopE2eServers();
}
