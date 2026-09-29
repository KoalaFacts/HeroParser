import { defineConfig, lazyPlugins } from "vite-plus";
import vue from "@vitejs/plugin-vue";

// https://vite.dev/config/
export default defineConfig({
  base: "./",
  plugins: lazyPlugins(() => [
    vue({
      vapor: true,
    } as any),
  ]),
  build: {
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (id.includes("node_modules/onnxruntime-web/")) {
            return "onnxruntime-web";
          }
        },
      },
    },
  },
});
