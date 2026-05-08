import { type CSSProperties, type ReactNode, FormEvent, useMemo, useState } from "react";
import { useAuth } from "./hooks/useAuth";
import { JobInput, useJobs } from "./hooks/useJobs";

function App() {
  const auth = useAuth();

  if (auth.loading) {
    return <Shell><Panel title="Loading">Checking your session.</Panel></Shell>;
  }

  if (!auth.status?.isAuthenticated) {
    return (
      <Shell>
        <AuthScreen
          canBootstrap={auth.status?.canBootstrap ?? false}
          error={auth.error}
          onLogin={auth.login}
          onBootstrap={auth.bootstrap}
        />
      </Shell>
    );
  }

  return (
    <Shell>
      <Dashboard user={auth.status.user} onLogout={auth.logout} />
    </Shell>
  );
}

function Dashboard({ user, onLogout }: { user: NonNullable<ReturnType<typeof useAuth>["status"]>["user"]; onLogout: () => Promise<void> }) {
  const jobs = useJobs();
  const [title, setTitle] = useState("");
  const [priority, setPriority] = useState<"low" | "normal" | "high" | "urgent">("normal");
  const [message, setMessage] = useState<string | null>(null);

  const orderedJobs = useMemo(() => jobs.jobs, [jobs.jobs]);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setMessage(null);

    try {
      await jobs.createJob({
        title,
        priority,
        status: "scheduled",
      } satisfies JobInput);
      setTitle("");
      setPriority("normal");
      setMessage("Job created.");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Unable to create job.");
    }
  }

  return (
    <div style={styles.page}>
      <header style={styles.header}>
        <div>
          <div style={styles.kicker}>VisionPaint</div>
          <h1 style={styles.h1}>Jobs</h1>
        </div>
        <button style={styles.secondaryButton} onClick={() => void onLogout()}>
          Sign out
        </button>
      </header>

      <section style={styles.panel}>
        <div style={styles.row}>
          <div>
            <div style={styles.label}>Signed in as</div>
            <div style={styles.value}>{user?.personName}</div>
          </div>
          <div>
            <div style={styles.label}>Role</div>
            <div style={styles.value}>{user?.companyRole}</div>
          </div>
        </div>
      </section>

      <section style={styles.grid}>
        <Panel title="Create job">
          <form style={styles.form} onSubmit={handleSubmit}>
            <input style={styles.input} value={title} onChange={(event) => setTitle(event.target.value)} placeholder="Job title" required />
            <select style={styles.input} value={priority} onChange={(event) => setPriority(event.target.value as typeof priority)}>
              <option value="low">Low</option>
              <option value="normal">Normal</option>
              <option value="high">High</option>
              <option value="urgent">Urgent</option>
            </select>
            <button style={styles.primaryButton} type="submit">Create</button>
          </form>
          {message ? <p style={styles.helper}>{message}</p> : null}
          {jobs.error ? <p style={styles.error}>{jobs.error}</p> : null}
        </Panel>

        <Panel title={`Jobs (${orderedJobs.length})`}>
          {jobs.loading ? <p style={styles.helper}>Loading jobs.</p> : null}
          <div style={styles.jobList}>
            {orderedJobs.map((job) => (
              <div key={job.id} style={styles.jobRow}>
                <div>
                  <div style={styles.value}>{job.title}</div>
                  <div style={styles.helper}>{job.status} / {job.priority}</div>
                </div>
                <button style={styles.ghostButton} onClick={() => void jobs.archiveJob(job.id)}>
                  Archive
                </button>
              </div>
            ))}
            {!jobs.loading && orderedJobs.length === 0 ? <p style={styles.helper}>No jobs yet.</p> : null}
          </div>
        </Panel>
      </section>
    </div>
  );
}

