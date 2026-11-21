export const SOURCE_TEXT_EXTENSIONS = [
  '.txt', '.md', '.cs', '.js', '.jsx', '.ts', '.tsx', '.py', '.java', '.html', '.css',
  '.json', '.xml', '.yaml', '.yml', '.sql', '.sh', '.bash', '.dockerfile', '.tf', '.go',
  '.rs', '.toml', '.ini', '.graphql', '.vue',
]

export const SUPPORTED_DOCUMENT_EXTENSIONS = [
  ...SOURCE_TEXT_EXTENSIONS, '.pdf', '.docx',
]

export const SUPPORTED_DOCUMENT_ACCEPT = [
  ...SUPPORTED_DOCUMENT_EXTENSIONS,
  'text/plain',
  'text/markdown',
  'application/pdf',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
].join(',')

export function getFileExtension(fileName) {
  if (fileName.toLowerCase() === 'dockerfile') return '.dockerfile'
  const dot = fileName.lastIndexOf('.')
  return dot > 0 ? fileName.slice(dot) : ''
}

export function getFileStem(fileName) {
  if (fileName.toLowerCase() === 'dockerfile') return 'Dockerfile'
  const extension = getFileExtension(fileName)
  return extension ? fileName.slice(0, -extension.length) : fileName
}

export function getDocumentRepresentation(fileName, capabilities) {
  if (capabilities?.representation) return capabilities.representation
  const extension = getFileExtension(fileName).toLowerCase()
  return SOURCE_TEXT_EXTENSIONS.includes(extension) ? 'sourceText' : 'extractedRichText'
}

export function supportedFormatsLabel() {
  return SUPPORTED_DOCUMENT_EXTENSIONS.join(' / ').toUpperCase()
}
