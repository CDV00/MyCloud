import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutlined';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import SendIcon from '@mui/icons-material/Send';
import {
  Alert,
  Avatar,
  Box,
  Button,
  Chip,
  CircularProgress,
  IconButton,
  List,
  ListItemButton,
  ListItemText,
  Menu,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Fragment, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import {
  deleteComment,
  editComment,
  listComments,
  postComment,
  resolveComment,
  type FileComment,
} from '../../api/collab';
import { getSharing, type DriveItem } from '../../api/drive';
import { toUserMessage } from '../../api/errors';
import { useAuth } from '../../auth/authContext';
import { formatModified } from '../../lib/format';
import { useUsers } from '../people/useUsers';
import { commentKeys } from './useItemRoom';

export interface ComposerTarget {
  versionId?: string | null;
  versionNo?: number | null;
}

interface Person {
  id: string;
  name: string;
}

const initials = (name: string) =>
  name
    .split(/\s+/)
    .filter(Boolean)
    .slice(-2)
    .map((w) => w[0]!.toUpperCase())
    .join('');

/** Nội dung tin nhắn, tô đậm các "@Tên" của người được nhắc. */
function Body({ text, names }: { text: string; names: string[] }) {
  if (names.length === 0) return <>{text}</>;
  const pattern = new RegExp(
    `(@(?:${names.map((n) => n.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')}))`,
    'g',
  );
  return (
    <>
      {text.split(pattern).map((part, i) =>
        part.startsWith('@') && names.includes(part.slice(1)) ? (
          <Box key={i} component="span" sx={{ color: 'primary.main', fontWeight: 600 }}>
            {part}
          </Box>
        ) : (
          <Fragment key={i}>{part}</Fragment>
        ),
      )}
    </>
  );
}

/**
 * Trao đổi về tệp (ADR-017): các luồng tin nhắn (chung hoặc góp ý một phiên bản), trả lời, sửa / xoá tin của mình,
 * đánh dấu luồng đã xử lý, nhắc tên bằng "@". Cập nhật realtime nhờ phòng của tệp (useItemRoom làm mới truy vấn).
 */
export function Discussion({
  item,
  canComment,
  target,
  onTargetChange,
  versionFilter,
  onVersionFilter,
}: {
  item: DriveItem;
  canComment: boolean;
  /** Ngữ cảnh của tin nhắn sắp gửi (góp ý phiên bản nào). */
  target: ComposerTarget | null;
  onTargetChange(target: ComposerTarget | null): void;
  /** Chỉ hiện luồng của một phiên bản. */
  versionFilter: { id: string; no: number } | null;
  onVersionFilter(filter: { id: string; no: number } | null): void;
}) {
  const { user } = useAuth();
  const queryClient = useQueryClient();
  const comments = useQuery({ queryKey: commentKeys.item(item.id), queryFn: () => listComments(item.id) });
  const sharing = useQuery({
    queryKey: ['sharing', item.id],
    queryFn: () => getSharing(item.id),
    staleTime: 60_000,
  });
  const authors = useUsers((comments.data ?? []).flatMap((c) => [c.authorId, c.resolvedBy, ...c.mentions]));
  const [showResolved, setShowResolved] = useState(false);
  const [replyTo, setReplyTo] = useState<FileComment | null>(null);
  const [editing, setEditing] = useState<FileComment | null>(null);
  const [menu, setMenu] = useState<{ comment: FileComment; anchor: HTMLElement } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const bottom = useRef<HTMLDivElement>(null);

  const people: Person[] = useMemo(() => {
    const list: Person[] = [];
    if (sharing.data?.owner) list.push({ id: sharing.data.owner.id, name: sharing.data.owner.displayName });
    for (const p of sharing.data?.permissions ?? []) list.push({ id: p.user.id, name: p.user.displayName });
    return list.filter((p, i) => p.id !== user?.id && list.findIndex((q) => q.id === p.id) === i);
  }, [sharing.data, user?.id]);

  const nameOf = (id: string | null) =>
    !id
      ? ''
      : id === user?.id
        ? 'Bạn'
        : (authors.get(id)?.displayName ?? people.find((p) => p.id === id)?.name ?? '…');

  const all = comments.data ?? [];
  const roots = all
    .filter((c) => c.parentId === null)
    .filter((c) => !versionFilter || c.versionId === versionFilter.id)
    .filter((c) => showResolved || !c.resolvedAt || versionFilter);
  const resolvedCount = all.filter((c) => c.parentId === null && c.resolvedAt).length;
  const repliesOf = (root: FileComment) => all.filter((c) => c.parentId === root.id);

  useEffect(() => {
    bottom.current?.scrollIntoView({ block: 'end' });
  }, [all.length]);

  const refresh = () => void queryClient.invalidateQueries({ queryKey: commentKeys.item(item.id) });
  const run = useMutation({
    mutationFn: (work: () => Promise<unknown>) => work(),
    onError: (e) => setError(toUserMessage(e)),
    onSuccess: () => setError(null),
    onSettled: refresh,
  });

  function bubble(c: FileComment, reply: boolean) {
    const mine = c.authorId === user?.id;
    const name = nameOf(c.authorId);
    const mentionNames = c.mentions
      .map((m) => authors.get(m)?.displayName ?? people.find((p) => p.id === m)?.name)
      .filter(Boolean) as string[];
    return (
      <Stack key={c.id} direction="row" spacing={1} sx={{ alignItems: 'flex-start', pl: reply ? 5 : 0 }}>
        <Avatar
          sx={{
            width: reply ? 26 : 32,
            height: reply ? 26 : 32,
            fontSize: 13,
            bgcolor: mine ? 'primary.main' : 'secondary.main',
          }}
        >
          {initials(name === 'Bạn' ? (user?.displayName ?? 'B') : name)}
        </Avatar>
        <Box sx={{ minWidth: 0, flexGrow: 1 }}>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'baseline' }}>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>
              {name}
            </Typography>
            <Typography variant="caption" color="text.secondary">
              {formatModified(c.createdAt)}
              {c.editedAt && ' · đã sửa'}
            </Typography>
          </Stack>
          {c.deleted ? (
            <Typography variant="body2" color="text.secondary" sx={{ fontStyle: 'italic' }}>
              Tin nhắn đã bị xoá
            </Typography>
          ) : (
            <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
              <Body text={c.body ?? ''} names={mentionNames} />
            </Typography>
          )}
          {!reply && canComment && !c.deleted && (
            <Button size="small" sx={{ p: 0, minWidth: 0, mt: 0.25 }} onClick={() => setReplyTo(c)}>
              Trả lời
            </Button>
          )}
        </Box>
        {!c.deleted && (mine || item.ownerId === user?.id || (!reply && canComment)) && (
          <IconButton
            size="small"
            aria-label="Thao tác với tin nhắn"
            onClick={(e) => setMenu({ comment: c, anchor: e.currentTarget })}
          >
            <MoreVertIcon fontSize="small" />
          </IconButton>
        )}
      </Stack>
    );
  }

  return (
    <Stack sx={{ height: '100%', minHeight: 0 }}>
      <Stack
        direction="row"
        spacing={1}
        useFlexGap
        sx={{ alignItems: 'center', flexWrap: 'wrap', px: 2, py: 1 }}
      >
        {versionFilter ? (
          <Chip
            size="small"
            color="primary"
            label={`Phiên bản ${versionFilter.no}`}
            onDelete={() => onVersionFilter(null)}
          />
        ) : (
          resolvedCount > 0 && (
            <Chip
              size="small"
              variant={showResolved ? 'filled' : 'outlined'}
              icon={<CheckCircleOutlineIcon />}
              label={
                showResolved ? `Ẩn ${resolvedCount} luồng đã xử lý` : `Hiện ${resolvedCount} luồng đã xử lý`
              }
              onClick={() => setShowResolved((v) => !v)}
            />
          )
        )}
      </Stack>

      <Box sx={{ flexGrow: 1, overflow: 'auto', px: 2, pb: 1 }}>
        {comments.isPending && <CircularProgress size={24} />}
        {comments.error && <Alert severity="error">{toUserMessage(comments.error)}</Alert>}
        {comments.data && roots.length === 0 && (
          <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
            {versionFilter ? 'Chưa có góp ý nào cho phiên bản này.' : 'Chưa có trao đổi nào.'}
            {canComment && <br />}
            {canComment && 'Hãy bắt đầu — dùng @ để nhắc tên người liên quan.'}
          </Typography>
        )}
        <Stack spacing={1.5}>
          {roots.map((root) => (
            <Paper
              key={root.id}
              variant="outlined"
              sx={{
                p: 1.5,
                borderRadius: 2,
                opacity: root.resolvedAt ? 0.75 : 1,
                borderColor: replyTo?.id === root.id ? 'primary.main' : 'divider',
              }}
            >
              {(root.versionId || root.resolvedAt) && (
                <Stack direction="row" spacing={1} sx={{ mb: 1 }}>
                  {root.versionId && (
                    <Chip
                      size="small"
                      variant="outlined"
                      label={root.versionNo ? `Góp ý phiên bản ${root.versionNo}` : 'Phiên bản đã xoá'}
                      onClick={
                        root.versionNo
                          ? () => onVersionFilter({ id: root.versionId!, no: root.versionNo! })
                          : undefined
                      }
                    />
                  )}
                  {root.resolvedAt && (
                    <Chip
                      size="small"
                      color="success"
                      variant="outlined"
                      icon={<CheckCircleOutlineIcon />}
                      label={`${nameOf(root.resolvedBy)} đã đánh dấu đã xử lý`}
                    />
                  )}
                </Stack>
              )}
              <Stack spacing={1.25}>
                {bubble(root, false)}
                {repliesOf(root).map((r) => bubble(r, true))}
              </Stack>
            </Paper>
          ))}
        </Stack>
        <div ref={bottom} />
      </Box>

      {error && (
        <Alert severity="error" onClose={() => setError(null)} sx={{ mx: 2, mb: 1 }}>
          {error}
        </Alert>
      )}
      {canComment ? (
        <Composer
          key={editing?.id ?? 'new'}
          people={people}
          busy={run.isPending}
          initial={editing?.body ?? ''}
          context={
            editing ? (
              <Chip size="small" label="Đang sửa tin nhắn" onDelete={() => setEditing(null)} />
            ) : replyTo ? (
              <Chip
                size="small"
                label={`Trả lời ${nameOf(replyTo.authorId)}`}
                onDelete={() => setReplyTo(null)}
              />
            ) : target?.versionId ? (
              <Chip
                size="small"
                color="primary"
                label={`Góp ý phiên bản ${target.versionNo}`}
                onDelete={() => onTargetChange(null)}
              />
            ) : null
          }
          onSend={(body, mentions) =>
            run.mutate(async () => {
              if (editing) {
                await editComment(item.id, editing.id, body);
                setEditing(null);
              } else {
                await postComment(item.id, {
                  body,
                  mentions,
                  parentId: replyTo?.id ?? null,
                  versionId: replyTo ? null : (target?.versionId ?? versionFilter?.id ?? null),
                });
                setReplyTo(null);
              }
            })
          }
        />
      ) : (
        <Typography
          variant="body2"
          color="text.secondary"
          sx={{ px: 2, py: 1.5, borderTop: 1, borderColor: 'divider' }}
        >
          Bạn chỉ có quyền xem — cần quyền nhận xét trở lên để tham gia trao đổi.
        </Typography>
      )}

      <Menu anchorEl={menu?.anchor} open={menu !== null} onClose={() => setMenu(null)}>
        {menu && menu.comment.parentId === null && canComment && (
          <MenuItem
            onClick={() => {
              const c = menu.comment;
              setMenu(null);
              run.mutate(() => resolveComment(item.id, c.id, !c.resolvedAt));
            }}
          >
            {menu.comment.resolvedAt ? 'Mở lại luồng' : 'Đánh dấu đã xử lý'}
          </MenuItem>
        )}
        {menu && menu.comment.authorId === user?.id && (
          <MenuItem
            onClick={() => {
              setEditing(menu.comment);
              setMenu(null);
            }}
          >
            Sửa
          </MenuItem>
        )}
        {menu && (menu.comment.authorId === user?.id || item.ownerId === user?.id) && (
          <MenuItem
            sx={{ color: 'error.main' }}
            onClick={() => {
              const c = menu.comment;
              setMenu(null);
              run.mutate(() => deleteComment(item.id, c.id));
            }}
          >
            Xoá
          </MenuItem>
        )}
      </Menu>
    </Stack>
  );
}

