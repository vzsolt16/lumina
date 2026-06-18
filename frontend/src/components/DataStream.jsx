const STREAM_ITEMS = [
  'LMNA·0x1', 'FLSH·REV', '0110110', 'QZMOD·3', 'AI·SYS', '0x8A3F',
  'SYNC·OK', '1001011', 'NTRL·AI', 'BUF·CLR', '0x2C7E', 'LRN·IDX',
  '0100111', 'DECK·42', 'PROC·OK', '0x5D9A', 'CARD·GEN', '1110001',
  'RETNT·↑', 'SYS·RDY', '0xAE12',
]

// Fixed 48px monospace data-stream sidebar (see DESIGN.md). Repeated for rhythm.
export default function DataStream() {
  const items = [...STREAM_ITEMS, ...STREAM_ITEMS.slice(0, 10)]
  return (
    <div className="data-stream" aria-hidden="true">
      {items.map((item, i) => (
        <span key={i}>{item}</span>
      ))}
    </div>
  )
}
