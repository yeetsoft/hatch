import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { ThemeProvider, applySharedSeed } from '@hatch/ui';
import './theme.css';
import { App } from './App';

applySharedSeed();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider>
      <App />
    </ThemeProvider>
  </StrictMode>,
);