/** Ô soạn tin: Enter gửi, Shift+Enter xuống dòng; gõ "@" để chọn người được nhắc tên. */
function Composer({
  people,
  busy,
  initial,
  context,
  onSend,
}: {
  people: Person[];
  busy: boolean;
  initial: string;
  context: ReactNode;
  onSend(body: string, mentions: string[]): void;
}) {
  const [text, setText] = useState(initial);
  const [mentioned, setMentioned] = useState<Person[]>([]);
  const [query, setQuery] = useState<string | null>(null);
  const input = useRef<HTMLTextAreaElement>(null);

  const suggestions =
    query === null
      ? []
      : people.filter((p) => p.name.toLowerCase().includes(query.toLowerCase())).slice(0, 6);

  function onChange(value: string, caret: number) {
    setText(value);
    const match = /@([^\s@]{0,30})$/.exec(value.slice(0, caret));
    setQuery(match ? match[1]! : null);
  }

  function pick(p: Person) {
    const el = input.current;
    const caret = el?.selectionStart ?? text.length;
    const before = text.slice(0, caret).replace(/@([^\s@]{0,30})$/, `@${p.name} `);
    setText(before + text.slice(caret));
    setMentioned((m) => (m.some((x) => x.id === p.id) ? m : [...m, p]));
    setQuery(null);
    requestAnimationFrame(() => {
      el?.focus();
      el?.setSelectionRange(before.length, before.length);
    });
  }

  function send() {
    const body = text.trim();
    if (!body || busy) return;
    onSend(
      body,
      mentioned.filter((p) => body.includes(`@${p.name}`)).map((p) => p.id),
    );
    setText('');
    setMentioned([]);
  }

  return (
    <Box sx={{ borderTop: 1, borderColor: 'divider', p: 1.5, position: 'relative' }}>
      {context && <Box sx={{ mb: 1 }}>{context}</Box>}
      {suggestions.length > 0 && (
        <Paper
          elevation={6}
          sx={{ position: 'absolute', bottom: '100%', left: 12, right: 12, mb: 0.5, zIndex: 2 }}
        >
          <List dense disablePadding aria-label="Nhắc tên">
            {suggestions.map((p) => (
              <ListItemButton key={p.id} onMouseDown={(e) => e.preventDefault()} onClick={() => pick(p)}>
                <ListItemText primary={p.name} />
              </ListItemButton>
            ))}
          </List>
        </Paper>
      )}
      <Stack direction="row" spacing={1} sx={{ alignItems: 'flex-end' }}>
        <TextField
          inputRef={input}
          fullWidth
          multiline
          maxRows={6}
          size="small"
          placeholder="Viết trao đổi… (@ để nhắc tên, Shift+Enter xuống dòng)"
          value={text}
          onChange={(e) => onChange(e.target.value, e.target.selectionStart ?? e.target.value.length)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
              e.preventDefault();
              if (suggestions.length > 0) pick(suggestions[0]!);
              else send();
            } else if (e.key === 'Escape' && query !== null) {
              e.stopPropagation();
              setQuery(null);
            }
          }}
          slotProps={{ htmlInput: { 'aria-label': 'Nội dung trao đổi', maxLength: 4000 } }}
        />
        <IconButton color="primary" disabled={!text.trim() || busy} onClick={send} aria-label="Gửi">
          {busy ? <CircularProgress size={20} /> : <SendIcon />}
        </IconButton>
      </Stack>
    </Box>
  );
}
