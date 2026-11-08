import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Port 1420 + strictPort match Tauri's defaults, so `tauri init` works without changes.
export default defineConfig({
  plugins: [react()],
  clearScreen: false,
  server: { port: 1420, strictPort: true },
});
