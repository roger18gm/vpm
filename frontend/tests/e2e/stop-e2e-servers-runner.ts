import { stopE2eServers } from "./stop-e2e-servers";

stopE2eServers();
console.log("Stopped processes listening on ports 5100 and 5173 (if any).");
