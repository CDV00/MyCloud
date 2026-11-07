/**
 * HTTP client dùng chung.
 *
 * - Access token chỉ nằm trong bộ nhớ (không localStorage) — XSS không đọc được token đã lưu.
 * - Refresh token nằm trong cookie HttpOnly do Identity đặt; trình duyệt tự gửi khi gọi /auth/refresh.
 * - Gặp 401: refresh một lần (nhiều request đồng thời dùng chung một lần refresh) rồi gửi lại request.
 */

export const AUTH_BASE = '/api/identity/v1/auth';

export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
  errors?: { code: string; message: string }[];
}

export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails;

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? `HTTP ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }
}

export interface TokenResponse {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
}

type SessionListener = (token: string | null) => void;

let accessToken: string | null = null;
let refreshInFlight: Promise<string | null> | null = null;
const listeners = new Set<SessionListener>();

export function getAccessToken(): string | null {
  return accessToken;
}

export function setAccessToken(token: string | null): void {
  accessToken = token;
  listeners.forEach((listener) => listener(token));
}

/** Nhận thông báo khi token đổi; null nghĩa là phiên đã hết (refresh thất bại hoặc đăng xuất). */
export function onSessionChange(listener: SessionListener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/**
 * Đổi refresh token (cookie) lấy access token mới. Gọi đồng thời nhiều lần chỉ tạo MỘT request:
 * refresh token xoay vòng sau mỗi lần dùng, gửi trùng sẽ bị coi là dùng lại token.
 */
export function refreshAccessToken(): Promise<string | null> {
  refreshInFlight ??= (async () => {
    try {
      const response = await fetch(`${AUTH_BASE}/refresh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: '{}',
        credentials: 'same-origin',
      });
      const token = response.ok ? ((await response.json()) as TokenResponse).accessToken : null;
      setAccessToken(token);
      return token;
    } catch {
      setAccessToken(null);
      return null;
    } finally {
      refreshInFlight = null;
    }
  })();
  return refreshInFlight;
}

export interface RequestOptions extends Omit<RequestInit, 'body'> {
  /** Body JSON (tự đặt Content-Type). */
  json?: unknown;
  body?: BodyInit;
  /** Không gửi access token và không tự refresh (endpoint đăng nhập/đăng ký). */
  anonymous?: boolean;
}

export async function apiFetch<T = void>(path: string, options: RequestOptions = {}): Promise<T> {
  const { json, anonymous = false, headers, ...init } = options;

  const send = (token: string | null) => {
    const merged = new Headers(headers);
    if (json !== undefined) {
      merged.set('Content-Type', 'application/json');
    }
    if (token && !anonymous) {
      merged.set('Authorization', `Bearer ${token}`);
    }
    return fetch(path, {
      ...init,
      headers: merged,
      body: json !== undefined ? JSON.stringify(json) : init.body,
      credentials: 'same-origin',
    });
  };

  let response = await send(accessToken);
  if (response.status === 401 && !anonymous) {
    const token = await refreshAccessToken();
    if (token) {
      response = await send(token);
    }
  }

  if (!response.ok) {
    throw new ApiError(response.status, await readProblem(response));
  }

  if (response.status === 204 || response.headers.get('Content-Length') === '0') {
    return undefined as T;
  }
  return (await response.json()) as T;
}

async function readProblem(response: Response): Promise<ProblemDetails> {
  try {
    return (await response.json()) as ProblemDetails;
  } catch {
    return { status: response.status, title: response.statusText };
  }
}
