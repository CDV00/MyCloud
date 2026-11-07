import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiFetch, ApiError, getAccessToken, onSessionChange, setAccessToken } from './http';

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

describe('apiFetch', () => {
  const fetchMock = vi.fn<typeof fetch>();

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock);
    fetchMock.mockReset();
    setAccessToken('expired-token');
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    setAccessToken(null);
  });

  function authHeader(call: number): string | null {
    const init = fetchMock.mock.calls[call]![1]!;
    return new Headers(init.headers).get('Authorization');
  }

  it('gửi access token trong header Authorization', async () => {
    fetchMock.mockResolvedValueOnce(json(200, { ok: true }));

    await apiFetch('/api/drive/v1/folders');

    expect(authHeader(0)).toBe('Bearer expired-token');
  });

  it('gặp 401 thì refresh một lần rồi gửi lại request với token mới', async () => {
    fetchMock.mockImplementation(async (input, init) => {
      const url = String(input);
      if (url.endsWith('/auth/refresh'))
        return json(200, { accessToken: 'fresh-token', tokenType: 'Bearer', expiresIn: 900 });
      const auth = new Headers(init?.headers).get('Authorization');
      return auth === 'Bearer fresh-token' ? json(200, { id: 1 }) : json(401, { title: 'Unauthorized' });
    });

    // Ba request đồng thời cùng nhận 401 ⇒ chỉ MỘT lần refresh (refresh token bị xoay vòng sau mỗi lần dùng).
    const results = await Promise.all([apiFetch('/a'), apiFetch('/b'), apiFetch('/c')]);

    expect(results).toEqual([{ id: 1 }, { id: 1 }, { id: 1 }]);
    const refreshCalls = fetchMock.mock.calls.filter(([url]) => String(url).endsWith('/auth/refresh'));
    expect(refreshCalls).toHaveLength(1);
    expect(getAccessToken()).toBe('fresh-token');
  });

  it('refresh thất bại thì báo phiên hết hạn và ném ApiError 401', async () => {
    fetchMock.mockImplementation(async (input) =>
      String(input).endsWith('/auth/refresh')
        ? json(401, { code: 'IDENTITY.INVALID_REFRESH_TOKEN' })
        : json(401, {}),
    );
    const listener = vi.fn();
    const unsubscribe = onSessionChange(listener);

    const error = await apiFetch('/api/identity/v1/users/me').catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(401);
    expect(listener).toHaveBeenCalledWith(null);
    expect(getAccessToken()).toBeNull();
    unsubscribe();
  });

  it('request ẩn danh không gửi token và không tự refresh', async () => {
    fetchMock.mockResolvedValueOnce(json(401, { code: 'IDENTITY.INVALID_CREDENTIALS' }));

    await expect(
      apiFetch('/api/identity/v1/auth/login', { method: 'POST', json: {}, anonymous: true }),
    ).rejects.toMatchObject({
      status: 401,
      problem: { code: 'IDENTITY.INVALID_CREDENTIALS' },
    });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(authHeader(0)).toBeNull();
  });

  it('204 trả về undefined', async () => {
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));

    await expect(apiFetch('/x', { method: 'DELETE' })).resolves.toBeUndefined();
  });
});
