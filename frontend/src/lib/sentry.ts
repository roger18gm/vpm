import type { App } from "vue";
import type { Router } from "vue-router";
import * as Sentry from "@sentry/vue";

const sensitiveKey = /password|secret|token|authorization|cookie|api[-_]?key|credential/i;

export function installSentry(app: App, router: Router) {
  const dsn = import.meta.env.VITE_SENTRY_DSN;
  if (!dsn) {
    return;
  }

  Sentry.init({
    app,
    dsn,
    environment: import.meta.env.MODE,
    attachProps: false,
    dataCollection: {
      userInfo: false,
      cookies: false,
      httpHeaders: false,
      httpBodies: [],
      urlQueryParams: false,
      stackFrameVariables: false,
    },
    beforeSend(event) {
      const route = router.currentRoute.value;
      const screen = typeof route.name === "string" && route.name.length > 0 ? route.name : route.path;
      event.tags = { ...event.tags, screen };
      scrubSentryEvent(event);
      return event;
    },
  });
}

export function scrubSentryEvent(event: {
  request?: {
    url?: string;
    cookies?: unknown;
    data?: unknown;
    headers?: Record<string, unknown>;
    query_string?: unknown;
  };
  extra?: Record<string, unknown>;
  contexts?: Record<string, unknown>;
}) {
  if (event.request) {
    event.request.cookies = undefined;
    event.request.data = undefined;
    if (event.request.url) {
      event.request.url = scrubUrl(event.request.url);
    }
    if (event.request.headers) {
      for (const key of Object.keys(event.request.headers)) {
        if (sensitiveKey.test(key)) {
          delete event.request.headers[key];
        }
      }
    }
    if (typeof event.request.query_string === "string") {
      event.request.query_string = scrubQueryString(event.request.query_string);
    }
  }

  scrubRecord(event.extra);
  scrubRecord(event.contexts);
}

function scrubRecord(record: Record<string, unknown> | undefined) {
  if (!record) {
    return;
  }

  for (const key of Object.keys(record)) {
    if (sensitiveKey.test(key)) {
      record[key] = "[Filtered]";
      continue;
    }

    const value = record[key];
    if (value && typeof value === "object" && !Array.isArray(value)) {
      scrubRecord(value as Record<string, unknown>);
    }
  }
}

function scrubUrl(url: string) {
  try {
    const parsed = new URL(url);
    scrubSearchParams(parsed.searchParams);
    return parsed.toString();
  } catch {
    return url;
  }
}

function scrubQueryString(queryString: string) {
  const hadQuestionMark = queryString.startsWith("?");
  const params = new URLSearchParams(hadQuestionMark ? queryString.slice(1) : queryString);
  scrubSearchParams(params);
  const scrubbed = params.toString();
  return hadQuestionMark ? `?${scrubbed}` : scrubbed;
}

function scrubSearchParams(params: URLSearchParams) {
  for (const key of [...params.keys()]) {
    if (sensitiveKey.test(key)) {
      params.set(key, "[Filtered]");
    }
  }
}
