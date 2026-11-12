import { useEffect, useState } from 'react'
import { NodeViewWrapper, ReactNodeViewRenderer } from '@tiptap/react'
import { getDocumentAssetBlob } from '../../api/client.js'

export function AssetImageView({ node, selected }) {
  const [src, setSrc] = useState('')

  useEffect(() => {
    let active = true
    let objectUrl = ''
    getDocumentAssetBlob(node.attrs.documentId, node.attrs.assetId)
      .then((blob) => {
        if (!active) return
        objectUrl = URL.createObjectURL(blob)
        setSrc(objectUrl)
      })
      .catch(() => setSrc(''))
    return () => {
      active = false
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [node.attrs.documentId, node.attrs.assetId])

  return (
    <NodeViewWrapper
      className={`doc-asset-image align-${node.attrs.align || 'left'}${selected ? ' selected' : ''}`}
      data-drag-handle
    >
      {src ? (
        <img
          src={src}
          alt={node.attrs.alt || ''}
          width={node.attrs.width || undefined}
          height={node.attrs.height || undefined}
          style={{ maxWidth: '100%', height: 'auto' }}
        />
      ) : <span className="gen-empty">IMAGE_UNAVAILABLE</span>}
    </NodeViewWrapper>
  )
}

export const AssetImage = (Image) => Image.extend({
  name: 'image',
  inline: false,
  addAttributes() {
    return {
      ...(this.parent?.() ?? {}),
      assetId: { default: null },
      src: { default: '' },
      documentId: { default: null },
      alt: { default: '' },
      width: { default: null },
      height: { default: null },
      align: { default: 'left' },
    }
  },
  addNodeView() {
    return ReactNodeViewRenderer(AssetImageView)
  },
})
