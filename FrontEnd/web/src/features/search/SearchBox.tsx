import CloseIcon from '@mui/icons-material/Close';
import SearchIcon from '@mui/icons-material/Search';
import { IconButton, InputAdornment, TextField } from '@mui/material';
import { useState, type FormEvent } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router';

/** Ô tìm kiếm trên thanh trên cùng; Enter ⇒ trang kết quả (giữ bộ lọc đang chọn). */
export function SearchBox() {
  const navigate = useNavigate();
  const location = useLocation();
  const [params] = useSearchParams();
  const onSearchPage = location.pathname === '/search';
  const urlQuery = onSearchPage ? (params.get('q') ?? '') : '';
  const [value, setValue] = useState(urlQuery);
  const [syncedQuery, setSyncedQuery] = useState(urlQuery);
  if (syncedQuery !== urlQuery) {
    // Điều hướng (vd quay lại trang trước) ⇒ ô tìm kiếm hiện đúng từ khoá của URL.
    setSyncedQuery(urlQuery);
    setValue(urlQuery);
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    const next = onSearchPage ? new URLSearchParams(params) : new URLSearchParams();
    next.set('q', value.trim());
    navigate(`/search?${next}`);
  }

  return (
    <form role="search" onSubmit={submit} style={{ flexGrow: 1, maxWidth: 720 }}>
      <TextField
        fullWidth
        size="small"
        placeholder="Tìm trong Drive"
        value={value}
        onChange={(e) => setValue(e.target.value)}
        slotProps={{
          htmlInput: { 'aria-label': 'Tìm trong Drive', maxLength: 100 },
          input: {
            sx: { borderRadius: 6, bgcolor: 'action.hover', '& fieldset': { border: 0 } },
            startAdornment: (
              <InputAdornment position="start">
                <SearchIcon />
              </InputAdornment>
            ),
            endAdornment: value ? (
              <InputAdornment position="end">
                <IconButton size="small" aria-label="Xoá từ khoá" onClick={() => setValue('')}>
                  <CloseIcon fontSize="small" />
                </IconButton>
              </InputAdornment>
            ) : undefined,
          },
        }}
      />
    </form>
  );
}
