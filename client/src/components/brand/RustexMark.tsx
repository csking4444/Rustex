/**
 * Original mark for Rustex — a hazard-plate hexagon with a corroded crossbar, evoking
 * "tactical/industrial hazard" rather than any specific game's branding. Deliberately not a
 * modified version of Facepunch's Rust logo (that's their trademark/copyright) — see the
 * originality note in README.md. Pure SVG, themeable via currentColor + the two accent stops.
 */
export function RustexMark({ size = 32, className = "" }: { size?: number; className?: string }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 48 48"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      className={className}
      aria-label="Rustex"
      role="img"
    >
      <defs>
        <linearGradient id="rustexMarkGradient" x1="0" y1="0" x2="48" y2="48" gradientUnits="userSpaceOnUse">
          <stop offset="0%" stopColor="#990000" />
          <stop offset="100%" stopColor="#5A0000" />
        </linearGradient>
      </defs>

      <path
        d="M24 2 L44 13 V35 L24 46 L4 35 V13 Z"
        fill="url(#rustexMarkGradient)"
        stroke="#C1121F"
        strokeWidth="1.5"
        strokeOpacity="0.6"
      />

      <path
        d="M24 2 L44 13 V35 L24 46 L4 35 V13 Z"
        fill="none"
        stroke="#000000"
        strokeOpacity="0.25"
        strokeWidth="6"
        strokeDasharray="1 5"
        strokeLinecap="round"
      />

      <path d="M15 30 L24 15 L33 30 Z" fill="none" stroke="#FFFFFF" strokeWidth="2.25" strokeLinejoin="round" />
      <circle cx="24" cy="26.5" r="2" fill="#FFFFFF" />
    </svg>
  );
}
