import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useAuth } from '../../auth/authContext';
import { driveKeys, refreshDrive, searchKeys } from '../drive/queries';
import { liveHub, type PresenceUpdate } from '../live/liveHub';

export interface Present {
  userId: string;
  name: string;
  mode: 'viewing' | 'editing';
  seenAt: number;
}

const HEARTBEAT_MS = 20_000;
const EXPIRE_MS = 60_000;

export const commentKeys = { item: (itemId: string) => ['comments', itemId] as const };

/**
 * Tham gia "phòng" realtime của tệp (ADR-017): làm mới lịch sử / trao đổi khi người khác lưu phiên bản hay gửi tin nhắn,
 * và cho biết ai đang xem / đang sửa (nhịp tim mỗi 20 giây, mất tín hiệu 1 phút ⇒ coi như đã rời).
 */
export function useItemRoom(itemId: string | null | undefined, mode: 'viewing' | 'editing'): Present[] {
  const queryClient = useQueryClient();
  const { user } = useAuth();
  const [room, setRoom] = useState<{ itemId: string; people: Map<string, Present> } | null>(null);

  useEffect(() => {
    if (!itemId) return;
    const beat = () => liveHub.presence(itemId, mode);
    let lastReply = 0;

    const stop = liveHub.watch(
      itemId,
      (activity) => {
        void queryClient.invalidateQueries({ queryKey: commentKeys.item(itemId) });
        if (activity.kind === 'version') {
          // Nội dung mới: làm mới cả danh sách thư mục ⇒ xem trước tải đúng phiên bản mới nhất.
          refreshDrive(queryClient);
        } else if (activity.kind !== 'comment') {
          void queryClient.invalidateQueries({ queryKey: driveKeys.versions(itemId) });
        }

        // Nhật ký hoạt động (Search) cập nhật trễ vài giây.
        setTimeout(() => void queryClient.invalidateQueries({ queryKey: searchKeys.activity(itemId) }), 2000);
      },
      (p: PresenceUpdate) => {
        if (p.userId === user?.id) return;
        setRoom((prev) => {
          const people = new Map(prev?.itemId === itemId ? prev.people : []);
          const isNew = !people.has(p.userId);
          if (p.mode === 'left') people.delete(p.userId);
          else people.set(p.userId, { userId: p.userId, name: p.name, mode: p.mode, seenAt: Date.now() });
          // Người mới vào ⇒ báo lại ngay để họ thấy mình (không phải đợi nhịp tim kế tiếp).
          if (isNew && p.mode !== 'left' && Date.now() - lastReply > 2000) {
            lastReply = Date.now();
            setTimeout(beat, 300);
          }

          return { itemId, people };
        });
      },
    );

    const first = setTimeout(beat, 1500);
    const heartbeat = setInterval(beat, HEARTBEAT_MS);
    const sweep = setInterval(
      () =>
        setRoom((prev) => {
          if (!prev || prev.itemId !== itemId) return prev;
          const people = new Map([...prev.people].filter(([, v]) => Date.now() - v.seenAt < EXPIRE_MS));
          return people.size === prev.people.size ? prev : { itemId, people };
        }),
      15_000,
    );

    return () => {
      stop();
      clearTimeout(first);
      clearInterval(heartbeat);
      clearInterval(sweep);
    };
  }, [itemId, mode, queryClient, user?.id]);

  return room && room.itemId === itemId ? [...room.people.values()] : [];
}
