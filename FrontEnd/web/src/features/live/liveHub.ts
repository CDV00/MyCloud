import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr';
import type { AppNotification } from '../../api/notifications';
import { NOTIFICATION_HUB } from '../../api/notifications';
import { issueLiveTicket } from '../../api/collab';
import { getAccessToken, refreshAccessToken } from '../../api/http';

export interface ItemActivity {
  itemId: string;
  /** comment | version | version_note … */
  kind: string;
  actorId: string | null;
}

export interface PresenceUpdate {
  itemId: string;
  userId: string;
  name: string;
  mode: 'viewing' | 'editing' | 'left';
  at: string;
}

interface Room {
  activity: Set<(a: ItemActivity) => void>;
  presence: Set<(p: PresenceUpdate) => void>;
}

/**
 * Một kết nối SignalR dùng chung cho cả app (ADR-017): thông báo của người dùng + "phòng" của từng tệp đang mở. Vào phòng
 * cần vé Drive cấp (đã kiểm tra quyền xem); mất kết nối rồi nối lại ⇒ tự vào lại mọi phòng đang theo dõi.
 */
class LiveHub {
  private connection: HubConnection | null = null;
  private starting: Promise<void> | null = null;
  private readonly notificationListeners = new Set<(n: AppNotification) => void>();
  private readonly rooms = new Map<string, Room>();

  /** Lắng nghe thông báo mới; trả hàm huỷ. */
  onNotification(listener: (n: AppNotification) => void) {
    this.notificationListeners.add(listener);
    void this.ensureStarted();
    return () => {
      this.notificationListeners.delete(listener);
      this.stopIfIdle();
    };
  }

  /** Theo dõi phòng của tệp; trả hàm rời phòng. */
  watch(itemId: string, onActivity: (a: ItemActivity) => void, onPresence: (p: PresenceUpdate) => void) {
    let room = this.rooms.get(itemId);
    const isNew = !room;
    if (!room) {
      room = { activity: new Set(), presence: new Set() };
      this.rooms.set(itemId, room);
    }

    room.activity.add(onActivity);
    room.presence.add(onPresence);
    if (isNew) void this.join(itemId);

    return () => {
      const current = this.rooms.get(itemId);
      if (!current) return;
      current.activity.delete(onActivity);
      current.presence.delete(onPresence);
      if (current.activity.size === 0 && current.presence.size === 0) {
        this.rooms.delete(itemId);
        if (this.connection?.state === HubConnectionState.Connected) {
          void this.connection.invoke('Unwatch', itemId).catch(() => undefined);
        }
      }

      this.stopIfIdle();
    };
  }

  /** Không còn ai dùng (vd đăng xuất — layout bị gỡ) ⇒ đóng kết nối; lần sau mở lại với token mới. */
  private stopIfIdle() {
    if (this.notificationListeners.size > 0 || this.rooms.size > 0 || !this.connection) return;
    const connection = this.connection;
    this.connection = null;
    this.starting = null;
    void connection.stop();
  }

  /** Nhịp tim "đang xem / đang sửa" (bỏ qua khi chưa kết nối). */
  presence(itemId: string, mode: 'viewing' | 'editing') {
    if (this.connection?.state === HubConnectionState.Connected && this.rooms.has(itemId)) {
      void this.connection.invoke('Presence', itemId, mode).catch(() => undefined);
    }
  }

  private async join(itemId: string) {
    try {
      await this.ensureStarted();
      if (!this.rooms.has(itemId) || this.connection?.state !== HubConnectionState.Connected) return;
      const { token } = await issueLiveTicket(itemId);
      await this.connection.invoke<boolean>('Watch', token);
    } catch {
      // Không vào được phòng (mất mạng, hết quyền): màn hình vẫn dùng được, chỉ không tự cập nhật.
    }
  }

  private ensureStarted(): Promise<void> {
    if (this.connection?.state === HubConnectionState.Connected) return Promise.resolve();
    if (this.starting) return this.starting;

    if (!this.connection) {
      const connection = new HubConnectionBuilder()
        .withUrl(NOTIFICATION_HUB, {
          accessTokenFactory: async () => getAccessToken() ?? (await refreshAccessToken()) ?? '',
        })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();
      connection.on('notification', (n: AppNotification) => this.notificationListeners.forEach((l) => l(n)));
      connection.on('item-activity', (a: ItemActivity) =>
        this.rooms.get(a.itemId)?.activity.forEach((l) => l(a)),
      );
      connection.on('presence', (p: PresenceUpdate) =>
        this.rooms.get(p.itemId)?.presence.forEach((l) => l(p)),
      );
      connection.onreconnected(() => {
        for (const itemId of this.rooms.keys()) void this.join(itemId);
      });
      this.connection = connection;
    }

    const connection = this.connection;
    this.starting = connection
      .start()
      .catch(() => {
        // Hub chưa sẵn sàng / mất mạng: thử lại sau 10 giây; REST vẫn dùng được.
        setTimeout(() => {
          this.starting = null;
          void this.ensureStarted().then(() => {
            for (const itemId of this.rooms.keys()) void this.join(itemId);
          });
        }, 10_000);
      })
      .finally(() => {
        if (connection.state === HubConnectionState.Connected) this.starting = null;
      });
    return this.starting;
  }
}

export const liveHub = new LiveHub();