function AuthScreen({
  canBootstrap,
  error,
  onLogin,
  onBootstrap,
}: {
  canBootstrap: boolean;
  error: string | null;
  onLogin: (email: string, password: string) => Promise<unknown>;
  onBootstrap: (name: string, email: string, password: string) => Promise<unknown>;
}) {
  const [mode, setMode] = useState(canBootstrap ? "bootstrap" : "login");
  const [name, setName] = useState("VisionPaint Owner");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setMessage(null);

    try {
      if (mode === "bootstrap") {
        await onBootstrap(name, email, password);
      } else {
        await onLogin(email, password);
      }
      setMessage("Signed in.");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Sign in failed.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Panel title={canBootstrap ? "Create the first account" : "Sign in"}>
      <form style={styles.form} onSubmit={handleSubmit}>
        {mode === "bootstrap" ? (
          <input style={styles.input} value={name} onChange={(event) => setName(event.target.value)} placeholder="Your name" required />
        ) : null}
        <input style={styles.input} value={email} onChange={(event) => setEmail(event.target.value)} placeholder="Email" type="email" required />
        <input style={styles.input} value={password} onChange={(event) => setPassword(event.target.value)} placeholder="Password" type="password" required />
        <button style={styles.primaryButton} type="submit" disabled={busy}>
          {busy ? "Working..." : mode === "bootstrap" ? "Create account" : "Sign in"}
        </button>
      </form>
      {canBootstrap ? (
        <button
          style={styles.ghostButton}
          onClick={() => setMode((current) => (current === "bootstrap" ? "login" : "bootstrap"))}
        >
          {mode === "bootstrap" ? "Use sign in" : "First-time setup"}
        </button>
      ) : null}
      {message ? <p style={styles.helper}>{message}</p> : null}
      {error ? <p style={styles.error}>{error}</p> : null}
    </Panel>
  );
}

function Shell({ children }: { children: ReactNode }) {
  return <main style={styles.shell}>{children}</main>;
}

function Panel({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section style={styles.panel}>
      <h2 style={styles.h2}>{title}</h2>
      {children}
    </section>
  );
}

const styles: Record<string, CSSProperties> = {
  shell: {
    minHeight: "100vh",
    background: "#f4f4f5",
    color: "#111827",
    padding: 24,
    fontFamily: 'Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif',
  },
  page: {
    maxWidth: 1120,
    margin: "0 auto",
  },
  header: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
    marginBottom: 24,
  },
  kicker: {
    textTransform: "uppercase",
    letterSpacing: 0,
    fontSize: 12,
    color: "#991b1b",
    marginBottom: 4,
  },
  h1: {
    margin: 0,
    fontSize: 32,
    lineHeight: 1.1,
  },
  h2: {
    margin: "0 0 16px",
    fontSize: 18,
  },
  panel: {
    background: "#ffffff",
    border: "1px solid #e5e7eb",
    borderRadius: 8,
    padding: 20,
  },
  grid: {
    display: "grid",
    gridTemplateColumns: "1fr 1fr",
    gap: 16,
  },
  form: {
    display: "grid",
    gap: 12,
  },
  input: {
    width: "100%",
    border: "1px solid #d1d5db",
    borderRadius: 6,
    padding: "10px 12px",
    fontSize: 14,
    background: "#ffffff",
    color: "#111827",
  },
  primaryButton: {
    border: 0,
    borderRadius: 6,
    padding: "10px 14px",
    background: "#991b1b",
    color: "#ffffff",
    fontWeight: 600,
    cursor: "pointer",
  },
  secondaryButton: {
    border: "1px solid #d1d5db",
    borderRadius: 6,
    padding: "10px 14px",
    background: "#ffffff",
    color: "#111827",
    fontWeight: 600,
    cursor: "pointer",
  },
  ghostButton: {
    border: "1px solid #d1d5db",
    borderRadius: 6,
    padding: "8px 12px",
    background: "#f9fafb",
    color: "#111827",
    cursor: "pointer",
  },
  row: {
    display: "flex",
    justifyContent: "space-between",
    gap: 24,
  },
  label: {
    fontSize: 12,
    color: "#6b7280",
    marginBottom: 4,
  },
  value: {
    fontSize: 15,
    fontWeight: 600,
  },
  helper: {
    fontSize: 13,
    color: "#6b7280",
  },
  error: {
    fontSize: 13,
    color: "#b91c1c",
  },
  jobList: {
    display: "grid",
    gap: 10,
  },
  jobRow: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
    gap: 12,
    padding: 12,
    border: "1px solid #e5e7eb",
    borderRadius: 6,
  },
};

export default App;
