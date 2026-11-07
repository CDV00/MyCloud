import { createTheme } from '@mui/material/styles';
import { viVN } from '@mui/material/locale';

export const theme = createTheme(
  {
    palette: {
      primary: { main: '#0b57d0', light: '#c2e7ff', contrastText: '#001d35' },
      background: { default: '#f8fafd', paper: '#ffffff' },
    },
    shape: { borderRadius: 8 },
    typography: {
      fontFamily: '"Roboto", "Segoe UI", system-ui, -apple-system, sans-serif',
      button: { textTransform: 'none', fontWeight: 500 },
    },
  },
  viVN,
);
