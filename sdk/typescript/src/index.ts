export type ContextMemoryHeadersInput = {
  apiKey: string;
  appId?: string;
  userId?: string;
  sessionId?: string;
  extra?: Record<string, string>;
};

/** Build auth/tenant headers for ContextMemory (self-host or Cloud). */
export function headers(input: ContextMemoryHeadersInput): Record<string, string> {
  const h: Record<string, string> = {
    Authorization: `Bearer ${input.apiKey}`,
    "Content-Type": "application/json",
  };
  if (input.appId) h["X-App-Id"] = input.appId;
  if (input.userId) h["X-User-Id"] = input.userId;
  if (input.sessionId) h["X-Session-Id"] = input.sessionId;
  if (input.extra) Object.assign(h, input.extra);
  return h;
}

export type OpenAIClientOptionsInput = ContextMemoryHeadersInput & {
  baseUrl: string;
};

/** Options compatible with the official OpenAI JS client `defaultHeaders` / `baseURL`. */
export function openAIClientOptions(input: OpenAIClientOptionsInput) {
  return {
    baseURL: input.baseUrl.replace(/\/$/, ""),
    apiKey: input.apiKey,
    defaultHeaders: headers(input),
  };
}
