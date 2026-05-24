import { execSync } from "node:child_process";

const defaultPorts = [5100, 5173];

export function stopE2eServers(ports: number[] = defaultPorts) {
  if (process.env.PLAYWRIGHT_SKIP_STOP_SERVERS === "true") {
    return;
  }

  for (const port of ports) {
    stopListenersOnPort(port);
  }
}

function stopListenersOnPort(port: number) {
  const pids = findListeningPids(port);
  for (const pid of pids) {
    killProcess(pid);
  }
}

function findListeningPids(port: number) {
  try {
    if (process.platform === "win32") {
      const output = execSync(`netstat -ano | findstr :${port}`, {
        encoding: "utf8",
        stdio: ["ignore", "pipe", "ignore"],
      });

      const pids = new Set<string>();
      for (const line of output.split(/\r?\n/u)) {
        if (!/LISTENING/u.test(line)) {
          continue;
        }

        const parts = line.trim().split(/\s+/u);
        const pid = parts[parts.length - 1];
        if (pid && pid !== "0") {
          pids.add(pid);
        }
      }

      return [...pids];
    }

    const output = execSync(`lsof -ti tcp:${port}`, {
      encoding: "utf8",
      stdio: ["ignore", "pipe", "ignore"],
    }).trim();

    return output ? output.split(/\s+/u) : [];
  } catch {
    return [];
  }
}

function killProcess(pid: string) {
  try {
    if (process.platform === "win32") {
      execSync(`taskkill /PID ${pid} /F`, { stdio: "ignore" });
      return;
    }

    execSync(`kill -9 ${pid}`, { stdio: "ignore" });
  } catch {
    // Process may have already exited.
  }
}
