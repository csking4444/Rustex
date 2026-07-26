import type { Config } from "tailwindcss";

export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  darkMode: "class",
  theme: {
    extend: {
      colors: {
        base: {
          950: "#0B0B0B", // matte black
          900: "#141414",
          800: "#1A1A1A", // charcoal
          700: "#242424", // gunmetal
          600: "#333333", // slate gray
        },
        blood: {
          DEFAULT: "#7A0000", // primary accent
          light: "#990000", // secondary / crimson
        },
        critical: "#C1121F",
        success: "#1F8A3B",
        warning: "#D89A2B",
        info: "#4A7A96", // steel blue
        text: {
          primary: "#FFFFFF",
          secondary: "#C7C7C7",
          muted: "#8A8A8A",
        },
      },
      fontFamily: {
        sans: ["Inter", "system-ui", "sans-serif"],
        mono: ["JetBrains Mono", "ui-monospace", "monospace"],
      },
      boxShadow: {
        "glow-blood": "0 0 20px rgba(122, 0, 0, 0.45)",
        "glow-critical": "0 0 24px rgba(193, 18, 31, 0.55)",
        panel: "0 8px 32px rgba(0, 0, 0, 0.55)",
      },
      backdropBlur: {
        xs: "2px",
      },
      borderRadius: {
        xl: "0.875rem",
        "2xl": "1.25rem",
      },
      keyframes: {
        "pulse-glow": {
          "0%, 100%": { boxShadow: "0 0 0 0 rgba(193, 18, 31, 0.55)" },
          "50%": { boxShadow: "0 0 0 10px rgba(193, 18, 31, 0)" },
        },
      },
      animation: {
        "pulse-glow": "pulse-glow 2s ease-out infinite",
      },
    },
  },
  plugins: [],
} satisfies Config;
