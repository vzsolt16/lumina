export const SUPPORTED_DOCUMENT_EXTENSIONS = ['.txt', '.md', '.pdf', '.docx']

export const SUPPORTED_DOCUMENT_ACCEPT = [
  ...SUPPORTED_DOCUMENT_EXTENSIONS,
  'text/plain',
  'text/markdown',
  'application/pdf',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
].join(',')

export function getFileExtension(fileName) {
  const dot = fileName.lastIndexOf('.')
  return dot > 0 ? fileName.slice(dot) : ''
}

export function getFileStem(fileName) {
  const extension = getFileExtension(fileName)
  return extension ? fileName.slice(0, -extension.length) : fileName
}

export function supportedFormatsLabel() {
  return SUPPORTED_DOCUMENT_EXTENSIONS.join(' / ').toUpperCase()
}
