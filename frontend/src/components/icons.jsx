// Line-art icon set for Lumina. Outline-only, square caps/miter joins to match the
// sharp, hairline, no-radius aesthetic. Stroke uses currentColor so containers control
// the accent; size via the `size` prop (drives both width and height).

function Svg({ size = 20, children, ...rest }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.5}
      strokeLinecap="square"
      strokeLinejoin="miter"
      aria-hidden="true"
      {...rest}
    >
      {children}
    </svg>
  )
}

// Notes — document outline with text lines.
export function NotesIcon(props) {
  return (
    <Svg {...props}>
      <path d="M5 3 H19 V21 H5 Z" />
      <line x1="8" y1="8" x2="16" y2="8" />
      <line x1="8" y1="12" x2="16" y2="12" />
      <line x1="8" y1="16" x2="13" y2="16" />
    </Svg>
  )
}

// Flashcards — two offset stacked cards.
export function FlashcardsIcon(props) {
  return (
    <Svg {...props}>
      <path d="M3 8 H15 V20 H3 Z" />
      <path d="M9 4 H21 V16" />
    </Svg>
  )
}

// Quizzes — sheet with a checkmark and answer lines.
export function QuizIcon(props) {
  return (
    <Svg {...props}>
      <path d="M5 3 H19 V21 H5 Z" />
      <path d="M8 9 L9.5 10.5 L12 7.5" />
      <path d="M8 16 L9.5 17.5 L12 14.5" />
      <line x1="14" y1="9" x2="16" y2="9" />
      <line x1="14" y1="16" x2="16" y2="16" />
    </Svg>
  )
}

// Study Corner — square-edged speech bubble with a reply tail.
export function ChatIcon(props) {
  return (
    <Svg {...props}>
      <path d="M3 4 H21 V15 H9 L5 19 V15 H3 Z" />
      <line x1="7" y1="8" x2="17" y2="8" />
      <line x1="7" y1="11" x2="13" y2="11" />
    </Svg>
  )
}

// Upload — document with an up-arrow (dropzone).
export function UploadIcon(props) {
  return (
    <Svg {...props}>
      <path d="M5 3 H14 L19 8 V21 H5 Z" />
      <path d="M14 3 V8 H19" />
      <line x1="12" y1="18" x2="12" y2="11" />
      <path d="M9 14 L12 11 L15 14" />
    </Svg>
  )
}

// File — document with folded corner and text lines (loaded doc).
export function FileIcon(props) {
  return (
    <Svg {...props}>
      <path d="M5 3 H14 L19 8 V21 H5 Z" />
      <path d="M14 3 V8 H19" />
      <line x1="8" y1="13" x2="16" y2="13" />
      <line x1="8" y1="17" x2="14" y2="17" />
    </Svg>
  )
}
